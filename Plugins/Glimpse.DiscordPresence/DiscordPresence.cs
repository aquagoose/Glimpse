using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DiscordRPC;
using Glimpse.API;
using Glimpse.API.UI;
using Hexa.NET.ImGui;
using MetaBrainz.MusicBrainz;
using MetaBrainz.MusicBrainz.CoverArt;
using MetaBrainz.MusicBrainz.CoverArt.Interfaces;
using MetaBrainz.MusicBrainz.Interfaces.Searches;

namespace Glimpse.DiscordPresence;

public partial class DiscordPresence : IPlugin
{
    private IGlimpse _glimpse;
    private bool _initialized;
    
    private string? _currentUrl;

    private DiscordConfig _config;
    
    public DiscordRpcClient Client;

    public bool IsInitialized => _initialized;

    public bool ConfigChanged => false; // the discord config is not currently editable

    public void OnGUI(IImmediateGUI ui)
    {
        ui.Table(["Album", "AlbumArt"], ctx =>
        {
            foreach ((string album, string albumArt) in _config.AlbumArt)
            {
                ctx.NewRow();
                ctx.NewColumn();
                ui.Text(album);
                ctx.NewColumn();
                ui.Text(albumArt);
            }
        });
    }

    public void Initialize(IGlimpse glimpse)
    {
        _glimpse = glimpse;
        _currentUrl = "glimpse";
        
        Client = new DiscordRpcClient("1280266653950804111");
        
        if (!_glimpse.ConfigManager.TryGetConfig("Discord", out _config))
        {
            _config = new DiscordConfig();
            _glimpse.ConfigManager.WriteConfig("Discord", _config);
        }
        
        Client.Initialize();
        
        _glimpse.Player.StateChanged += PlayerOnStateChanged;

        _initialized = true;
    }

    void PlayerOnStateChanged(TrackState state)
    {
        IAudioPlayer player = _glimpse.Player;
        
        switch (state)
        {
            case TrackState.Playing:
                SetPresence(player.CurrentTrack, player.ElapsedTime, player.TrackLength);
                break;
            
            case TrackState.Paused:
            case TrackState.Stopped:
                Client.ClearPresence();
                break;
        }
    }

    private void SetPresence(TrackInfo info, TimeSpan current, TimeSpan total)
    {
        _glimpse.Logger.Log($"Set discord presence to track: {info.Artist} - {info.Title}");
        _currentUrl = "glimpse";

        DateTime now = DateTime.UtcNow;
        
        RichPresence presence = new RichPresence()
            .WithType(ActivityType.Listening)
            .WithStatusDisplay(StatusDisplayType.State)
            .WithDetails(info.Title)
            .WithState(info.Artist)
            .WithTimestamps(new Timestamps(now - current, now + (total - current)))
            .WithAssets(new Assets() { LargeImageText = info.Album, LargeImageKey = _currentUrl });
        
        Client.SetPresence(presence);

        // Only search for new album art if the album changes or the URL is null.
        // This saves queries to musicbrainz.
        if (info.Album is { } albumName)
        {
            _glimpse.Logger.Log($"AlbumName: {albumName}");
            albumName = RemoveDiscNumberRegex().Replace(albumName, "");

            // insert the album artist in front of the name if available.
            // helps musicbrainz narrow down the search to stand a better chance of finding the correct album art
            // ignores "various artists" as that seems to trip musicbrainz up
            if (info.AlbumArtist != null && !info.AlbumArtist.Equals("various artists", StringComparison.CurrentCultureIgnoreCase))
                albumName = info.AlbumArtist + ' ' + albumName;

            _glimpse.Logger.Log($"Searching for: \"{albumName}\"");

            if (_config.AlbumArt.TryGetValue(albumName, out _currentUrl))
            {
                Client.UpdateLargeAsset(_currentUrl);
                return;
            }

            _glimpse.Logger.Log("  ... not found in album art cache, querying musicbrainz...");
            
            Task.Run(() =>
            {
                const string app = "com.aquagoose.glimpse";
                const string contact = "https://github.com/aquagoose";
                string version = _glimpse.Version.ToString();

                using Query query = new Query(app, version, contact);
                var releases = query.FindReleasesAsync(albumName, 5, simple: true).GetAwaiter().GetResult();
                using CoverArt art = new CoverArt(app, version, contact);

                foreach (ISearchResult<MetaBrainz.MusicBrainz.Interfaces.Entities.IRelease> release in releases.Results)
                {
                    IImage? image = null;
                    IRelease? coverArtRelease =
                        art.FetchReleaseIfAvailableAsync(release.Item.Id).GetAwaiter().GetResult();

                    if (coverArtRelease != null)
                    {
                        foreach (IImage img in coverArtRelease.Images)
                        {
                            if (img.Front)
                            {
                                image = img;
                                break;
                            }
                        }
                    }

                    if (image is null)
                        continue;

                    _currentUrl = image.Location?.ToString();
                    if (_currentUrl == null)
                        continue;

                    _glimpse.Logger.Log($"  ... found at {_currentUrl}");
                    _config.AlbumArt[albumName] = _currentUrl;
                    _glimpse.ConfigManager.WriteConfig("Discord", _config);

                    Client.UpdateLargeAsset(_currentUrl);
                    break;
                }
            });
        }
    }

    public void Dispose()
    {
        if (!_initialized)
            return;
        _initialized = false;

        _glimpse.Player.StateChanged -= PlayerOnStateChanged;
        Client.Dispose();
    }

    [GeneratedRegex(@"\s*([\[(]*)\s*(disc|cd)(\s*)\d+\s*([)\]]*)",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant)]
    private static partial Regex RemoveDiscNumberRegex();
}