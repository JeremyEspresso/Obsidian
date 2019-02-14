﻿using Newtonsoft.Json;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Events.EventArgs;
using Obsidian.Logging;
using Obsidian.Packets;
using Obsidian.Packets.Handshaking;
using Obsidian.Packets.Status;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/*

    What is?
    0x04 = Client Settings
    0x0A = Plugin Message
    0x00 = Teleport Confirm
    0x11 = Player Position And Look (serverbound)

    What do?
    0x04:
        Store the received client settings on player object
    0x0A:
        This is for communication with mods. Let's ignore this for now.
    0x00:
        Store the new coordinates to the player object
    0x11:
        store the new position and look (done)

    Now after connecting to the world, what do?!

    */

namespace Obsidian.Connection
{
    public class Client
    {
        private PacketState _state = PacketState.Handshaking;
        private bool Compressed = false;
        public Config Config;
        public int KeepAlives;
        public NetworkStream netstream;
        public Server OriginServer;
        public MinecraftPlayer Player;

        public Client(TcpClient tcp, Config config, Server origin)
        {
            this.Tcp = tcp;
            this.Cancellation = new CancellationTokenSource();
            this.Config = config;
            this.OriginServer = origin;
        }

        public CancellationTokenSource Cancellation { get; private set; }
        private Logger Logger => OriginServer.Logger;

        //current state of client
        public PacketState State
        {
            get => _state;
            set
            {
                _state = value;
                Logger.LogMessageAsync($"Client has been switched to state {_state.ToString()}").GetAwaiter().GetResult();
            }
        }

        public TcpClient Tcp { get; private set; }

        private async Task<CompressedPacket> GetNextCompressedPacketAsync(Stream stream)
        {
            return await CompressedPacket.ReadFromStreamAsync(stream);
        }

        private async Task<Packet> GetNextPacketAsync(Stream stream)
        {
            return await Packet.ReadFromStreamAsync(stream);
        }

        public void DisconnectClient()
        {
            Cancellation.Cancel();
        }

        ///Kicks a client with a reason
        public async Task DisconnectClientAsync(Chat reason)
        {
            var disconnect = new Disconnect(reason);
            var packet = new Packet();
            if (State == PacketState.Play)
            {
                packet = new Packet(0x1B, await disconnect.ToArrayAsync());
            }
            else
            {
                packet = new Packet(0x00, await disconnect.ToArrayAsync());
            }

            await packet.WriteToStreamAsync(Tcp.GetStream());

            DisconnectClient();
        }

        public async Task SendChatAsync(string message, byte position = 0)
        {
            var chat = Chat.Simple(message);
            var pack = new Packet(0x0E, await new ChatMessage(chat, position).ToArrayAsync());
            await pack.WriteToStreamAsync(netstream);
        }

        public async Task SendJoinGameAsync()
        {
            var pack = new Packet(0x25, await new JoinGame(0, 0, 1, 0, "default", true).ToArrayAsync());
            await pack.WriteToStreamAsync(netstream);
        }

        /// <summary>
        /// Sends KeepAlice
        /// </summary>
        /// <param name="id">ID for the keepalive. Just keep increasing this by 1, easiest approach.</param>
        /// <returns></returns>
        public async Task SendKeepAliveAsync(long id)
        {
            this.KeepAlives++;
            var pack = new Packet(0x21, await new KeepAlive(id).ToArrayAsync());
            await pack.WriteToStreamAsync(netstream);
        }

        public async Task SendPositionLookAsync(double x, double y, double z, float yaw, float pitch, PositionFlags flags, int teleportid)
        {
            var poslook = new PlayerPositionLook(x, y, z, yaw, pitch, flags, teleportid);
            var pack = new Packet(0x32, await poslook.ToArrayAsync());
            await pack.WriteToStreamAsync(netstream);
        }

        public async Task SendSoundEffectAsync(int soundId, Position position, SoundCategory category = SoundCategory.Master, float pitch = 1.0f, float volume = 1f)
        {
            await new Packet(0x4D, await new SoundEffect(soundId, position, category, pitch, volume).ToArrayAsync()).WriteToStreamAsync(netstream);
        }

