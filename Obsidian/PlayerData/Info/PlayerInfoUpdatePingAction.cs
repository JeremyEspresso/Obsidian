﻿using Obsidian.Net;
using System.Threading.Tasks;

namespace Obsidian.PlayerData.Info
{
    public class PlayerInfoUpdatePingAction : PlayerInfoAction
    {
        public int Ping { get; set; }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

            await stream.WriteVarIntAsync(this.Ping);
        }

        public override void Write(MinecraftStream stream)
        {
            base.Write(stream);

            stream.WriteVarInt(Ping);
        }

        public override void Write(NetWriteStream stream)
        {
            base.Write(stream);

            stream.WriteVarInt(Ping);
        }
    }
}