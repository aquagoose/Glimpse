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
    private string? _selectedLibrary;
    
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

        if (ImGui.BeginListBox("##Libraries", _popup.ScaleVec(400, 415)))
        {
            foreach (LibraryDirectory dir in LibraryPaths)
                dir.Update(_glimpse.Library, ref _selectedLibrary, locale);
            
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
            
            ImGui.BeginDisabled(_selectedLibrary == null);
            if (ImGui.ImageButton("Remove", _minus, _popup.ScaleVec(16)))
            {
                _glimpse.Library.RemoveLibaryPath(_selectedLibrary);
                _selectedLibrary = null;
                _needsRefresh = true;
            }
            ImGui.EndDisabled();
            
            ImGui.SetItemTooltipUnformatted(locale.GetString("Widget.ManageLibrary.RemoveSelectedFolder.Tooltip"));
            ImGui.SameLine();

            if (ImGui.ImageButton("Refresh", _refresh, _popup.ScaleVec(16)))
                _needsRefresh = true;
            ImGui.SetItemTooltipUnformatted(locale.GetString("Widget.ManageLibrary.RefreshButton.Tooltip"));

            ImGui.Separator();

            // todo this should have a confirmation ??????????
            if (ImGui.Button(locale.GetString("Widget.ManageLibrary.RemoveAllButton")))
                _glimpse.Library.RemoveAllLibraryPaths();
            ImGui.SetItemTooltipUnformatted(locale.GetString("Widget.ManageLibrary.RemoveAllButton.Tooltip"));
            
            ImGui.Checkbox("Refresh on Launch", ref _glimpse.Config.Library.RefreshLibraryOnStartup);
            ImGui.SetItemTooltipUnformatted("Refresh the music library when Glimpse is launched.");
            ImGui.Checkbox("Remove Deleted Files", ref _glimpse.Config.Library.RemoveMissingFilesFromLibrary);
            ImGui.SetItemTooltipUnformatted("Automatically remove tracks from the library that can no longer be found.");
            
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
        public unsafe void Update(MusicLibrary library, ref string selectedDirectory, Locale locale)
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
                        bool enabled = true;
                        // TODO: Expose the hash set.
                        if (library.ExcludedDirectories.Contains(directory))
                            enabled = false;

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
                ImGui.TreeNodeEx(pathName,
                    ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen |
                    (Path == selectedDirectory ? ImGuiTreeNodeFlags.Selected : 0));
                if (ImGui.IsItemClicked())
                    selectedDirectory = Path;

                ImGui.PopStyleColor();
                ImGui.SetItemTooltipUnformatted(locale.GetString("Widget.ManageLibrary.PathNotFound"));
                return;
            }

            if (!Enabled)
                ImGui.PushStyleColor(ImGuiCol.Text, *ImGui.GetStyleColorVec4(ImGuiCol.TextDisabled));
            
            ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.DrawLinesFull;
            if (Path == selectedDirectory)
                flags |= ImGuiTreeNodeFlags.Selected;
            
            bool treeNodeOpened = ImGui.TreeNodeEx(pathName, flags);
            if (ImGui.IsItemClicked())
                selectedDirectory = Path;
            if (treeNodeOpened)
            {
                foreach ((_, LibraryDirectory directory) in SubDirectories)
                    directory.Update(library, ref selectedDirectory, locale);
                
                ImGui.TreePop();
            }

            if (!Enabled)
                ImGui.PopStyleColor();
        }
    }
}