        public async Task SendSpawnPositionAsync(Position position)
        {
            await Logger.LogMessageAsync("Sending Spawn Position packet.");
            var packet = new Packet(0x49, await new SpawnPosition(position).ToArrayAsync());
            await packet.WriteToStreamAsync(netstream);
        }

        public async Task StartClientConnection()
        {
            netstream = Tcp.GetStream();
            while (!Cancellation.IsCancellationRequested && Tcp.Connected)
            {
                Packet packet = null;
                Packet returnpack = null;

                if (Compressed)
                    packet = await GetNextCompressedPacketAsync(netstream);
                else
                    packet = await GetNextPacketAsync(netstream);

                await Logger.LogMessageAsync("Received a new packet.");

                await this.OriginServer.Events.InvokePacketReceived(new BaseMinecraftEventArgs(this, packet));

                if (packet.PacketLength == 0)
                {
                    this.DisconnectClient();
                }

                switch (State)
                {
                    case PacketState.Handshaking: //Intial state / beginning
                        switch (packet.PacketId)
                        {
                            case 0x00:
                                // Handshake
                                var handshake = await Handshake.FromArrayAsync(packet.PacketData);
                                var nextState = handshake.NextState;

                                if (nextState != PacketState.Status && nextState != PacketState.Login)
                                {
                                    await Logger.LogMessageAsync($"Client sent unexpected state (), forcing it to disconnect");
                                    await DisconnectClientAsync(new Chat() { Text = "you seem suspicious" });
                                }

                                State = nextState;
                                await Logger.LogMessageAsync($"Handshaking with client (protocol: {handshake.Version}, server: {handshake.ServerAddress}:{handshake.ServerPort})");
                                break;
                        }
                        break;

                    // TODO: encryption. using offline mode for now.
                    case PacketState.Login:
                        switch (packet.PacketId)
                        {
                            default:
                                await Logger.LogMessageAsync($"Client in state Login tried to send an unimplemented packet. Forcing it to disconnect.");
                                await this.DisconnectClientAsync(new Chat()
                                {
                                    Text = Config.JoinMessage
                                });
                                break;

                            case 0x00:
                                // Login start, expected uncompressed
                                var loginStart = await LoginStart.FromArrayAsync(packet.PacketData);
                                await Logger.LogMessageAsync($"Received login request from user {loginStart.Username}");

                                /*var isonline = this.OriginServer.CheckPlayerOnline(loginStart.Username);
                                if (isonline)
                                {
                                    // kick out the player
                                    await this.DisconnectClientAsync(Chat.Simple($"A player with usename {loginStart.Username} is already online!"));
                                }*/
                                this.Player = new MinecraftPlayer(loginStart.Username, 0, 0, 0);

                                // For offline mode, Respond with LoginSuccess (0x02) and switch state to Play.
                                var loginSuccess = new LoginSuccess("069a79f4-44e9-4726-a5be-fca90e38aaf5", loginStart.Username); // does this mean we can change usernames server-side??
                                await Logger.LogMessageAsync($"Sent Login success to User {loginStart.Username}");

                                // UUID for Notch
                                returnpack = new Packet(0x02, await loginSuccess.ToArrayAsync());
                                await returnpack.WriteToStreamAsync(netstream);

                                // Set packet state to play as indicated in the docs
                                this.State = PacketState.Play;

                                // Send Join Game packet
                                await Logger.LogMessageAsync("Sending Join Game packet.");
                                await SendJoinGameAsync();

                                // Send spawn location packet
                                await SendSpawnPositionAsync(new Position(0, 100, 0));

                                // Send position packet
                                await Logger.LogMessageAsync("Sending Position packet.");
                                await SendPositionLookAsync(0, 0, 0, 0, 0, PositionFlags.NONE, 0);

                                await Logger.LogMessageAsync("Player is logged in.");
                                await Logger.LogMessageAsync("Sending welcome msg");

                                await this.SendChatAsync("§dWelcome to Obsidian Test Build. §l§4<3", 2);
                                // Login success!
                                await this.OriginServer.SendChatAsync($"§l§4{this.Player.Username} has joined the server.", this, system: true);
                                await this.OriginServer.Events.InvokePlayerJoin(new PlayerJoinEventArgs(this, packet, DateTimeOffset.Now));
                                break;

                            case 0x01:
                                // Encryption response
                                break;

                            case 0x02:
                                // Login Plugin Response
                                break;
                        }
                        break;

                    case PacketState.Status: //server ping/list
                        switch (packet.PacketId)
                        {
                            case 0x00:
                                // Request
                                await Logger.LogMessageAsync("Received empty packet in STATUS state. Sending json status data.");
                                var res = new RequestResponse(JsonConvert.SerializeObject(ServerStatus.DebugStatus));
                                returnpack = new Packet(0x00, await res.GetDataAsync());
                                await returnpack.WriteToStreamAsync(netstream);
                                break;

                            case 0x01:
                                // Ping
                                var ping = await PingPong.FromArrayAsync(packet.PacketData); // afaik you can just resend the ping to the client
                                await Logger.LogMessageAsync($"Client sent us ping request with payload {ping.Payload}");

                                returnpack = new Packet(0x01, await ping.ToArrayAsync());
                                await returnpack.WriteToStreamAsync(netstream);
                                this.DisconnectClient();
                                break;
                        }
                        break;

                    case PacketState.Play: // Gameplay packets. Put this last because the list is the longest.
                        await Logger.LogMessageAsync($"Received Play packet with Packet ID 0x{packet.PacketId.ToString("X")}");
                        switch (packet.PacketId)
                        {
                            case 0x00:
                                // Teleport Confirm
                                // GET X Y Z FROM PACKET TODO
                                //this.Player.Position = new Position((int)x, (int)y, (int)z);
                                await Logger.LogMessageAsync("Received teleport confirm");
                                break;

                            case 0x01:
                                // Query Block NBT
                                await Logger.LogMessageAsync("Received query block nbt");
                                break;

                            case 0x02:
                                // Incoming chat message
                                var message = await IncomingChatMessage.FromArrayAsync(packet.PacketData);

                                await this.OriginServer.SendChatAsync(message.Message, this);
                                break;

                            case 0x03:
                                // Client status
                                await Logger.LogMessageAsync("Received client status");
                                break;

                            case 0x04:
                                // Client Settings
                                await Logger.LogMessageAsync("Received client settings");
                                break;

                            case 0x05:
                                // Tab-Complete
                                await Logger.LogMessageAsync("Received tab-complete");
                                break;

                            case 0x06:
                                // Confirm Transaction
                                await Logger.LogMessageAsync("Received confirm transaction");
                                break;

                            case 0x07:
                                // Enchant Item
                                await Logger.LogMessageAsync("Received enchant item");
                                break;

                            case 0x08:
                                // Click Window
                                await Logger.LogMessageAsync("Received click window");
                                break;

                            case 0x09:
                                // Close Window (serverbound)
                                await Logger.LogMessageAsync("Received close window");
                                break;

                            case 0x0A:
                                // Plugin Message (serverbound)
                                await Logger.LogMessageAsync("Received plugin message");
                                break;

                            case 0x0B:
                                // Edit Book
                                await Logger.LogMessageAsync("Received edit book");
                                break;

                            case 0x0C:
                                // Query Entity NBT
                                await Logger.LogMessageAsync("Received query entity nbt");
                                break;

                            case 0x0D:
                                // Use Entity
                                await Logger.LogMessageAsync("Received use entity");
                                break;

                            case 0x0E:
                                // Keep Alive (serverbound)
                                var keepalive = await KeepAlive.FromArrayAsync(packet.PacketData);
                                // Check whether keepalive id has been sent
                                await Logger.LogMessageAsync($"Successfully kept alive player {this.Player.Username} with ka id {keepalive.KeepAliveId}");
                                this.KeepAlives = 0;
                                break;

                            case 0x0F:
                                // Player
                                var onground = BitConverter.ToBoolean(packet.PacketData, 0);
                                await Logger.LogMessageAsync($"{Player.Username} on ground?: {onground}");
                                this.Player.OnGround = onground;
                                break;

                            case 0x10:
                                // Player Position
                                var pos = await PlayerPosition.FromArrayAsync(packet.PacketData);
                                this.Player.X = pos.X;
                                this.Player.Y = pos.Y;
                                this.Player.Z = pos.Z;
                                this.Player.OnGround = pos.OnGround;
                                await Logger.LogMessageAsync($"Updated position for {Player.Username}");
                                break;

                            case 0x11:
                                // Player Position And Look (serverbound)
                                var ppos = await PlayerPositionLook.FromArrayAsync(packet.PacketData);
                                this.Player.X = ppos.X;
                                this.Player.Y = ppos.Y;
                                this.Player.Z = ppos.Z;
                                this.Player.Yaw = ppos.Yaw;
                                this.Player.Pitch = ppos.Pitch;
                                await Logger.LogMessageAsync($"Updated look and position for {Player.Username}");
                                break;

                            case 0x12:
                                // Player Look
                                var look = await PlayerLook.FromArrayAsync(packet.PacketData);
                                this.Player.Yaw = look.Yaw;
                                this.Player.Pitch = look.Pitch;
                                this.Player.OnGround = look.OnGround;
                                await Logger.LogMessageAsync($"Updated look for {Player.Username}");
                                break;

                            case 0x13:
                                // Vehicle Move (serverbound)
                                await Logger.LogMessageAsync("Received vehicle move");
                                break;

                            case 0x14:
                                // Steer Boat
                                await Logger.LogMessageAsync("Received steer boat");
                                break;

                            case 0x15:
                                // Pick Item
                                await Logger.LogMessageAsync("Received pick item");
                                break;

                            case 0x16:
                                // Craft Recipe Request
                                await Logger.LogMessageAsync("Received craft recipe request");
                                break;

                            case 0x17:
                                // Player Abilities (serverbound)
                                await Logger.LogMessageAsync("Received player abilities");
                                break;

                            case 0x18:
                                // Player Digging
                                await Logger.LogMessageAsync("Received player digging");
                                break;

                            case 0x19:
                                // Entity Action
                                await Logger.LogMessageAsync("Received entity action");
                                break;

                            case 0x1A:
                                // Steer Vehicle
                                await Logger.LogMessageAsync("Received steer vehicle");
                                break;

                            case 0x1B:
                                // Recipe Book Data
                                await Logger.LogMessageAsync("Received recipe book data");
                                break;

                            case 0x1C:
                                // Name Item
                                await Logger.LogMessageAsync("Received name item");
                                break;

                            case 0x1D:
                                // Resource Pack Status
                                await Logger.LogMessageAsync("Received resource pack status");
                                break;

                            case 0x1E:
                                // Advancement Tab
                                await Logger.LogMessageAsync("Received advancement tab");
                                break;

                            case 0x1F:
                                // Select Trade
                                await Logger.LogMessageAsync("Received select trade");
                                break;

                            case 0x20:
                                // Set Beacon Effect
                                await Logger.LogMessageAsync("Received set beacon effect");
                                break;

                            case 0x21:
                                // Held Item Change (serverbound)
                                await Logger.LogMessageAsync("Received held item change");
                                break;

                            case 0x22:
                                // Update Command Block
                                await Logger.LogMessageAsync("Received update command block");
                                break;

                            case 0x23:
                                // Update Command Block Minecart
                                await Logger.LogMessageAsync("Received update command block minecart");
                                break;

                            case 0x24:
                                // Creative Inventory Action
                                await Logger.LogMessageAsync("Received creative inventory action");
                                break;

                            case 0x25:
                                // Update Structure Block
                                await Logger.LogMessageAsync("Received update structure block");
                                break;

                            case 0x26:
                                // Update Sign
                                await Logger.LogMessageAsync("Received update sign");
                                break;

                            case 0x27:
                                // Animation (serverbound)
                                await Logger.LogMessageAsync("Received animation (serverbound)");
                                break;

                            case 0x28:
                                // Spectate
                                await Logger.LogMessageAsync("Received spectate");
                                break;

                            case 0x29:
                                // Player Block Placement
                                await Logger.LogMessageAsync("Received player block placement");
                                break;

                            case 0x2A:
                                // Use Item
                                await Logger.LogMessageAsync("Received use item");
                                break;
                        }
                        break;
                }

                // will paste that in a txt
            }
            await Logger.LogMessageAsync($"Disconnected client");
            await this.OriginServer.SendChatAsync($"§l§4{this.Player.Username} has left the server.", this, 0, true);

            if (Tcp.Connected)
                this.Tcp.Close();
        }
    }
}