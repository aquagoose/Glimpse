using System.Numerics;
using Glimpse.Graphics;
using Glimpse.Library;
using Hexa.NET.ImGui;
using piko.SDL3;
using Image = Glimpse.Graphics.Image;

namespace Glimpse.Forms.Widgets;

public class ManageLibraryWidget : IDisposable
{
    private readonly Popup _popup;
    private readonly Renderer _renderer;
    private readonly Glimpse _glimpse;
    
    private readonly SDL.DialogFileCallback _folderCallback;
    
    private Image _plus = null!;
    private Image _minus = null!;
    private Image _refresh = null!;
    
    private bool _needsRefresh;
    
    public List<LibraryDirectory> LibraryPaths = null!;

    public unsafe ManageLibraryWidget(Popup popup)
    {
        _popup = popup;
        _renderer = popup.Renderer;
        _glimpse = popup.Glimpse;
        
        _folderCallback = FolderCallback;
        
        _plus = _renderer.CreateImage("asset://Icons.Plus.png");
        _minus = _renderer.CreateImage("asset://Icons.Minus.png");
        _refresh = _renderer.CreateImage("asset://Icons.Update.png");

        LibraryPaths = [];
        _needsRefresh = true;
    }

    public unsafe void Update(Locale locale)
    {
        if (_needsRefresh)
            Refresh();
        
        ImGui.BeginDisabled(_glimpse.Library.IsIndexing);

        if (ImGui.BeginListBox("##Libraries", _popup.ScaleVec(375, 415)))
        {
            foreach (LibraryDirectory dir in LibraryPaths)
                dir.Update(_glimpse.Library, ref _needsRefresh, true, locale);
            
            ImGui.EndListBox();
        }
        
        ImGui.SameLine();

        ImGui.BeginChild("Settings", ImGuiChildFlags.AutoResizeY);
        {
            if (ImGui.ImageButton("AddNewFolders", _plus, _popup.ScaleVec(16)))
            {
                string? defaultLocation = Environment.GetFolderPath(Environment.SpecialFolder.MyComputer);
                if (string.IsNullOrWhiteSpace(defaultLocation))
                    defaultLocation = null;

                SDL.ShowOpenFolderDialog(_folderCallback, 0, _glimpse.MainWindow.Handle, defaultLocation, true);
            }
            ImGui.SetItemTooltipUnformatted(locale.GetString("Widget.ManageLibrary.AddFolders.Tooltip"));
            
            ImGui.SameLine();

            if (ImGui.ImageButton("Refresh", _refresh, _popup.ScaleVec(16)))
                _needsRefresh = true;
            ImGui.SetItemTooltipUnformatted(locale.GetString("Widget.ManageLibrary.RefreshButton.Tooltip"));

            ImGui.Separator();

            // todo this should have a confirmation ??????????
            if (ImGui.Button(locale.GetString("Widget.ManageLibrary.RemoveAllButton")))
                _glimpse.Library.RemoveAllLibraryPaths();
            ImGui.SetItemTooltipUnformatted(locale.GetString("Widget.ManageLibrary.RemoveAllButton.Tooltip"));
            
            ImGui.Checkbox(locale.GetString("Setting.Library.RefreshLibraryOnStartup"), ref _glimpse.Config.Library.RefreshLibraryOnStartup);
            ImGui.SetItemTooltipUnformatted(locale.GetString("Setting.Library.RefreshLibraryOnStartup.Tooltip"));
            ImGui.Checkbox(locale.GetString("Setting.Library.RemoveMissingFilesFromLibrary"), ref _glimpse.Config.Library.RemoveMissingFilesFromLibrary);
            ImGui.SetItemTooltipUnformatted(locale.GetString("Setting.Library.RemoveMissingFilesFromLibrary.Tooltip"));
            
            ImGui.EndChild();
        }
        
        ImGui.EndDisabled();
    }
    
    public void Refresh()
    {
        LibraryPaths = [];

        foreach (string path in _glimpse.Library.LibraryPaths)
            LibraryPaths.Add(new LibraryDirectory(path, true, true));
    }
    
