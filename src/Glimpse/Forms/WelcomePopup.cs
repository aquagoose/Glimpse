using System.Numerics;
using Glimpse.API;
using Glimpse.API.Library;
using Glimpse.Configs;
using Glimpse.Forms.Widgets;
using Glimpse.Library;
using Hexa.NET.ImGui;
using piko.SDL3;
using Image = Glimpse.Graphics.Image;

namespace Glimpse.Forms;

public class WelcomePopup : Popup
{
    private ManageLibraryWidget _manageLibraryWidget;
    private ThemeWidget _themeWidget;
    
    private bool _hasOldLibrary;
    
    private bool _disableNext;
    private readonly SDL.DialogFileCallback _folderDialog;
    
    private Image _glimpse;
    private uint _tabIndex;

    public override void Open()
    {
        _manageLibraryWidget = new ManageLibraryWidget(this);
        _themeWidget = new ThemeWidget(this);
        
        _hasOldLibrary = File.Exists(Path.Combine(IConfigManager.BaseDir, OldMusicDatabase.DatabaseName + ".json"));
        
        _glimpse = Renderer.CreateImage("asset://Icons.Glimpse.png");
        _tabIndex = 0;
    }

    protected override unsafe void Update(float dt)
    {
        Locale locale = Glimpse.Locale;
        _disableNext = false;

        Vector2 windowSize = ImGui.GetIO().DisplaySize;
        Vector2 welcomeSize = ScaleVec(800, 500);
        Vector2 welcomePos = new Vector2(windowSize.X / 2 - welcomeSize.X / 2, windowSize.Y / 2 - welcomeSize.Y / 2);
        
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(windowSize);
        if (ImGui.Begin(locale.GetString("Popup.Welcome.Name"), ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground))
        {
            ImGui.GetWindowDrawList().AddRectFilled(new Vector2(0, 0), windowSize,
                ImGui.ColorConvertFloat4ToU32(*ImGui.GetStyleColorVec4(ImGuiCol.WindowBg) with { W = 0.75f }));
            ImGui.SetNextWindowPos(welcomePos);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, *ImGui.GetStyleColorVec4(ImGuiCol.PopupBg));
            ImGui.BeginChild("TabChild", welcomeSize, ImGuiChildFlags.Borders);
            {
                if (ImGui.BeginTabBar("Tabs"))
                {
                    Tab(locale.GetString("Popup.Welcome.Tab.Welcome"), 0, WelcomeTab);
                    Tab(locale.GetString("Popup.Welcome.Tab.Import"), 1, ImportTab);
                    Tab(locale.GetString("Popup.Welcome.Tab.Preferences"), 2, PreferencesTab);
                    
                    ImGui.EndTabBar();
                }

                ImGui.EndChild();
            }
            ImGui.PopStyleColor();

            Vector2 buttonSize = ScaleVec(100, 50);
            
            float right = welcomePos.X + welcomeSize.X;
            float padding = ImGui.GetStyle().ItemSpacing.X;
            float totalButtonsWidth = buttonSize.X * 2 + padding;
            ImGui.SetCursorPosX(right - totalButtonsWidth);
            
            ImGui.BeginDisabled(_tabIndex == 0);
            if (ImGui.Button(locale.GetString("Button.Previous"), buttonSize))
                _tabIndex--;
            ImGui.EndDisabled();
            
            ImGui.SameLine();

            bool disableNext = _disableNext != null;
            if (ImGui.Button(locale.GetString("Button.Next"), buttonSize))
            {
                // TODO: This is very manual. Perhaps add a "next button" action or something?
                if (_tabIndex == 1)
                {
                    // we don't reeeallly want people accidentally skipping past the import screen
                    // however for those who do, this allows them to skip, while giving those who blindly click
                    // a change to go back and add stuff.
                    if (Glimpse.Library.LibraryPaths.Count == 0)
                    {
                        AddPopup(new MessageBoxPopup(MessageBoxPopup.Buttons.YesNo,
                            locale.GetString("Popup.Welcome.SkipImport"),
                            locale.GetString("Popup.Welcome.SkipImport.Message"), () => _tabIndex++));
                    }
                    else
                    {
                        if (!Glimpse.Library.IsIndexing) // Prevent indexing while indexing already.
                            Glimpse.Library.Index();

                        _tabIndex++;
                    }
                }
                else
                    _tabIndex++;

                if (_tabIndex >= 3)
                {
                    ((GlimpsePlayer) Glimpse.MainWindow).RefreshLayout();
                    Glimpse.ConfigManager.WriteConfig(GlimpseConfig.ConfigName, Glimpse.Config);
                    Close();
                }
            }
            
            ImGui.End();
        }
    }

    private void Tab(string name, int index, Action tabFunc)
    {
        ImGui.BeginDisabled(_tabIndex != index);

        if (ImGui.BeginTabItem(name, _tabIndex == index ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
        {
            tabFunc();
            ImGui.EndTabItem();
        }
        
        ImGui.EndDisabled();
    }

    private void WelcomeTab()
    {
        ImGui.BeginChild("GlimpseLogo", ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.AutoResizeY);
        {
            ImGui.Image(_glimpse, ScaleVec(256));
            ImGui.EndChild();
        }

        ImGui.SameLine();

        ImGui.BeginChild("WelcomeText", ImGuiChildFlags.AutoResizeY);
        {
            ImGui.PushFont(ImFontPtr.Null, 48);
            ImGui.TextUnformatted(Glimpse.Locale.GetString("Popup.Welcome.Tab.Welcome.WelcomeToGlimpse"));
            ImGui.PopFont();

            ImGui.TextUnformatted(Glimpse.Locale.GetString("Popup.Welcome.Tab.Welcome.Tagline"));
            ImGui.TextUnformatted(Glimpse.Locale.GetString("Popup.Welcome.Tab.Welcome.GetStarted"));

            ImGui.EndChild();
        }
    }

    private void ImportTab()
    {
        if (_hasOldLibrary)
        {
            _hasOldLibrary = false;
            AddPopup(new MessageBoxPopup(MessageBoxPopup.Buttons.YesNo,
                Glimpse.Locale.GetString("Popup.Welcome.Tab.Import.MigrationAssistant"),
                Glimpse.Locale.GetString("Popup.Welcome.Tab.Import.MigrationAssistant.Message"), ImportOldLibrary));
        }

        if (_manageLibraryWidget.LibraryPaths.Count == 0)
            _disableNext = true;
        
        ImGui.TextUnformatted(Glimpse.Locale.GetString("Popup.Welcome.Tab.Import.GetStarted"));
        _manageLibraryWidget.Update(Glimpse.Locale);
    }

    private void PreferencesTab()
    {
        ref GlimpseConfig config = ref Glimpse.Config;
        _themeWidget.Update(ref config);
    }

    private void ImportOldLibrary()
    {
        Glimpse.ConfigManager.TryGetConfig(OldMusicDatabase.DatabaseName, out OldMusicDatabase oldDb);

        HashSet<string> paths = [];
        foreach ((string path, Track track) in oldDb.Tracks)
        {
            track.Path = path;
            Glimpse.Library.InsertOrUpdateTrack(track);

            string basePath = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(basePath))
                continue;

            paths.Add(basePath);
        }

        foreach ((_, Album album) in oldDb.Albums)
            Glimpse.Library.InsertOrUpdateAlbum(album);

        foreach ((_, Artist artist) in oldDb.Artists)
            Glimpse.Library.InsertOrUpdateArtist(artist);

        foreach ((_, Genre genre) in oldDb.Genres)
            Glimpse.Library.InsertOrUpdateGenre(genre);

        // orders the paths by number of / and then checks if each path is "contained inside"
        // a path already in the base paths set.
        HashSet<string> basePaths = [];
        foreach (string path in paths.OrderBy(s => s.Replace('\\', '/').Count('/')))
        {
            foreach (string basePath in basePaths)
            {
                if (path.StartsWith(basePath))
                    goto SKIP; // If a match was found then we know that this path is a sub-path.
            }

            basePaths.Add(path);

            SKIP: ;
        }

        foreach (string basePath in basePaths)
            Glimpse.Library.AddLibraryPath(basePath);

        if (basePaths.Count == 0)
        {
            AddPopup(new MessageBoxPopup(MessageBoxPopup.Buttons.Ok, Glimpse.Locale.GetString("Error"),
                Glimpse.Locale.GetString("Popup.Welcome.Tab.Import.MigrationAssistant.Error")));
        }

        _manageLibraryWidget.Refresh();
    }

    public override void Dispose()
    {
        _glimpse.Dispose();
        
        _themeWidget.Dispose();
        _manageLibraryWidget.Dispose();
    }
}