﻿using System.Collections.Generic;
using Glimpse.Player.Configs;

namespace Glimpse.DiscordPresence;

public class DiscordConfig : IConfig
{
    public Dictionary<string, string> AlbumArt;

    public DiscordConfig()
    {
        AlbumArt = new Dictionary<string, string>();
    }
}