    public void Dispose()
    {
        _refresh.Dispose();
        _plus.Dispose();
    }
    
    private unsafe void FolderCallback(nint userdata, sbyte** filelist, int filter)
    {
        sbyte** fileList = (sbyte**) filelist;
        int index = 0;
        while (fileList[index] != null)
        {
            string directory = new string(fileList[index]);
            _glimpse.Library.AddLibraryPath(directory);
            _needsRefresh = true;
            index++;
        }
    }
    
    public class LibraryDirectory
    {
        private bool _exists;
        
        /// <summary>
        /// The path of the directory.
        /// </summary>
        public string Path;

        /// <summary>
        /// If the directory is a base library path. The full directory path will only be displayed if true.
        /// </summary>
        public bool IsBaseDir;
        
        /// <summary>
        /// Whether the directory is enabled. If disabled, the directory will not be indexed.
        /// </summary>
        public bool Enabled;
        
        /// <summary>
        /// Subdirectories contained in this directory.
        /// </summary>
        public Dictionary<string, LibraryDirectory>? SubDirectories;

        public LibraryDirectory(string path, bool isBaseDir, bool enabled)
        {
            Path = path;
            IsBaseDir = isBaseDir;
            Enabled = enabled;
            _exists = true;
        }

        // todo can we just make the locale static or something this is horrible and i hate needing to pass it everywhere
        public unsafe void Update(MusicLibrary library, ref bool needsRefresh, bool parentEnabled, Locale locale)
        {
            if (SubDirectories == null && _exists)
            {
                if (Directory.Exists(Path))
                {
                    SubDirectories = [];
                    EnumerationOptions options = new()
                    {
                        IgnoreInaccessible = true
                    };
                    foreach (string directory in Directory.GetDirectories(Path, "*", options))
                    {
                        // TODO: Expose the hash set.
                        bool enabled = !library.ExcludedDirectories.Contains(directory);
                        SubDirectories.Add(directory, new LibraryDirectory(directory, false, enabled));
                    }
                }
                else
                    _exists = false;
            }
            
            string pathName = IsBaseDir ? Path : System.IO.Path.GetFileName(Path);

            if (!_exists)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
                ImGui.TreeNodeEx(pathName, ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen);

                ImGui.PopStyleColor();
                ImGui.SetItemTooltipUnformatted(locale.GetString("Widget.ManageLibrary.PathNotFound"));
                return;
            }
            
            // if this node isn't enabled, disable all child nodes.
            // otherwise, pass through the current value
            // this ensures that all child nodes of any disabled node will also be disabled.
            bool shouldEnable = !Enabled ? false : parentEnabled;

            if (!shouldEnable)
                ImGui.PushStyleColor(ImGuiCol.Text, *ImGui.GetStyleColorVec4(ImGuiCol.TextDisabled));
            
            ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.DrawLinesFull | ImGuiTreeNodeFlags.LabelSpanAllColumns;
            
            bool treeNodeOpened = ImGui.TreeNodeEx(pathName, flags);
            
            if (!shouldEnable)
                ImGui.PopStyleColor();
            
            if (parentEnabled && ImGui.BeginPopupContextItem())
            {
                if (Enabled)
                {
                    if (ImGui.Selectable(locale.GetString(IsBaseDir ? "Widget.ManageLibrary.Menu.RemoveFromLibrary" : "Widget.ManageLibrary.Menu.ExcludeFromLibrary")))
                    {
                        library.RemoveLibaryPath(Path);
                        needsRefresh = true;
                    }
                }
                else if (ImGui.Selectable(locale.GetString("Widget.ManageLibrary.Menu.IncludeInLibrary")))
                {
                    library.AddLibraryPath(Path);
                    needsRefresh = true;
                }

                ImGui.EndMenu();
            }
            
            if (treeNodeOpened)
            {
                foreach ((_, LibraryDirectory directory) in SubDirectories)
                    directory.Update(library, ref needsRefresh, shouldEnable, locale);
                
                ImGui.TreePop();
            }
        }
    }
}