using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DiscordRPC;
using Glimpse.Player.Configs;
using MetaBrainz.MusicBrainz;
using MetaBrainz.MusicBrainz.CoverArt;
using MetaBrainz.MusicBrainz.CoverArt.Interfaces;
using MetaBrainz.MusicBrainz.Interfaces.Searches;

namespace Glimpse.Player;

public static class DiscordPresence
{
    private static string _currentUrl;

    public static DiscordConfig Config;
    
    public static DiscordRpcClient Client;
    
    public static void Initialize()
    {
        Client = new DiscordRpcClient("1280266653950804111");

        if (!IConfig.TryGetConfig("Discord", out Config))
        {
            Config = new DiscordConfig();
            IConfig.WriteConfig("Discord", Config);
        }

#if !DEBUG
        Client.Initialize();
#endif
    }

    public static void SetPresence(TrackInfo info, double songLengthInSeconds)
    {
        if (!Client.IsInitialized)
            return;

        RichPresence presence = new RichPresence()
            .WithDetails(info.Title)
            .WithState(info.Artist)
            .WithTimestamps(Timestamps.FromTimeSpan(songLengthInSeconds))
            .WithAssets(new Assets() { LargeImageText = info.Album, LargeImageKey = _currentUrl });
        
        Client.SetPresence(presence);
        
        string albumName = info.Album;

        int startIndex = albumName.IndexOf("disc", StringComparison.OrdinalIgnoreCase);

        if (startIndex != -1)
        {
            int endIndex;
            bool foundNumber = false;

            for (endIndex = startIndex + "disc".Length; endIndex < albumName.Length; endIndex++)
            {
                char c = albumName[endIndex];

                if (c is not ' ' && c is < '0' or > '9')
                    break;

                foundNumber = true;
            }

            if (foundNumber)
                albumName = albumName.Remove(startIndex, endIndex - startIndex).Replace("()", "").Replace("[]", "").Trim();
        }

        if (Config.AlbumArt.TryGetValue(albumName, out _currentUrl))
        {
            Client.UpdateLargeAsset(_currentUrl);
            return;
        }

        // Only search for new album art if the album changes or the URL is null.
        // This saves queries to musicbrainz.
        if (info.Album != TrackInfo.UnknownAlbum)
        {
            Task.Run(() =>
            {
                const string app = "GlimpseAudioPlayer";
                const string contact = "https://github.com/aquagoose";
                const string version = "0.0.0-dev";

                using Query query = new Query(app, version, contact);
                var releases = query.FindReleases(albumName, 5);
                using CoverArt art = new CoverArt(app, version, contact);

                foreach (ISearchResult<MetaBrainz.MusicBrainz.Interfaces.Entities.IRelease> release in releases.Results)
                {
                    IImage image = null;

                    try
                    {
                        foreach (IImage img in art.FetchReleaseIfAvailable(release.Item.Id)?.Images)
                        {
                            if (img.Front)
                            {
                                image = img;
                                break;
                            }
                        }
                    }
                    catch (Exception) { }

                    if (image is not null)
                    {
                        _currentUrl = image.Location?.ToString();
                        Config.AlbumArt[albumName] = _currentUrl;
                        IConfig.WriteConfig("Discord", Config);
                        break;
                    }
                }
                
                Client.UpdateLargeAsset(_currentUrl);
            });
        }
    }

    public static void Deinitialize()
    {
        if (!Client.IsInitialized)
            return;
        
        Client.Dispose();
    }
}