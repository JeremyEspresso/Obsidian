﻿using Obsidian.Net;
using System;
using System.Threading.Tasks;

namespace Obsidian.PlayerData.Info
{
    public class PlayerInfoAction
    {
        public Guid Uuid { get; set; }

        public virtual async Task WriteAsync(MinecraftStream stream) => await stream.WriteUuidAsync(this.Uuid);

        public virtual void Write(MinecraftStream stream) => stream.WriteUuid(Uuid);

        public virtual void Write(NetWriteStream stream) => stream.WriteGuid(Uuid);
    }
}