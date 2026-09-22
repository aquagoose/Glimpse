using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Glimpse.API;
using Glimpse.Assets;
using Glimpse.API.Library;
using Glimpse.Audio;
using Glimpse.Configs;
using Glimpse.Graphics;
using Glimpse.Platforms;
using Hexa.NET.ImGui;
using piko.SDL3;
using Color = System.Drawing.Color;
using Image = Glimpse.Graphics.Image;
using Track = Glimpse.API.Library.Track;

namespace Glimpse.Forms;

public class GlimpsePlayer : Window
{
    private bool _init;
    private bool _needsRefresh;
    private ImGuiStyle _defaultStyle;
    private Theme.ThemeConfig _themeConfig;

    private List<CustomButton> _customButtons;

    private Size _restoreSize;
    private bool _miniplayer;

    private SemVer _newVersion;
    private string? _newVersionURL;
    private float _newVersionBlinker;
    
    private string? _currentAlbum;
    private AlbumView _currentView;
    private SizedCollection<Track> _currentTracks;
    private SizedCollection<string> _albums;

    private Playlist? _favoritesPlaylist;
    private Dictionary<string, Playlist>? _playlists;
    
    private bool _wasSeekClicked;
    private double? _seekPosition;
    private int _currentRowHover;
    private int _currentRatingHover;

    private Image _playButton;
    private Image _pauseButton;
    private Image _skipButton;
    private Image _stopButton;
    private Image _plusButton;
    private Image _star;
    private Image _starFilled;
    private Image _cogButton;
    private Image _bugButton;
    private Image _updateButton;
    private Image _shuffleButton;
    private Image _repeatButton;
    private Image _repeatOneButton;
    private Image _heart;
    private Image _heartFilled;

    private Image _defaultAlbumArt;
    private Image? _albumArt;

    private byte[]? _newAlbumArt;
    private bool _shouldDeleteArt;

    private Timer _playCountTimer;
    private bool _hasIncrementedPlayCount;
    
    public GlimpsePlayer()
    {
        _customButtons = [];
        
#if DEBUG
        Title = "Glimpse DEBUG";
#else
        Title = "Glimpse";
#endif
        Size = new Size(1100, 650);
        //Size = new Size(620, 400);
    }

    protected override unsafe void Initialize()
    {
        if (Glimpse.ConfigManager.TryGetConfig(StateConfig.ConfigName, out StateConfig state))
        {
            Position = state.Position;
            Size = state.Size;
            Maximized = state.Maximized;
        }

        _playButton = Renderer.CreateImage("asset://Icons.PlayButton.png");
        _pauseButton = Renderer.CreateImage("asset://Icons.PauseButton.png");
        _skipButton = Renderer.CreateImage("asset://Icons.SkipButton.png");
        _stopButton = Renderer.CreateImage("asset://Icons.StopButton.png");
        _plusButton = Renderer.CreateImage("asset://Icons.Plus.png");
        _star = Renderer.CreateImage("asset://Icons.Star.png");
        _starFilled = Renderer.CreateImage("asset://Icons.Star-Filled.png");
        _cogButton = Renderer.CreateImage("asset://Icons.Cog.png");
        _bugButton = Renderer.CreateImage("asset://Icons.Bug.png");
        _updateButton = Renderer.CreateImage("asset://Icons.Update.png");
        _shuffleButton = Renderer.CreateImage("asset://Icons.Shuffle.png");
        _repeatButton = Renderer.CreateImage("asset://Icons.Repeat.png");
        _repeatOneButton = Renderer.CreateImage("asset://Icons.RepeatOne.png");
        _heart = Renderer.CreateImage("asset://Icons.Heart.png");
        _heartFilled = Renderer.CreateImage("asset://Icons.Heart-Filled.png");
        
        Glimpse.Player.TrackChanged += PlayerOnTrackChanged;
        Glimpse.Player.StateChanged += PlayerOnStateChanged;
        Glimpse.Platform.ButtonPressed += PlatformOnButtonPressed;
        Glimpse.Platform.GetPosition += PlatformOnGetPosition;
        Glimpse.Library.Updated += () => _needsRefresh = true; // TODO: Make this a method

        const uint fontSize = 18;
        Renderer.ImGui.AddFont("Fonts.Roboto-Regular.ttf", fontSize);
        Renderer.ImGui.AddFont("Fonts.NotoSansJP-Regular.ttf", fontSize);
        Renderer.ImGui.AddFont("Fonts.NotoSansSC-Regular.ttf", fontSize);
        Renderer.ImGui.AddFont("Fonts.NotoSansKR-Regular.ttf", fontSize);
        Renderer.ImGui.AddFont("Fonts.NotoEmoji-Regular.ttf", fontSize);
        Renderer.ImGui.AddFont("Fonts.MaterialSymbolsOutlined-Regular.ttf", fontSize);
        
        ImGuiIOPtr io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.ConfigInputTrickleEventQueue = false;

        ImGuiStylePtr style = ImGui.GetStyle();
        _defaultStyle = *style.Handle;
        
        RefreshLayout();
        ChangeView(AlbumView.Albums);
        ChangeAlbum(null); // Change to the default album view where all tracks are displayed.

        // TODO: Call this function on track change (and perhaps in the main loop), not with a timer.
        _playCountTimer = new Timer(CheckIncrementPlayCount, null, 0, 1000);
        
        if (_currentTracks.Count == 0)
            AddPopup(new WelcomePopup());

#if !DISABLE_AUTOUPDATE
        // Only perform the update check if the user wants it!
        if (Glimpse.Config.General.EnableUpdateChecking)
            Task.Run(CheckForNewerVersion);
#endif
    }

    private void CheckIncrementPlayCount(object? state)
    {
        AudioPlayer player = Glimpse.Player;

        // Count after 60%
        // TODO: Make this adjustable.
        const double songConsumedPercentageBeforeIncrement = 0.6;

        if (player.CurrentTrack == null || player.TrackState != TrackState.Playing || _hasIncrementedPlayCount ||
            player.ConsumedTime.TotalSeconds < player.TrackLength.TotalSeconds * songConsumedPercentageBeforeIncrement)
        {
            return;
        }

        _hasIncrementedPlayCount = true;
        // TODO: I don't like this.
        if (Glimpse.Library.TryGetTrack(player.CurrentTrackPath, out Track? track))
        {
            track.PlayCount++;
            track.LastPlayed = DateTime.Now;
            Glimpse.Library.UpdateTrack(track);
        }
    }

