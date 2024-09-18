using System.Text.RegularExpressions;
using DiscordRPC;
using Glimpse.Api;  
using MetaBrainz.MusicBrainz;
using MetaBrainz.MusicBrainz.CoverArt;
using MetaBrainz.MusicBrainz.CoverArt.Interfaces;
using MetaBrainz.MusicBrainz.Interfaces.Searches;

namespace Glimpse.DiscordPresence;

public partial class DiscordPresence : Plugin
{
    private IGlimpse _glimpse;
    
    private static string _currentUrl;

    public DiscordConfig Config;
    
    public DiscordRpcClient Client;
    
    public override void Initialize(IGlimpse glimpse)
    {
        _glimpse = glimpse;
        
        Client = new DiscordRpcClient("1280266653950804111");
        
        if (!_glimpse.ConfigManager.TryGetConfig("Discord", out Config))
        {
            Config = new DiscordConfig();
            _glimpse.ConfigManager.WriteConfig("Discord", Config);
        }
        
        Client.Initialize();
        
        _glimpse.AudioPlayer.StateChanged += PlayerOnStateChanged;
    }

    void PlayerOnStateChanged(TrackState state)
    {
        IAudioPlayer player = _glimpse.AudioPlayer;
        
        switch (state)
        {
            case TrackState.Playing:
                SetPresence(player.CurrentTrack, player.ElapsedSeconds, player.TrackLength);
                break;
            
            case TrackState.Paused:
            case TrackState.Stopped:
                Client.ClearPresence();
                break;
        }
    }

    private void SetPresence(TrackInfo info, int currentSecond, int totalSeconds)
    {
        DateTime now = DateTime.UtcNow;
        
        RichPresence presence = new RichPresence()
            .WithDetails(info.Title)
            .WithState(info.Artist)
            .WithTimestamps(new Timestamps(now - TimeSpan.FromSeconds(currentSecond), now + TimeSpan.FromSeconds(totalSeconds)))
            .WithAssets(new Assets() { LargeImageText = info.Album, LargeImageKey = _currentUrl });
        
        Client.SetPresence(presence);
        
        string albumName = info.Album;
        Console.WriteLine($"AlbumName: {albumName}");
        albumName = RemoveDiscNumberRegex().Replace(albumName, "");
        Console.WriteLine(albumName);

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
                        _glimpse.ConfigManager.WriteConfig("Discord", Config);
                        break;
                    }
                }
                
                Client.UpdateLargeAsset(_currentUrl);
            });
        }
    }

    public override void Dispose()
    {
        Client.Dispose();
    }

    [GeneratedRegex(@"\s*([\[(]*)\s*(disc|cd)(\s*)\d+\s*([)\]]*)",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant)]
    private static partial Regex RemoveDiscNumberRegex();
}