    protected override unsafe void Update(float dt)
    {
        //ImGui.ShowStyleEditor();
        
        Locale locale = Glimpse.Locale;
        
        if (_newAlbumArt != null)
        {
            _albumArt?.Dispose();
            try
            {
                ImageLoadFlags flags = ImageLoadFlags.None;
                if (_themeConfig.AlbumArtGrayscale)
                    flags |= ImageLoadFlags.LoadGrayscale;
                
                _albumArt = Renderer.CreateImage(_newAlbumArt, flags);
            }
            catch (Exception e)
            {
                _albumArt = null;
                Glimpse.Logger.Log($"Failed to load album art: {e}");
            }

            _newAlbumArt = null;
        }
        else if (_shouldDeleteArt)
        {
            _shouldDeleteArt = false;
            
            _albumArt?.Dispose();
            _albumArt = null;
        }
        
        AudioPlayer player = Glimpse.Player;
        
        // Perform seeking if necessary 
        if (_wasSeekClicked)
        {
            Debug.Assert(_seekPosition is not null);
            player.Seek(_seekPosition.Value);
            _seekPosition = null;
            _wasSeekClicked = false;
        }
        
        if (Glimpse.Library.IsIndexing || _needsRefresh)
        {
            _needsRefresh = false;
            ChangeAlbum(_currentAlbum);
            ChangeView(_currentView);
        }

        Renderer.Clear(Color.Black);
        
/*#if DEBUG
        if (ImGui.BeginMainMenuBar())
        {
            ImGui.TextUnformatted("DEBUG Menu");

            ImGui.Spacing();
            
            if (ImGui.MenuItem("Style Editor"))
                AddPopup(new StyleEditorPopup());
            
            if (ImGui.MenuItem("Settings"))
                AddPopup(new SettingsPopup());
            
            ImGui.EndMainMenuBar();
        }
#endif*/

        const uint centralNode = 1 << 11;
        const uint noTabBar = 1 << 12;

        uint id = ImGui.DockSpaceOverViewport(ImGui.GetMainViewport(),
            ImGuiDockNodeFlags.PassthruCentralNode | (ImGuiDockNodeFlags) noTabBar);
        
        if (!_init)
        {
            _init = true;
            
            ImGuiP.DockBuilderRemoveNode(id);
            ImGuiP.DockBuilderAddNode(id, ImGuiDockNodeFlags.NoUndocking);
            uint transportId = id;

            if (!_miniplayer)
            {
                ImGuiDir dir = Glimpse.Config.Appearance.SwapTransportControls ? ImGuiDir.Up : ImGuiDir.Down;
                
                uint albumsSongsId;
                ImGuiP.DockBuilderSplitNode(id, dir, 0, &transportId, &albumsSongsId);

                ImGuiDockNodePtr transportNode = ImGuiP.DockBuilderGetNode(transportId);
                transportNode.LocalFlags = ImGuiDockNodeFlags.NoResize;
                transportNode.SizeRef = ScaleVec(1100, 122);
            
                uint albumsId;
                uint songsId;
                ImGuiP.DockBuilderSplitNode(albumsSongsId, ImGuiDir.Left, 0, &albumsId, &songsId);

                ImGuiDockNodePtr albumsNode = ImGuiP.DockBuilderGetNode(albumsId);
                albumsNode.SizeRef = ScaleVec(327, 650);
                //albumsNode.SizeRef = ScaleVec(200, 650);

                ImGuiDockNodePtr songsNode = ImGuiP.DockBuilderGetNode(songsId);
                songsNode.SizeRef = ScaleVec(772, 650);
                songsNode.LocalFlags = (ImGuiDockNodeFlags) centralNode;
                
                ImGuiP.DockBuilderDockWindow("Albums", albumsId);
                ImGuiP.DockBuilderDockWindow("Songs", songsId);
            }

            ImGuiP.DockBuilderDockWindow("Transport", transportId);
        
            ImGuiP.DockBuilderFinish(id);
        }

        Vector4 iconsColor = ImGui.GetStyle().Colors[(int) ImGuiCol.Text];

        bool switchToTrackList = false;
        bool switchToQueueView = false;
        AlbumView? switchView = null;
        
        #region Transport Dock
        
        if (ImGui.Begin("Transport", ImGuiWindowFlags.NoResize))
        {
            Vector2 winSize = ImGui.GetContentRegionAvail();

            Image albumArt = _albumArt ?? _defaultAlbumArt;
            Vector2 size;
            
            if (Glimpse.Config.Appearance.ConfineAlbumArtToSquare)
            {
                // If aspect ratio >1 then it is too wide and we must scale it by the width.
                // Otherwise scale by the height.
                double aspectRatio = albumArt.Width / (double) albumArt.Height;
                float scale = aspectRatio > 1 ? winSize.Y / albumArt.Width : winSize.Y / albumArt.Height;
                size = new Vector2(albumArt.Width, albumArt.Height) * scale;
            }
            else
            {
                // Always scale by the height, unless the image is larger than the maximum aspect ratio
                // in which case calculate the maximum allowed width in screen coordinates and scale the image using that.
                const double maxAspectRatio = 16.0 / 9.0;
                float scale = winSize.Y / albumArt.Height;
                size = new Vector2(albumArt.Width, albumArt.Height) * scale;
                float maxAlbumWidth = winSize.Y * (float) maxAspectRatio;
                if (size.X > maxAlbumWidth)
                {
                    float sizeScale = maxAlbumWidth / size.X;
                    size *= sizeScale;
                }
            }

            ImGui.BeginChild("AlbumArt", new Vector2(size.X, winSize.Y));
            {
                ImGui.SetCursorPosY(winSize.Y / 2 - size.Y / 2);
                ImGui.Image(albumArt, size);
                ImGui.EndChild();
            }
            
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsItemClicked())
            {
                Size windowSize = Size;

                if (_restoreSize.IsEmpty)
                    _restoreSize = new Size(470, 122);
                    
                _miniplayer = !_miniplayer;
                Size = _restoreSize;
                _restoreSize = windowSize;
                RefreshLayout();
            }
            
            ImGui.SameLine();

            ImGui.BeginChild("MainView");
            {
                ImGui.BeginChild("TrackInfo", ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.AutoResizeY);
                {
                    if (player.TrackState == TrackState.Stopped)
                    {
                        ImGui.TextUnformatted(locale.GetString("Glimpse"));
                        ImGui.TextUnformatted("");
                        ImGui.TextUnformatted("");
                    }
                    else
                    {
                        // TODO: Scroll to the selected track & album/artist
                        if (ImGui.TextButton(player.CurrentTrack?.Title ?? locale.GetString("UnknownTrack")) &&
                            player.CurrentTrack?.Album != null)
                        {
                            switchToQueueView = true;
                        }

                        if (ImGui.TextButton(player.CurrentTrack?.Artist ?? locale.GetString("UnknownArtist")) &&
                            player.CurrentTrack?.Artist != null)
                        {
                            switchToTrackList = true;
                            switchView = AlbumView.Artists;
                            ChangeView(AlbumView.Artists);
                            ChangeAlbum(player.CurrentTrack.Artist);
                        }

                        if (ImGui.TextButton(player.CurrentTrack?.Album ?? locale.GetString("UnknownAlbum")) &&
                            player.CurrentTrack?.Album != null)
                        {
                            switchToTrackList = true;
                            switchView = AlbumView.Albums;
                            ChangeView(AlbumView.Albums);
                            ChangeAlbum(player.CurrentTrack.Album);
                        }
                    }

                    ImGui.EndChild();
                }

                ImGui.SameLine();

                float miniplayerScale = _miniplayer ? 0.75f : 1.0f;
                
                Vector2 iconSize = new Vector2(32) * Scale * miniplayerScale;
                // there are 5 icons but 4 makes it centered. no i don't know why!
                // miniplayer moves the heart next to the shuffle/repeat icons, so we reduce the icon count by 1.
                int numIcons = _miniplayer ? 3 : 4;
                float spacing = ImGui.GetStyle().ItemSpacing.X * miniplayerScale;
                float padding = ImGui.GetStyle().FramePadding.X * miniplayerScale;
                float totalButtonsWidth = (iconSize.X + spacing + padding) * numIcons;

                Vector2 centerPos;
                if (_miniplayer)
                {
                    centerPos = new Vector2(winSize.X - totalButtonsWidth - 15, ImGui.GetCursorScreenPos().Y + (int) (40 * Scale));
                }
                else
                {
                    centerPos = new Vector2(winSize.X / 2 - totalButtonsWidth / 2, ImGui.GetCursorScreenPos().Y + (int) (10 * Scale));
                }

                ImGui.SetCursorScreenPos(centerPos);

                ImGui.BeginChild("TransportControls", ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.AutoResizeY);
                {
                    //Vector2 centerPos = new Vector2(Size.Width / 2, ImGui.GetCursorScreenPos().Y);
                    //float padding = ImGui.GetStyle().WindowPadding.X + 10;

                    ImGui.BeginDisabled(player.TrackState == TrackState.Stopped);

                    Vector4 buttonColor = *ImGui.GetStyleColorVec4(ImGuiCol.Button);

                    ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonColor);

                    if (!_miniplayer)
                    {
                        FavoriteButton(player, locale, iconSize, iconsColor);
                        ImGui.SameLine();
                    }

                    if (ImGui.ImageButton("BackwardButton", _skipButton, iconSize, new Vector2(1, 0),
                            new Vector2(0, 1), Vector4.Zero, iconsColor))
                    {
                        player.Previous();
                    }

                    ImGui.SameLine();

                    if (player.TrackState == TrackState.Playing)
                    {
                        if (ImGui.ImageButton("PauseButton", _pauseButton, iconSize, Vector4.Zero, iconsColor))
                            player.Pause();
                    }
                    else
                    {
                        if (ImGui.ImageButton("PlayButton", _playButton, iconSize, Vector4.Zero, iconsColor))
                            player.Play();
                    }

                    ImGui.SameLine();

                    if (ImGui.ImageButton("ForwardButton", _skipButton, iconSize, Vector4.Zero, iconsColor))
                    {
                        player.Next();
                    }

                    if (!_miniplayer)
                    {
                        ImGui.SameLine();
                        if (ImGui.ImageButton("StopButton", _stopButton, iconSize, Vector4.Zero, iconsColor))
                        {
                            player.Stop();
                        }
                    }

                    ImGui.PopStyleColor();
                    ImGui.PopStyleColor();

                    ImGui.EndDisabled();

                    ImGui.EndChild();
                }

                Vector2 cursorPos = ImGui.GetCursorPos();

                Vector2 contentRegion = ImGui.GetContentRegionAvail();
                ImGui.SetCursorPos(new Vector2(contentRegion.X - (_miniplayer ? 75 : 150) * Scale, 20));
                //if (!_miniplayer)
                {
                    ImGui.BeginChild("VolumeDock", ImGuiChildFlags.AutoResizeY);
                    {
                        ShuffleMode shuffle = player.Shuffle;
                        RepeatMode repeat = player.Repeat;

                        Vector4 shuffleButtonTint = iconsColor;
                        if (shuffle == ShuffleMode.Off)
                            shuffleButtonTint.W = 0.5f;

                        Vector4 repeatButtonTint = iconsColor;
                        if (repeat == RepeatMode.Off)
                            repeatButtonTint.W = 0.5f;

                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));

                        // move the favorite button up to the "volume dock" if we're in the mini player
                        if (_miniplayer)
                        {
                            FavoriteButton(player, locale, ScaleVec(16) * miniplayerScale, iconsColor);
                            ImGui.SameLine(0, 0);
                        }

                        if (ImGui.ImageButton("ShuffleButton", _shuffleButton, ScaleVec(16) * miniplayerScale, Vector4.Zero, shuffleButtonTint))
                        {
                            player.Shuffle = shuffle switch
                            {
                                ShuffleMode.Off => ShuffleMode.Default,
                                ShuffleMode.Default => ShuffleMode.Off,
                                _ => throw new ArgumentOutOfRangeException()
                            };
                        }
                        ImGui.SameLine(0, 0);
                        if (ImGui.ImageButton("RepeatButton", repeat == RepeatMode.RepeatOne ? _repeatOneButton : _repeatButton, ScaleVec(16) * miniplayerScale, Vector4.Zero, repeatButtonTint))
                        {
                            // loop the repeat mode around
                            player.Repeat = repeat switch
                            {
                                RepeatMode.Off => RepeatMode.RepeatQueue,
                                RepeatMode.RepeatQueue => RepeatMode.RepeatOne,
                                RepeatMode.RepeatOne => RepeatMode.Off,
                                _ => throw new ArgumentOutOfRangeException()
                            };
                        }
                        ImGui.PopStyleColor();

                        //if (!_miniplayer)
                        {
                            float volume = Glimpse.Player.Volume;
                            string format;

                            ImGuiSliderFlags sliderFlags = ImGuiSliderFlags.None;
                            if (_miniplayer)
                            {
                                ImGui.EndChild();
                                ImGui.SetCursorPos(new Vector2(contentRegion.X - 10, 0));
                                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4));
                                ImGui.PushFont(ImFontPtr.Null, 60);
                                sliderFlags |= (ImGuiSliderFlags) ImGuiSliderFlagsPrivate.Vertical;
                                ImGui.SetNextItemWidth(10);
                                format = "";
                            }
                            else
                            {
                                ImGui.SameLine(0, 2);
                                contentRegion = ImGui.GetContentRegionAvail();
                                ImGui.SetNextItemWidth(contentRegion.X);
                                format = ((int) (volume * 100)).ToString();
                            }

                            if (ImGui.SliderFloat("##Volume", ref volume, 0, 1, format, sliderFlags))
                            {
                                Glimpse.Player.Volume = volume;
                                Glimpse.Config.Audio.Volume = volume;
                            }

                            if (_miniplayer)
                            {
                                ImGui.PopFont();
                                ImGui.PopStyleVar();
                            }
                        }

                        if (!_miniplayer)
                            ImGui.EndChild();
                    }
                }

                // TODO: HACK
                ImGui.SetCursorPos(cursorPos);
                ImGui.BeginChild("SongPosition");
                {
                    float cursorPosY = ImGui.GetCursorPosY() + (int) (10 * Scale);
                    contentRegion = ImGui.GetContentRegionAvail();

                    float align = ImGui.GetStyle().FramePadding.Y;

                    double position = _seekPosition ?? player.ElapsedTime.TotalSeconds;
                    double length = player.TrackLength.TotalSeconds;
                    
                    string elapsedText = Utils.FormatTimespan(player.ElapsedTime);
                    string lengthText = Utils.FormatTimespan(player.TrackLength);

                    Vector2 elapsedTextSize = ImGui.CalcTextSize(elapsedText);
                    Vector2 lengthTextSize = ImGui.CalcTextSize(lengthText);

                    ImGui.SetCursorPosY(cursorPosY + align);
                    ImGui.TextUnformatted(elapsedText);
                    ImGui.SameLine();
                    
                    // TODO: Realllly need to work out a better way of working out positions rather than randomly
                    //   throwing numbers around and hoping it looks right. Fully expecting to run into a major MAJOR
                    //   headache some day.
                    ImGui.SetCursorPosY(cursorPosY + 7 * Scale);
                    Vector2 globalCursorPos = ImGui.GetCursorScreenPos();
                    float width = contentRegion.X - elapsedTextSize.X - lengthTextSize.X - (int) (20 * Scale);
                    ImGui.ProgressBar((float) (position / length), new Vector2(width, 10 * Scale), "");
                    
                    // ProgressBars in ImGui don't have any slider-like behaviours. Before we were using a slider and it
                    // worked well, but progress bars look so much better.
                    // We have to hack the slider-like behaviour in.
                    
                    // Start seeking when the progress bar is hovered OR if a seek has already been requested, so that
                    // if the user moves their mouse away from the bar, it will continue seeking as long as they are
                    // holding the mouse button
                    // TODO: The hitbox is too small. Increase the size of the hitbox
                    if ((ImGui.IsItemHovered() || _seekPosition != null) && player.TrackState != TrackState.Stopped)
                    {
                        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
                        {
                            // Calculate the mouse position relative to the bar then turn it into a 0-1 range.
                            float xFraction = (ImGui.GetMousePos().X - globalCursorPos.X) / width;
                            xFraction = float.Clamp(xFraction, 0.0f, 1.0f);
                            _seekPosition = length * xFraction;
                        }
                        else if (_seekPosition != null) // Only seek once the mouse button is let go.
                            _wasSeekClicked = true;
                    }
                    else
                    {
                        _seekPosition = null;
                        _wasSeekClicked = false;
                    }

                    ImGui.SameLine();
                    ImGui.SetCursorPosY(cursorPosY);
                    ImGui.TextUnformatted(lengthText);

                    ImGui.EndChild();
                }

                ImGui.EndChild();
            }
        }
        ImGui.End();
        
        #endregion

        if (_miniplayer)
            return;
        
        #region Albums Dock
        
        if (ImGui.Begin("Albums", ImGuiWindowFlags.HorizontalScrollbar))
        {
            /*string newDirectory = null;

            if (ImGui.Selectable(".."))
                newDirectory = Path.GetDirectoryName(_currentDirectory);
            
            foreach (string directory in _directories)
            {
                if (ImGui.Selectable(Path.GetFileName(directory)))
                    newDirectory = directory;
            }
            
            if (newDirectory != null)
                ChangeDirectory(newDirectory);*/

            Vector2 contentRegion = ImGui.GetContentRegionAvail() - ImGui.GetStyle().ItemSpacing;
            const float split = 0.6f;
            
            ImGui.BeginDisabled();
            
            string str = "";
            ImGui.SetNextItemWidth(contentRegion.X * split);
            ImGui.InputTextWithHint("##SearchBox", locale.GetString("Player.SearchBar"), ref str, 1000);
            
            ImGui.EndDisabled();
            
            ImGui.SameLine();
            
            /*ImGui.SetNextItemWidth(contentRegion.X * (1.0f - split));
            string preview = _currentView switch
            {
                AlbumView.Albums => locale.GetString("Player.ViewSelect.Albums"),
                AlbumView.Artists => locale.GetString("Player.ViewSelect.Artists"),
                _ => throw new ArgumentOutOfRangeException()
            };
            if (ImGui.BeginCombo("##DisplaySelector", preview))
            {
                if (ImGui.Selectable(locale.GetString("Player.ViewSelect.Albums")))
                {
                    _currentView = AlbumView.Albums;
                    _currentAlbum = ShowAllString;
                }

                if (ImGui.Selectable(locale.GetString("Player.ViewSelect.Artists")))
                {
                    _currentView = AlbumView.Artists;
                    _currentAlbum = ShowAllString;
                }
                //ImGui.Selectable("Playlists");
                
                ImGui.EndCombo();
            }*/
            
            if (ImGui.BeginTabBar("AlbumTabs"))
            {
                //ImGui.PushFont(_iconsFont, 32);

                //if (switchView is AlbumView view)
                //    _currentView = view;

                if (ImGui.BeginTabItemTooltip("\ue019##Albums", locale.GetString("Player.ViewSelect.Albums"), switchView is AlbumView.Albums))
                {
                    if (_currentView != AlbumView.Albums)
                        ChangeView(AlbumView.Albums);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItemTooltip("\ue01a##Artists", locale.GetString("Player.ViewSelect.Artists"), switchView is AlbumView.Artists))
                {
                    if (_currentView != AlbumView.Artists)
                        ChangeView(AlbumView.Artists);
                    ImGui.EndTabItem();
                }
                
                if (ImGui.BeginTabItemTooltip("\ue521##Genres", locale.GetString("Player.ViewSelect.Genres"), switchView is AlbumView.Genres))
                {
                    if (_currentView != AlbumView.Genres)
                        ChangeView(AlbumView.Genres);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItemTooltip("\ue05f##Playlists", locale.GetString("Player.ViewSelect.Playlists")))
                {
                    if (_currentView != AlbumView.Playlists)
                        ChangeView(AlbumView.Playlists);
                    ImGui.EndTabItem();
                }

                // make this a common function as the Favorites playlist is separate from the other buttons yet requires
                // *most of* the same context menu functionality
                void ShowAlbumEntrySharedContextMenu(string albumName)
                {
                    if (ImGui.Selectable(locale.GetString("Menu.PlayAll")))
                    {
                        if (TryGetTracks(albumName, out HashSet<string>? tracks))
                        {
                            // turn shuffle off as the player will auto shuffle once playback starts.
                            player.Shuffle = ShuffleMode.Off;
                            player.QueueTracks(tracks, QueueSlot.Clear);
                            int trackIndex = 0;
                            while (!player.TryChangeTrack(trackIndex++)) ;
                            player.Play();
                        }
                    }

                    if (ImGui.Selectable(locale.GetString("Menu.ShuffleAll")))
                    {
                        if (TryGetTracks(albumName, out HashSet<string>? tracks))
                        {
                            int numTracks = tracks.Count;
                            player.Shuffle = ShuffleMode.Default;
                            player.QueueTracks(tracks, QueueSlot.Clear);
                            while (!player.TryChangeTrack(Random.Shared.Next(numTracks))) ;
                            player.Play();
                        }
                    }

                    if (ImGui.Selectable(locale.GetString("Menu.AddAllToQueue")))
                    {
                        if (TryGetTracks(albumName, out HashSet<string>? tracks))
                        {
                            player.QueueTracks(tracks, QueueSlot.AtEnd);
                            if (player.TrackState == TrackState.Stopped)
                            {
                                int trackIndex = 0;
                                while (!player.TryChangeTrack(trackIndex++)) ;
                                player.Play();
                            }
                        }
                    }
                }
                
                ImGui.BeginChild("AlbumList", ImGuiWindowFlags.HorizontalScrollbar);
                {
                    switch (_currentView)
                    {
                        case AlbumView.Playlists:
                        {
                            if (ImGui.Selectable(locale.GetString("Player.Playlists.New")))
                                AddPopup(new NewPlaylistPopup(name => CreatePlaylist(name, null)));

                            if (ImGui.Selectable(locale.GetString("Player.Playlists.Favorites"), _currentAlbum == IMusicLibrary.FavoritesPlaylistName))
                                ChangeAlbum(IMusicLibrary.FavoritesPlaylistName);

                            if (ImGui.BeginPopupContextItem())
                            {
                                ShowAlbumEntrySharedContextMenu(IMusicLibrary.FavoritesPlaylistName);
                                ImGui.EndPopup();
                            }

                            ImGui.Separator();
                            break;
                        }
                        default:
                        {
                            if (ImGui.Selectable(locale.GetString("Player.Albums.ShowAll"), _currentAlbum == null))
                            {
                                ChangeAlbum(null);
                                switchToTrackList = true;
                            }

                            break;
                        }
                    }

                    ImGuiListClipperPtr clipper = ImGui.ImGuiListClipper();
                    clipper.Begin((int) _albums.Count);
                    
                    while (clipper.Step())
                    {
                        IEnumerable<string> albumsRange =
                            _albums.Collection.Take(new Range(clipper.DisplayStart, clipper.DisplayEnd));

                        foreach (string albumName in albumsRange)
                        {
                            // typically the displayed name is the same as the album name, unless it's the special
                            // empty string, in which case display the "No Album" name instead
                            string displayedAlbumName = albumName;
                            if (albumName == string.Empty)
                                displayedAlbumName = locale.GetString("Player.Albums.NoAlbum");
                            
                            if (ImGui.Selectable(displayedAlbumName, _currentAlbum == albumName))
                            {
                                ChangeAlbum(albumName);
                                switchToTrackList = true;
                            }

                            if (ImGui.BeginPopupContextItem())
                            {
                                ShowAlbumEntrySharedContextMenu(albumName);

                                switch (_currentView)
                                {
                                    case AlbumView.Playlists:
                                    {
                                        ImGui.Separator();

                                        if (ImGui.Selectable(locale.GetString("Menu.DeletePlaylist")))
                                        {
                                            AddPopup(new MessageBoxPopup(MessageBoxPopup.Buttons.YesNo,
                                                locale.GetString("Popup.DeletePlaylist.Name"),
                                                locale.GetString("Popup.DeletePlaylist.Text", albumName), () =>
                                                {
                                                    if (!Glimpse.Library.TryDeletePlaylist(albumName))
                                                    {
                                                        AddPopup(new MessageBoxPopup(MessageBoxPopup.Buttons.Ok,
                                                            locale.GetString("Popup.DeletePlaylist.Failed.Name"),
                                                            locale.GetString("Popup.DeletePlaylist.Failed.Text")));
                                                    }

                                                    // we're already in the album view so don't need to perform the check.
                                                    ChangeView(AlbumView.Albums);
                                                }));
                                        }

                                        break;
                                    }
                                }
                            
                                // TODO: Reimplement album removal & deletion
                                /*if (ImGui.Selectable(locale.GetString("Menu.RemoveFromLibrary")))
                                    AddPopup(new RemoveTrackPopup(albumName, true, false));
                                if (Glimpse.Config.General.EnableFileDeletion && ImGui.Selectable(locale.GetString("Menu.DeleteAlbum")))
                                    AddPopup(new RemoveTrackPopup(albumName, true, true));*/
                            
                                ImGui.EndPopup();
                            }
                        }
                    }
                    
                    ImGui.EndChild();
                }
                
                ImGui.EndTabBar();
            }
        }
        ImGui.End();
        
        #endregion
        
        #region Songs Dock
        
        if (ImGui.Begin("Songs"))
        {
            /*foreach (string file in _files)
            {
                if (ImGui.Selectable(Path.GetFileName(file)))
                {
                    player.ChangeTrack(file);
                    player.Play();
                }
            }*/

            if (ImGui.BeginTabBar("SongsTabs"))
            {
                Vector2 currentCursorPos = ImGui.GetCursorPos();
                Vector2 contentRegion = ImGui.GetContentRegionAvail();

                bool updateAvailable = _newVersionURL != null;

                int numButtons = updateAvailable ? 4 : 3;
                numButtons += _customButtons.Count;
                float totalButtonWidth = numButtons * ((16 * Scale) + (ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X));
                
                ImGui.SetCursorPos(new Vector2(contentRegion.X - (int) totalButtonWidth + 15 * Scale, (int) (5 * Scale)));
                ImGui.BeginChild("SettingsButtons");
                {
                    foreach (CustomButton button in _customButtons)
                    {
                        if (ImGui.ImageButton(button.Name, button.Image, ScaleVec(16), Vector4.Zero, iconsColor))
                            button.OnClick();
                        
                        if (button.Tooltip != null)
                            ImGui.SetItemTooltipUnformatted(button.Tooltip);
                        
                        ImGui.SameLine();
                    }
                    
                    if (updateAvailable)
                    {
                        Vector4 buttonColor = *ImGui.GetStyleColorVec4(ImGuiCol.Button);
                        Vector4 highlightColor = new Vector4(1, 0, 0, 1);
                        float amount = (float.Sin(_newVersionBlinker) + 1) / 2;
                        
                        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Lerp(buttonColor, highlightColor, amount));
                        if (ImGui.ImageButton("Update", _updateButton, ScaleVec(16), Vector4.Zero, iconsColor)) Utils.OpenLink(_newVersionURL);
                        
                        ImGui.SetItemTooltipUnformatted(locale.GetString("Player.UpdateAvailable", _newVersion));
                        
                        ImGui.PopStyleColor();
                        ImGui.SameLine();
                        
                        _newVersionBlinker += dt * 2;
                        if (_newVersionBlinker >= float.Pi * 2)
                            _newVersionBlinker -= float.Pi * 2;
                    }

                    if (ImGui.ImageButton("ReportBug", _bugButton, ScaleVec(16), Vector4.Zero, iconsColor)) Utils.OpenLink("https://github.com/aquagoose/Glimpse/issues/new?template=bug_report.md");

                    ImGui.SetItemTooltipUnformatted(locale.GetString("Player.ReportBug"));
                    
                    ImGui.SameLine();
                    
                    if (ImGui.ImageButton("Settings", _cogButton, ScaleVec(16), Vector4.Zero, iconsColor))
                        AddPopup(new SettingsPopup());
                    ImGui.SetItemTooltipUnformatted(locale.GetString("Player.Settings"));
            
                    ImGui.SameLine();
            
                    if (ImGui.ImageButton("AddDirs", _plusButton, ScaleVec(16), Vector4.Zero, iconsColor))
                        //AddPopup(new AddFolderPopup());
                        AddPopup(new ManageLibraryPopup());
                    ImGui.SetItemTooltipUnformatted(locale.GetString("Player.AddDirs"));
                    
                    ImGui.EndChild();
                }
                
                ImGui.SetCursorPos(currentCursorPos);
                
                ImGuiTabItemFlags trackFlags =
                    switchToTrackList ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                
                if (ImGui.BeginTabItem(locale.GetString("Player.Tab.Tracks"), trackFlags))
                {
                    if (ImGui.BeginTable("SongTable", 9, ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX | ImGuiTableFlags.RowBg))
                    {
                        const int ratingColumn = 6;
                        ImGui.TableSetupColumn(locale.GetString("Track"), ImGuiTableColumnFlags.WidthFixed,  40.0f * Scale);
                        ImGui.TableSetupColumn(locale.GetString("Title"), ImGuiTableColumnFlags.WidthFixed, 265.0f * Scale);
                        ImGui.TableSetupColumn(locale.GetString("Artist"), ImGuiTableColumnFlags.WidthFixed, 160.0f * Scale);
                        ImGui.TableSetupColumn(locale.GetString("Album"), ImGuiTableColumnFlags.WidthFixed, 195.0f * Scale);
                        ImGui.TableSetupColumn(locale.GetString("Length"), ImGuiTableColumnFlags.WidthFixed, 48.0f * Scale);
                        ImGui.TableSetupColumn(locale.GetString("Plays"), ImGuiTableColumnFlags.WidthFixed, 40.0f * Scale);
                        ImGui.TableSetupColumn(locale.GetString("Rating"), ImGuiTableColumnFlags.WidthFixed, 85.0f * Scale);
                        ImGui.TableSetupColumn(locale.GetString("LastPlayed"), ImGuiTableColumnFlags.WidthFixed, 160.0f * Scale);
                        ImGui.TableSetupColumn(locale.GetString("FileName"), ImGuiTableColumnFlags.WidthFixed, 300.0f * Scale);
                        
                        ImGui.TableSetupScrollFreeze(0, 1);
                        
                        ImGui.TableHeadersRow();

                        string currentTrackPath = Glimpse.Player.CurrentTrackPath;
                        //int songEntryHeight = (int) (25 * Scale);

                        ImGuiListClipperPtr clipper = ImGui.ImGuiListClipper();
                        clipper.Begin((int) _currentTracks.Count/*, songEntryHeight*/);
                        while (clipper.Step())
                        {
                            int song = clipper.DisplayStart;
                            IEnumerable<Track> visibleTracks =
                                _currentTracks.Collection.Take(new Range(clipper.DisplayStart, clipper.DisplayEnd));
                            foreach (Track track in visibleTracks)
                            {
                                ImGui.TableNextRow(/*songEntryHeight*/);
                                int currentRow = ImGui.TableGetRowIndex();

                                //Console.WriteLine(song);

                                ImGui.TableNextColumn();
                                // special case for playlists
                                // instead of displaying the track number in the album, playlists use a continuously
                                // incrementing number to show the track index.
                                uint trackNumber = (uint) song + 1;
                                if (_currentView != AlbumView.Playlists && track.TrackNumber is uint trkNum)
                                    trackNumber = trkNum;

                                ImGui.TextUnformatted(trackNumber.ToString());

                                ImGui.TableNextColumn();

                                string path = track.Path;
                                string title = track.Title ?? locale.GetString("UnknownTrack");
                                string artist = track.Artist ?? locale.GetString("UnknownArtist");
                                string album = track.Album ?? locale.GetString("UnknownAlbum");
                                string length = track.Length is TimeSpan trackLength
                                    ? Utils.FormatTimespan(trackLength)
                                    : "";
                                string playCount = track.PlayCount.ToString();
                                string lastPlayed = track.LastPlayed is DateTime last
                                    ? last.ToString(CultureInfo.CurrentUICulture)
                                    : "";

                                // In order to allow the rating buttons to be clicked, we tell the selectable to ignore
                                // the ratings column (otherwise the buttons won't click and instead the song will play)
                                // To do this we just disable the SpanAllColumns flag when the rating column is hovered.
                                bool isRatingHovered = ImGui.TableGetHoveredColumn() == ratingColumn;
                                if (ImGui.Selectable($"{title}##{path}", path == currentTrackPath, isRatingHovered ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.SpanAllColumns))
                                {
                                    player.QueueTracks(_currentTracks.Select(trk => trk.Path), QueueSlot.Clear);
                                    if (!player.TryChangeTrack(song))
                                        AddPopup(new FileNotFoundPopup(title));
                                }
                                ImGui.SetColumnTooltip(title);

                                if (ImGui.BeginPopupContextItem())
                                {
                                    if (ImGui.Selectable(locale.GetString("Menu.AddToQueue")))
                                        player.QueueTrack(path, QueueSlot.AtEnd);
                                    if (ImGui.Selectable(locale.GetString("Menu.PlayNext")))
                                        player.QueueTrack(path, QueueSlot.NextTrack);

                                    ImGui.Separator();

                                    // much like in the transport bar, create the favorites playlist if it doesn't exist
                                    bool isInFavorites = GetOrCreateFavoritesPlaylist().Tracks.Contains(path);
                                    if (ImGui.Selectable(locale.GetString(isInFavorites ? "Menu.RemoveFromFavorites" : "Menu.AddToFavorites")))
                                    {
                                        Debug.Assert(_favoritesPlaylist != null);
                                        if (isInFavorites)
                                            _favoritesPlaylist.Tracks.Remove(path);
                                        else
                                            _favoritesPlaylist.Tracks.Add(path);

                                        Glimpse.Library.UpdatePlaylist(_favoritesPlaylist);

                                        // refresh & show the change if the current view is the favourites playlist
                                        if (_currentView == AlbumView.Playlists && _currentAlbum == IMusicLibrary.FavoritesPlaylistName)
                                            ChangeAlbum(IMusicLibrary.FavoritesPlaylistName);
                                    }

                                    if (ImGui.BeginMenu(locale.GetString("Menu.AddToPlaylist")))
                                    {
                                        if (ImGui.MenuItem(locale.GetString("Player.Playlists.New")))
                                            AddPopup(new NewPlaylistPopup(name => CreatePlaylist(name, path)));

                                        if (_playlists is not Dictionary<string, Playlist> playlists)
                                        {
                                            playlists = Glimpse.Library.GetPlaylists().ToDictionary(playlist => playlist.Name);
                                            _playlists = playlists;
                                        }

                                        if (playlists.Count > 0)
                                        {
                                            ImGui.Separator();

                                            foreach ((string name, Playlist playlist) in playlists)
                                            {
                                                bool isInPlaylist = playlist.Tracks.Contains(path);

                                                if (ImGui.MenuItem($"{(isInPlaylist ? "\ue5ca " : "   ")}{name}"))
                                                {
                                                    if (isInPlaylist)
                                                        playlist.Tracks.Remove(path);
                                                    else
                                                        playlist.Tracks.Add(path);

                                                    Glimpse.Library.UpdatePlaylist(playlist);

                                                    // refresh and show the change if the current view is the playlist the
                                                    // song was added to/removed from
                                                    if (_currentView == AlbumView.Playlists && _currentAlbum == name)
                                                        ChangeAlbum(name);
                                                }
                                            }
                                        }

                                        ImGui.EndMenu();
                                    }

                                    ImGui.Separator();

                                    if (ImGui.Selectable(locale.GetString("Menu.ShowInExplorer", Glimpse.Platform.FileManagerName)))
                                        Glimpse.Platform.OpenFileInExplorer(path);
                                    if (ImGui.Selectable(locale.GetString("Menu.RemoveFromLibrary")))
                                        AddPopup(new RemoveTrackPopup(path, false));
                                    if (Glimpse.Config.General.EnableFileDeletion && ImGui.Selectable(locale.GetString("Menu.DeleteFile")))
                                        AddPopup(new RemoveTrackPopup(path,true));

                                    ImGui.EndPopup();
                                }

                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(artist);
                                ImGui.SetColumnTooltip(artist);
                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(album);
                                ImGui.SetColumnTooltip(album);
                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(length);
                                //ImGui.SetColumnTooltip(length);
                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(playCount);
                                //ImGui.SetColumnTooltip(playCount);
                                ImGui.TableNextColumn();
                                int rating = isRatingHovered && _currentRowHover == currentRow ? _currentRatingHover : track.Rating;
                                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0));
                                ImGui.PushStyleColor(ImGuiCol.Button, 0);
                                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0);
                                ImGui.PushStyleColor(ImGuiCol.ButtonActive, 0);
                                for (int i = 0; i < 5; i++)
                                {
                                    if (ImGui.ImageButton($"{path}rating{i}", i < rating ? _starFilled : _star, ScaleVec(16), Vector4.Zero, iconsColor))
                                    {
                                        track.Rating = (byte) (i + 1);
                                        Glimpse.Library.UpdateTrack(track);
                                    }

                                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                    {
                                        track.Rating = 0;
                                        Glimpse.Library.UpdateTrack(track);
                                    }

                                    if (ImGui.IsItemHovered())
                                    {
                                        _currentRowHover = currentRow;
                                        _currentRatingHover = i + 1;
                                    }

                                    ImGui.SameLine(0, 0);
                                }
                                ImGui.PopStyleColor(3);
                                ImGui.PopStyleVar();

                                ImGui.NewLine();
                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(lastPlayed);
                                ImGui.SetColumnTooltip(lastPlayed);
                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(path);
                                ImGui.SetColumnTooltip(path);

                                song++;
                            }
                        }

                        ImGui.EndTable();
                    }
                    
                    ImGui.EndTabItem();
                }

                ImGuiTabItemFlags queueFlags =
                    switchToQueueView ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                
                if (ImGui.BeginTabItem(locale.GetString("Player.Tab.Queue"), queueFlags))
                {
                    ImGui.BeginChild("QueuedTracks");
                    {
                        List<string> queuedTracks = player.QueuedTracks;
                        List<int> playOrder = player.PlayOrder;
                        ImGuiListClipperPtr clipper = ImGui.ImGuiListClipper();
                        clipper.Begin(queuedTracks.Count, (32 + 2) * Scale);

                        while (clipper.Step())
                        {
                            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        //for (int i = 0; i < queuedTracks.Count; i++)
                            {
                                string path = queuedTracks[playOrder[i]];

                                bool selected = i == player.CurrentTrackIndex;
                                bool dark = i < player.CurrentTrackIndex;

                                /*if (dark)
                                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));

                                if (ImGui.Selectable($"{i + 1}. {Glimpse.Database.Tracks[path].Title}", selected))
                                    player.ChangeTrack(i);
                                if (dark)
                                    ImGui.PopStyleColor();*/

                                Vector2 cursorPos = ImGui.GetCursorPos();
                                int height = (int) ((32 + 6) * Scale);

                                // TODO: perform better checking here!
                                Glimpse.Library.TryGetTrack(path, out Track? track);
                                string title = track?.Title ?? locale.GetString("UnknownTrack");
                                string artist = track?.Artist ?? locale.GetString("UnknownArtist");
                                string album = track?.Album ?? locale.GetString("UnknownAlbum");
                                
                                if (ImGui.Selectable($"##Queue{i}", selected, ImGuiSelectableFlags.AllowOverlap, new Vector2(0, height)))
                                {
                                    if (!player.TryChangeTrack(i))
                                    {
                                        AddPopup(new FileNotFoundPopup(title));
                                    }
                                }
                                ImGui.SetCursorPos(cursorPos);
                                ImGui.SameLine();

                                ImGui.BeginChild($"QueueTrack{i}", new Vector2(0, height), ImGuiWindowFlags.NoInputs);
                                {
                                    float posY = ImGui.GetCursorPosY();
                                    ImGui.SetCursorPosY(posY + 1);
                                    ImGui.PushFont(ImFontPtr.Null, 32);
                                    ImGui.TextUnformatted($"{i + 1}");
                                    ImGui.PopFont();
                                    ImGui.SetCursorPosY(posY);
                                    ImGui.SameLine();
                                    ImGui.BeginChild($"QueueTrackInfo{i}", ImGuiWindowFlags.NoInputs);
                                    {
                                        ImGui.TextUnformatted(title);
                                        ImGui.PushFont(ImFontPtr.Null, 14);
                                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.75f, 0.75f, 0.75f, 1.0f));
                                        ImGui.TextUnformatted(
                                            $"{artist} • {album} • {track?.Length.GetValueOrDefault():mm\\:ss}");
                                        ImGui.PopStyleColor();
                                        ImGui.PopFont();

                                        ImGui.EndChild();
                                    }
                                    ImGui.EndChild();
                                }
                            }
                        }

                        ImGui.EndChild();
                    }
                    
                    ImGui.EndTabItem();
                }
                
                ImGui.EndTabBar();
            }
        }
        ImGui.End();
        
        #endregion
    }

    public void RefreshLayout()
    {
        _init = false;
        SetupStyle(ImGui.GetStyle());
    }
    
    private void PlayerOnTrackChanged(TrackInfo info, string path)
    {
        _hasIncrementedPlayCount = false;
        TrackInfo.Image? art = info.AlbumArt;

        if (art?.Data == null)
            _shouldDeleteArt = true;
        else
            _newAlbumArt = art.Data;
    }
    
    private void PlayerOnStateChanged(TrackState state)
    {
        Glimpse.Platform.SetPlayState(state, Glimpse.Player.CurrentTrack, Glimpse.Player.ElapsedTime);
        
        if (state != TrackState.Stopped)
            return;

        _shouldDeleteArt = true;
    }
    
    private void PlatformOnButtonPressed(TransportButton? button, int? position)
    {
        AudioPlayer player = Glimpse.Player;

        if (button is { } transportButton)
        {
            switch (transportButton)
            {
                case TransportButton.Play:
                    player.Play();
                    break;
                case TransportButton.Pause:
                    player.Pause();
                    break;
                case TransportButton.Next:
                    player.Next();
                    break;
                case TransportButton.Previous:
                    player.Previous();
                    break;
                case TransportButton.Stop:
                    player.Stop();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(button), button, null);
            }
        }
        
        if (position is { } pos)
            player.Seek(pos);
    }
    
    private TimeSpan PlatformOnGetPosition()
    {
        return Glimpse.Player.ElapsedTime;
    }

    private Vector2 ScaleVec(float x, float y)
    {
        float scale = Scale;
        return new Vector2((int) (x * scale), (int) (y * scale));
    }

    private Vector2 ScaleVec(float scalar)
        => ScaleVec(scalar, scalar);

    private async Task CheckForNewerVersion()
    {
        Logger logger = Glimpse.Logger;
        logger.Log("Checking for update...");
        
        try
        {
            using HttpClient client = new();
            client.BaseAddress = new Uri("https://glimpseplayer.com");

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "download/version.json");
            using HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            JsonNode obj = JsonObject.Parse(json);

            SemVer thisVersion = Glimpse.Version;

            string? newVersionString = (string?) obj["version"];
            if (newVersionString == null)
                return;
            SemVer newVersion = new SemVer(newVersionString);

            if (newVersion <= thisVersion)
            {
                logger.Log("Glimpse is up to date.");
                return;
            }

            string? newVersionUrl = (string?) obj["url"];
            if (newVersionUrl == null)
                return;

            _newVersion = newVersion;
            _newVersionURL = newVersionUrl;
            logger.Log($"Version {_newVersion} is available!");
        }
        catch (Exception e)
        {
            logger.Log($"Error occurred while checking for update: {e}");
        }
    }

    public void AddCustomButton(CustomButton button)
    {
        _customButtons.Add(button);
    }

    public override void Dispose()
    {
        _playCountTimer.Dispose();
        
        _playButton.Dispose();
        _pauseButton.Dispose();
        _skipButton.Dispose();
        _stopButton.Dispose();
        _plusButton.Dispose();
        _star.Dispose();
        _starFilled.Dispose();
        _cogButton.Dispose();
        _bugButton.Dispose();
        _updateButton.Dispose();
        _shuffleButton.Dispose();
        _repeatButton.Dispose();
        _repeatOneButton.Dispose();

        if (Glimpse.Config.Appearance.SaveWindowStateOnExit)
        {
            StateConfig state = new()
            {
                Position = Position,
                Size = Size,
                Maximized = Maximized
            };

            Glimpse.ConfigManager.WriteConfig(StateConfig.ConfigName, state);
        }

        base.Dispose();
    }

    protected override void OnScaleChanged()
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        SetupStyle(style);
        RefreshLayout();
    }

    private void FavoriteButton(AudioPlayer player, Locale locale, Vector2 iconSize, Vector4 iconsColor)
    {
        bool isInFavorites = false;
        if (player.TrackState != TrackState.Stopped)
        {
            // if there's no favorites playlist for some reason, create a new one and save it.
            // stops the disk from being hammered by queries about if the playlist exists,
            // and the simplest solution is literally just to create the playlist.
            isInFavorites = GetOrCreateFavoritesPlaylist().Tracks.Contains(player.CurrentTrackPath);
        }

        if (ImGui.ImageButton("HeartButton", isInFavorites ? _heartFilled : _heart, iconSize,
                new Vector2(1, 0), new Vector2(0, 1), Vector4.Zero, iconsColor))
        {
            Debug.Assert(_favoritesPlaylist != null);

            if (isInFavorites)
                _favoritesPlaylist.Tracks.Remove(player.CurrentTrackPath);
            else
                _favoritesPlaylist.Tracks.Add(player.CurrentTrackPath);

            Glimpse.Library.UpdatePlaylist(_favoritesPlaylist);

            // if the current view is the favorites playlist, refresh to reflect the change
            if (_currentView == AlbumView.Playlists && _currentAlbum == IMusicLibrary.FavoritesPlaylistName)
                ChangeAlbum(IMusicLibrary.FavoritesPlaylistName);
        }

        ImGui.SetItemTooltipUnformatted(
            locale.GetString(isInFavorites ? "Menu.RemoveFromFavorites" : "Menu.AddToFavorites"));
    }

    private Playlist GetOrCreateFavoritesPlaylist()
    {
        if (_favoritesPlaylist == null && !Glimpse.Library.TryGetPlaylist(IMusicLibrary.FavoritesPlaylistName, out _favoritesPlaylist))
        {
            Glimpse.Logger.Log("Favorites playlist is missing, a new one is being created.");
            _favoritesPlaylist = new Playlist(IMusicLibrary.FavoritesPlaylistName, []);
            Glimpse.Library.UpdatePlaylist(_favoritesPlaylist);
        }

        return _favoritesPlaylist;
    }

    private void CreatePlaylist(string name, string? trackToAdd)
    {
        // if the playlist already exists, just add it to the existing playlist
        if (!Glimpse.Library.TryGetPlaylist(name, out Playlist? playlist))
            playlist = new Playlist(name, []);

        if (trackToAdd != null)
            playlist.Tracks.Add(trackToAdd);

        Glimpse.Library.UpdatePlaylist(playlist);

        // refresh the view if necessary
        if (_currentView == AlbumView.Playlists)
            ChangeView(AlbumView.Albums);
    }

    private bool TryGetTracks(string name, [NotNullWhen(true)] out HashSet<string>? tracks)
    {
        tracks = null;
        switch (_currentView)
        {
            case AlbumView.Albums:
                if (Glimpse.Library.TryGetAlbum(name, out Album? album))
                    tracks = album.Tracks;
                break;
            case AlbumView.Artists:
                if (Glimpse.Library.TryGetArtist(name, out Artist? artist))
                    tracks = artist.Tracks;
                break;
            case AlbumView.Genres:
                if (Glimpse.Library.TryGetGenre(name, out Genre? genre))
                    tracks = genre.Tracks;
                break;
            case AlbumView.Playlists:
                if (Glimpse.Library.TryGetPlaylist(name, out Playlist? playlist))
                    tracks = playlist.Tracks;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return tracks != null && tracks.Count > 0;
    }

    /// <summary>
    /// Change the current album.
    /// </summary>
    /// <param name="albumName">The album name. Use <see langword="null"/> to view all.</param>
    private void ChangeAlbum(string? albumName)
    {
        _currentAlbum = albumName;
        if (albumName == null)
            _currentTracks = Glimpse.Library.GetTracks();
        else
        {
            switch (_currentView)
            {
                case AlbumView.Albums:
                {
                    if (!Glimpse.Library.TryGetTracksForAlbum(albumName, out _currentTracks))
                        goto default;
                    break;
                }

                case AlbumView.Artists:
                {
                    if (!Glimpse.Library.TryGetTracksForArtist(albumName, out _currentTracks))
                        goto default;
                    break;
                }

                case AlbumView.Genres:
                {
                    if (!Glimpse.Library.TryGetTracksForGenre(albumName, out _currentTracks))
                        goto default;
                    break;
                }

                case AlbumView.Playlists:
                {
                    if (!Glimpse.Library.TryGetTracksForPlaylist(albumName, out _currentTracks))
                        goto default;
                    break;
                }
                
                default:
                    _currentTracks = Glimpse.Library.GetTracks();
                    break;
            }
            
        }
    }

    private void ChangeView(AlbumView view)
    {
        _currentView = view;
        switch (view)
        {
            case AlbumView.Albums:
            {
                SizedCollection<Album> albums = Glimpse.Library.GetAlbums();
                IEnumerable<string> albumNames = albums.Collection.Select(album => album.Name);
                _albums = new SizedCollection<string>(albumNames, albums.Count);
                break;
            }
            case AlbumView.Artists:
            {
                SizedCollection<Artist> artists = Glimpse.Library.GetArtists();
                IEnumerable<string> artistNames = artists.Collection.Select(artist => artist.Name);
                _albums = new SizedCollection<string>(artistNames, artists.Count);
                break;
            }
            case AlbumView.Genres:
            {
                SizedCollection<Genre> genres = Glimpse.Library.GetGenres();
                IEnumerable<string> genreNames = genres.Collection.Select(genre => genre.Name);
                _albums = new SizedCollection<string>(genreNames, genres.Count);
                break;
            }
            case AlbumView.Playlists:
            {
                _playlists = Glimpse.Library.GetPlaylists().ToDictionary(playlist => playlist.Name);
                _albums = new SizedCollection<string>(_playlists.Keys, (uint) _playlists.Count);
                break;
            }
        }
    }

    private unsafe void SetupStyle(ImGuiStylePtr style)
    {
        *style.Handle = _defaultStyle;
        
        const int rounding = 5;
        style.FrameRounding = rounding;
        style.GrabRounding = rounding;
        style.ChildRounding = rounding;
        style.PopupRounding = rounding;
        style.DockingSeparatorSize = (int) float.Ceiling(1 * Scale);
        style.ScaleAllSizes(Scale);
        style.FontScaleDpi = Scale;

        // TODO: This system is terrible!!!
        //   The theme should be stored in the config file as the theme's "Friendly Name", not as a "path" to the theme.
        string themeName = $"Themes.{Glimpse.Config.Appearance.Theme}.json";
        Stream stream;
        try
        {
            stream = Asset.GetAssetStream(themeName);
        }
        catch (Exception e)
        {
            Glimpse.Logger.Log("Couldn't load theme. Using default.");
            stream = Asset.GetAssetStream($"Themes.{Theme.DefaultTheme}.json");
        }

        bool useLightMode = Glimpse.Config.Appearance.PreferredColorScheme switch
        {
            PreferredColorScheme.SyncToOS => SDL.GetSystemTheme() == SDL.SystemTheme.Light,
            PreferredColorScheme.Dark => false,
            PreferredColorScheme.Light => true,
            _ => throw new ArgumentOutOfRangeException()
        };
        
        Theme theme =
            JsonSerializer.Deserialize<Theme>(stream, ConfigManager.GetDefaultSerializerOptions());
        theme.ApplyImGuiStyle(useLightMode, ImGui.GetStyle().Colors);

        _themeConfig = theme.Config;
        // Reload current album art in case the settings changed.
        _newAlbumArt = Glimpse.Player.CurrentTrack?.AlbumArt?.Data;

        // Reload default album art too.
        _defaultAlbumArt = Renderer.CreateImage(_themeConfig.Logo ?? "asset://Icons.Glimpse.png");

        stream.Dispose();
    }

    private enum AlbumView
    {
        Albums,
        Artists,
        Genres,
        Playlists
    }

    public struct CustomButton
    {
        public string Name;
        public Image Image;
        public string? Tooltip;
        public Action OnClick;

        public CustomButton(string name, Image image, string? tooltip, Action onClick)
        {
            Name = name;
            Image = image;
            Tooltip = tooltip;
            OnClick = onClick;
        }
    }
}