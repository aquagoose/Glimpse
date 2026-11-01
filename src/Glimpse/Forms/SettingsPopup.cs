using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Glimpse.API;
using Glimpse.API.UI;
using Glimpse.Assets;
using Glimpse.Configs;
using Glimpse.Forms.Widgets;
using Hexa.NET.ImGui;
using piko.SDL3;
using Image = Glimpse.Graphics.Image;

namespace Glimpse.Forms;

public class SettingsPopup : Popup
{
    private ImmediateGUI _gui;

    private GlimpseConfig _currentConfig;
    private ThemeWidget _themeWidget;
    
    private Image? _glimpseLogo;
    private string? _currentPlugin;

    private Image? _transportDown;
    private Image? _transportUp;

    public override void Open()
    {
        _gui = new ImmediateGUI(Glimpse);

        _currentConfig = Glimpse.Config;
        _currentConfig.Plugins.EnabledPlugins = new HashSet<string>(Glimpse.Config.Plugins.EnabledPlugins);

        _themeWidget = new ThemeWidget(this);
    }

    protected override void Update(float dt)
    {
        _gui.Scale = Scale;

        Locale currentLocale = Glimpse.Locale;

        ImGuiWindowFlags flags = ImGuiWindowFlags.None;
        if (Glimpse.Config != _currentConfig)
            flags |= ImGuiWindowFlags.UnsavedDocument;

#if !PUBLISH_AOT
        foreach ((_, Plugin plugin) in Glimpse.Plugins)
        {
            if (plugin.Instance.ConfigChanged)
                flags |= ImGuiWindowFlags.UnsavedDocument;
        }
#endif
        
        if (ImGui.OpenPopupModal(currentLocale.GetString("Popup.Settings.Name"), ScaleVec(620, 570), flags))
        {
            ImGui.BeginChild("SettingsItems", ScaleVec(600, 500));
            {
                if (ImGui.BeginTabBar("SettingsTab"))
                {
                    if (ImGui.BeginTabItem(currentLocale.GetString("Popup.Settings.Tab.General")))
                    {
                        if (ImGui.BeginCombo(currentLocale.GetString("Popup.Settings.Tab.General.Language"),
                                currentLocale.DisplayName))
                        {
                            foreach ((string id, Locale.AvailableLocale locale) in Locale.AvailableLocales.Locales)
                            {
                                if (ImGui.Selectable(locale.DisplayName, currentLocale.ID == id ? ImGuiSelectableFlags.Highlight : ImGuiSelectableFlags.None))
                                {
                                    _currentConfig.General.Language = id;
                                    Glimpse.Locale = Locale.LoadLocale(id);
                                }
                            }

                            ImGui.EndCombo();
                        }

                        ImGui.Checkbox(currentLocale.GetString("Popup.Settings.Tab.General.EnableDeleteFile"),
                            ref _currentConfig.General.EnableFileDeletion);
                        ImGui.SetItemTooltipUnformatted(
                            currentLocale.GetString("Popup.Settings.Tab.General.EnableDeleteFile.Tooltip"));

#if !DISABLE_AUTOUPDATE
                        ImGui.Checkbox(currentLocale.GetString("Popup.Settings.Tab.General.CheckForUpdates"),
                            ref _currentConfig.General.EnableUpdateChecking);
                        ImGui.SetItemTooltipUnformatted(currentLocale.GetString("Popup.Settings.Tab.General.CheckForUpdates.Tooltip"));
#endif

                        ImGui.EndTabItem();
                    }
                    
                    if (ImGui.BeginTabItem(currentLocale.GetString("Popup.Settings.Tab.Appearance")))
                    {
                        ImGui.BeginChild("AppearanceTab");
                        {
                            ImGui.SeparatorText(currentLocale.GetString("Popup.Settings.Tab.Appearance.Theme"));

                            _themeWidget.Update(ref _currentConfig);

                            if (ImGui.Button(currentLocale.GetString("Popup.Settings.Tab.Appearance.OpenThemeEditor")))
                            {
                                Close();
                                Glimpse.MainWindow.AddPopup(new ThemeEditor());
                            }

                            ImGui.SeparatorText(
                                currentLocale.GetString("Popup.Settings.Tab.Appearance.TransportLocation"));

                            _transportDown ??= Renderer.CreateImage("asset://Images.TransportDown.png");
                            _transportUp ??= Renderer.CreateImage("asset://Images.TransportUp.png");

                            string up = currentLocale.GetString("Popup.Settings.Tab.Appearance.TransportLocation.Up");
                            string down =
                                currentLocale.GetString("Popup.Settings.Tab.Appearance.TransportLocation.Down");

                            if (ImGui.SelectButton("TransportDown", _transportDown,
                                    ScaleVec(_transportDown.Width * 0.25f, _transportDown.Height * 0.25f),
                                    !_currentConfig.Appearance.SwapTransportControls))
                            {
                                _currentConfig.Appearance.SwapTransportControls = false;
                            }

                            ImGui.SetItemTooltipUnformatted(down);

                            ImGui.SameLine();

                            if (ImGui.SelectButton("TransportUp", _transportUp,
                                    ScaleVec(_transportUp.Width * 0.25f, _transportUp.Height * 0.25f),
                                    _currentConfig.Appearance.SwapTransportControls))
                            {
                                _currentConfig.Appearance.SwapTransportControls = true;
                            }

                            ImGui.SetItemTooltipUnformatted(up);

                            ImGui.SeparatorText(currentLocale.GetString("Popup.Settings.Tab.Appearance.Misc"));

                            ImGui.Checkbox(
                                currentLocale.GetString("Popup.Settings.Tab.Appearance.ConfineAlbumArtToSquare"),
                                ref _currentConfig.Appearance.ConfineAlbumArtToSquare);
                            ImGui.SetItemTooltipUnformatted(
                                currentLocale.GetString(
                                    "Popup.Settings.Tab.Appearance.ConfineAlbumArtToSquare.Tooltip"));

                            ImGui.Checkbox(
                                currentLocale.GetString("Popup.Settings.Tab.Appearance.SaveWindowStateOnExit"),
                                ref _currentConfig.Appearance.SaveWindowStateOnExit);
                            ImGui.SetItemTooltipUnformatted(
                                currentLocale.GetString("Popup.Settings.Tab.Appearance.SaveWindowStateOnExit.Tooltip"));

                            ImGui.EndChild();
                        }

                        ImGui.EndTabItem();
                    }

                    /*if (ImGui.BeginTabItem(locale.GetString("Popup.Settings.Tab.Player")))
                    {
                        ref float volume = ref _currentConfig.Volume;
                        ref uint sampleRate = ref _currentConfig.SampleRate;
                        //float speed = (float) _currentConfig.SpeedAdjust;

                        ImGui.SeparatorText(locale.GetString("Popup.Settings.Tab.Player.PlaybackHeading"));

                        if (ImGui.SliderFloat(locale.GetString("Popup.Settings.Tab.Player.Volume"), ref volume, 0, 1, "%.3f"))
                            Glimpse.Player.Volume = volume;
                        
                        /*ImGui.Checkbox("Auto Play", ref autoPlay);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Start playing when a track is selected or added to queue.");
                        
                        //if (ImGui.DragFloat("Speed Adjustment", ref speed, 0.01f, 0.01f, 10))
                        //    _currentConfig.SpeedAdjust = speed;
                        
                        ImGui.SeparatorText(locale.GetString("Popup.Settings.Tab.Player.DeviceHeading"));
                        ImGui.BeginDisabled();
                        if (ImGui.BeginCombo(locale.GetString("SampleRate"), sampleRate.ToString()))
                        {
                            ImGui.EndCombo();
                        }
                        ImGui.EndDisabled();

                        ImGui.EndTabItem();
                    }*/
#if !PUBLISH_AOT
                    if (ImGui.BeginTabItem(currentLocale.GetString("Popup.Settings.Tab.Plugins")))
                    {
                        if (Glimpse.Plugins.Count == 0)
                        {
                            ImGui.TextUnformatted(currentLocale.GetString("Popup.Settings.Tab.Plugins.NoneAvailable"));
                        }
                        else
                        {
                            // hack to stop the table from being VERY SLIGHTLY oversized and adding scroll bars
                            // why does it do this? i have no idea! and i can't be bothered to work out why
                            ImGui.BeginChild("Plugins", ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar);
                            {
                                // table hack to add an adjustable separator without using the dock builder
                                ImGui.BeginTable("PluginsTable", 2, ImGuiTableFlags.Resizable);
                                {
                                    ImGui.TableSetupColumn("Plugins", ImGuiTableColumnFlags.WidthStretch, 0.3f);
                                    ImGui.TableSetupColumn("PluginSettings", ImGuiTableColumnFlags.WidthStretch, 0.7f);

                                    ImGui.TableNextRow();
                                    ImGui.TableNextColumn();

                                    foreach ((string id, Plugin plugin) in Glimpse.Plugins)
                                    {
                                        ImGui.BeginChild("PluginsList");
                                        {
                                            bool enabled = _currentConfig.Plugins.EnabledPlugins.Contains(id);

                                            if (ImGui.Selectable($"{(enabled ? "\uE5CA" : "")}{plugin.Name}", id == _currentPlugin))
                                                _currentPlugin = id;

                                            ImGui.EndChild();
                                        }

                                        ImGui.SameLine();

                                        ImGui.TableNextColumn();

                                        ImGui.BeginChild("PluginSettings");
                                        {
                                            if (id == _currentPlugin)
                                            {
                                                bool enabled =
                                                    _currentConfig.Plugins.EnabledPlugins.Contains(_currentPlugin);
                                                if (ImGui.Checkbox(currentLocale.GetString("Checkbox.Enabled"),
                                                        ref enabled))
                                                {
                                                    if (enabled)
                                                        _currentConfig.Plugins.EnabledPlugins.Add(_currentPlugin);
                                                    else
                                                        _currentConfig.Plugins.EnabledPlugins.Remove(_currentPlugin);
                                                }

                                                ImGui.PushFont(ImFontPtr.Null, 32 * Scale);
                                                ImGui.TextUnformatted(plugin.Name);
                                                ImGui.PopFont();
                                                ImGui.TextUnformatted(currentLocale.GetString("Popup.Settings.Tab.Plugins.ByAuthor", plugin.Author));

                                                if (plugin.Description != null)
                                                    ImGui.TextWrapped(plugin.Description);

                                                // TODO: Hack - ideally would display the GUI for all plugins even if disabled
                                                //       Need some sort of API to ensure the plugins always know the config is
                                                //       valid before displaying the GUI?
                                                if (enabled)
                                                {
                                                    ImGui.SeparatorText(currentLocale.GetString("Popup.Settings.Name"));
                                                    if (Glimpse.Plugins[_currentPlugin].Instance.IsInitialized)
                                                        Glimpse.Plugins[_currentPlugin].Instance.OnGUI(_gui);
                                                }
                                            }

                                            ImGui.EndChild();
                                        }
                                    }

                                    ImGui.EndTable();
                                }

                                ImGui.EndChild();
                            }
                        }

                        ImGui.EndTabItem();
                    }
#endif

                    if (ImGui.BeginTabItem(currentLocale.GetString("Popup.Settings.Tab.About")))
                    {
                        _glimpseLogo ??= Renderer.CreateImage("asset://Icons.Glimpse.png");

                        ImGui.BeginChild("GlimpseLogo", ImGuiChildFlags.AlwaysAutoResize | ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.AutoResizeY);
                        {
                            ImGui.Image(_glimpseLogo, ScaleVec(128, 128));
                            ImGui.EndChild();
                        }

                        ImGui.SameLine();

                        ImGui.BeginChild("GlimpseText", ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.AutoResizeY);
                        {
                            ImGui.PushFont(null, 32);
                            ImGui.TextUnformatted(currentLocale.GetString("Popup.Settings.Tab.About.AppName", Glimpse.Version));
                            ImGui.PopFont();
                            ImGui.TextUnformatted("2026 aquagoose");

                            ImGui.Spacing();
                            ImGui.TextUnformatted(currentLocale.GetString("Popup.Settings.Tab.About.Credits"));
                            
                            ImGui.Spacing();
                            ImGui.TextLink(currentLocale.GetString("Popup.Settings.Tab.About.Website"),
                                "https://glimpseplayer.com");
                            
                            ImGui.SameLine();
                            
                            ImGui.TextLink(currentLocale.GetString("Popup.Settings.Tab.About.Donate"),
                                "https://glimpseplayer.com/donate");
                            
                            ImGui.SameLine();
                            
                            ImGui.TextLink(currentLocale.GetString("Popup.Settings.Tab.About.Repository"),
                                "https://glimpseplayer.com/repo");
                            
                            ImGui.SameLine();
                            
                            ImGui.TextLink(currentLocale.GetString("Popup.Settings.Tab.About.Discord"),
                                "https://glimpseplayer.com/discord");
                            
                            ImGui.EndChild();
                        }

                        ImGui.SeparatorText(currentLocale.GetString("Popup.Settings.Tab.About.OpenSourceLibraries"));
                        {
                            ImGui.BeginChild("OSLibraries");
                            {
                                ImGui.TextLink("mixr", "https://github.com/aquagoose/mixr");
                                ImGui.TextLink("Hexa.NET.ImGui", "https://github.com/HexaEngine/Hexa.NET.ImGui");
                                ImGui.TextLink("Silk.NET", "https://dotnet.github.io/Silk.NET/");
                                ImGui.TextLink("piko", "https://github.com/aquagoose/piko");
                                ImGui.TextLink("TagLibSharp", "https://github.com/mono/taglib-sharp");
                                ImGui.TextLink("ImageSharp", "https://sixlabors.com/products/imagesharp/");
                                ImGui.TextLink("empress", "https://github.com/aquagoose/empress");
                                ImGui.TextLink("DiscordRichPresence", "https://github.com/Lachee/discord-rpc-csharp");
                                ImGui.TextLink("MetaBrainz.MusicBrainz", "https://github.com/Zastai/MetaBrainz.MusicBrainz");
                                ImGui.TextLink("MetaBrainz.MusicBrainz.CoverArt", "https://github.com/Zastai/MetaBrainz.MusicBrainz.CoverArt");

                                ImGui.EndChild();
                            }
                        }

                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();

                    ImGui.EndChild();
                }

                if (ImGui.Button(currentLocale.GetString("Button.Save")))
                {
                    Apply();
                    Close();
                }
                
                ImGui.SameLine();
                
                if (ImGui.Button(currentLocale.GetString("Button.Cancel")))
                    Close();
            }
            
            ImGui.EndPopup();
        }
    }

    private void Apply()
    {
#if !PUBLISH_AOT
        foreach ((_, Plugin plugin) in Glimpse.Plugins)
        {
            if (plugin.Instance.ConfigChanged)
                plugin.Instance.SaveConfigChanges();
        }
#endif
        
        GlimpseConfig oldConfig = Glimpse.Config;
        if (oldConfig == _currentConfig)
            return;

        Logger logger = Glimpse.Logger;
        
        logger.Log("Saving and applying config changes.");
        //Glimpse.Player.Stop();
        
        Glimpse.Config = _currentConfig;
        Glimpse.ConfigManager.WriteConfig(GlimpseConfig.ConfigName, Glimpse.Config);
        
        if (_currentConfig.Appearance.SwapTransportControls != oldConfig.Appearance.SwapTransportControls || _currentConfig.Appearance.Theme != oldConfig.Appearance.Theme || _currentConfig.Appearance.PreferredColorScheme != oldConfig.Appearance.PreferredColorScheme)
            ((GlimpsePlayer) Glimpse.MainWindow).RefreshLayout();
        
#if !PUBLISH_AOT
        foreach ((string id, Plugin plugin) in Glimpse.Plugins)
        {
            // Plugin has been disabled
            if (oldConfig.Plugins.EnabledPlugins.Contains(id) && !_currentConfig.Plugins.EnabledPlugins.Contains(id))
            {
                logger.Log($"Disabling plugin {id}");
                plugin.Instance.Dispose();
            }
            // Plugin has been enabled
            else if (_currentConfig.Plugins.EnabledPlugins.Contains(id) && !oldConfig.Plugins.EnabledPlugins.Contains(id))
            {
                logger.Log($"Enabling plugin {id}");
                plugin.Instance.Initialize(Glimpse);
            }
        }
#endif
    }

    public override void Close()
    {
#if !PUBLISH_AOT
        foreach ((_, Plugin plugin) in Glimpse.Plugins)
            plugin.Instance.CancelConfigChanges();
#endif
        
        base.Close();
    }

    public override void Dispose()
    {
        _glimpseLogo?.Dispose();
    }

    private unsafe class ImmediateGUI : IImmediateGUI, ITableContext
    {
        private Glimpse _glimpse;
        private SDL.DialogFileCallback _callback;
        private Dictionary<int, FileDialogInstance> _fileDialogInstances;

        public float Scale;

        public ImmediateGUI(Glimpse glimpse)
        {
            _glimpse = glimpse;
            _callback = FileCallback;
            _fileDialogInstances = [];
            Scale = 1;
        }

        public void Separator()
        {
            ImGui.Separator();
        }

        public void Separator(string heading)
        {
            ImGui.SeparatorText(heading);
        }

        public void Text(string text)
        {
            ImGui.TextUnformatted(text);
        }

        public void Text(string text, uint fontSize)
        {
            ImGui.PushFont(ImFontPtr.Null, fontSize * Scale);
            ImGui.TextUnformatted(text);
            ImGui.PopFont();
        }

        public bool Button(string text, string? tooltip = null, [CallerLineNumber] int id = 0,
            [CallerMemberName] string caller = "")
        {
            ImGui.PushID(HashCode.Combine(caller, id));
            bool pressed = ImGui.Button(text);
            ImGui.PopID();

            if (tooltip != null)
                ImGui.SetItemTooltipUnformatted(tooltip);

            return pressed;
        }

        public bool Button(string text, Size size, string? tooltip = null, [CallerLineNumber] int id = 0,
            [CallerMemberName] string caller = "")
        {
            ImGui.PushID(HashCode.Combine(caller, id));
            bool pressed = ImGui.Button(text, new Vector2(size.Width * Scale, size.Height * Scale));
            ImGui.PopID();

            if (tooltip != null)
                ImGui.SetItemTooltipUnformatted(tooltip);

            return pressed;
        }

        public bool Checkbox(string text, ref bool ticked, string? tooltip = null, [CallerLineNumber] int id = 0,
            [CallerMemberName] string caller = "")
        {
            ImGui.PushID(HashCode.Combine(caller, id));
            bool pressed = ImGui.Checkbox(text, ref ticked);
            ImGui.PopID();

            if (tooltip != null)
                ImGui.SetItemTooltipUnformatted(tooltip);

            return pressed;
        }

        public bool Dropdown(string label, ref int value, ReadOnlySpan<string> items, string? tooltip = null,
            [CallerLineNumber] int id = 0, [CallerMemberName] string caller = "")
        {
            bool hasItemBeenSelected = false;

            ImGui.PushID(HashCode.Combine(caller, id));

            if (ImGui.BeginCombo(label, items[value]))
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (ImGui.Selectable(items[i]))
                    {
                        hasItemBeenSelected = true;
                        value = i;
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.PopID();

            if (tooltip != null)
                ImGui.SetItemTooltipUnformatted(tooltip);

            return hasItemBeenSelected;
        }

        public bool Input(string? hint, ref string text, string? label = null, string? tooltip = null,
            bool returnTrueOnlyOnEnter = true, [CallerLineNumber] int id = 0, [CallerMemberName] string caller = "")
        {
            ImGui.PushID(HashCode.Combine(caller, id));

            bool wasEdited;
            nuint bufferSize = (nuint) (text.Length > 800 ? 5000 : 1000);
            ImGuiInputTextFlags flags = returnTrueOnlyOnEnter ? ImGuiInputTextFlags.EnterReturnsTrue : 0;

            if (hint == null)
                wasEdited = ImGui.InputText(label ?? "", ref text, bufferSize, flags);
            else
                wasEdited = ImGui.InputTextWithHint(label ?? "", hint, ref text, bufferSize, flags);

            ImGui.PopID();

            if (tooltip != null)
                ImGui.SetItemTooltipUnformatted(tooltip);

            return wasEdited;
        }

        public bool Slider(string label, ref int number, int min, int max, string? tooltip = null,
            [CallerLineNumber] int id = 0, [CallerMemberName] string caller = "")
        {
            ImGui.PushID(HashCode.Combine(caller, id));
            bool wasAdjusted = ImGui.SliderInt(label, ref number, min, max);
            ImGui.PopID();

            if (tooltip != null)
                ImGui.SetItemTooltipUnformatted(tooltip);
            return wasAdjusted;
        }

        public bool Slider(string label, ref float number, float min, float max, string? tooltip = null,
            [CallerLineNumber] int id = 0, [CallerMemberName] string caller = "")
        {
            ImGui.PushID(HashCode.Combine(caller, id));
            bool wasAdjusted = ImGui.SliderFloat(label, ref number, min, max);
            ImGui.PopID();

            if (tooltip != null)
                ImGui.SetItemTooltipUnformatted(tooltip);
            return wasAdjusted;
        }

        public void Table(ReadOnlySpan<TableHeading> headings, Action<ITableContext> callback, int? numRows = 0,
            bool useRelativeWidths = false, [CallerLineNumber] int id = 0, [CallerMemberName] string caller = "")
        {
            int numColumns = headings.Length;

            ImGui.PushID(HashCode.Combine(caller, id));

            if (ImGui.BeginTable("", numColumns, ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable))
            {
                foreach (TableHeading heading in headings)
                {
                    ImGui.TableSetupColumn(heading.Title,
                        useRelativeWidths ? ImGuiTableColumnFlags.WidthStretch : ImGuiTableColumnFlags.WidthFixed,
                        heading.Width);
                }

                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();

                callback(this);

                ImGui.EndTable();
            }

            ImGui.PopID();
        }

        public void ShowFileDialog(FileDialogType type, string? title, ReadOnlySpan<FileFilter> filters,
            Action<string[]?, int> callback, bool allowMany = false, [CallerLineNumber] int id = 0,
            [CallerMemberName] string caller = "")
        {
            int currentID = HashCode.Combine(caller, id);
            // don't reshow the dialog if it is already open.
            if (_fileDialogInstances.ContainsKey(currentID))
                return;

            uint props = SDL.CreateProperties();
            SDL.SetPointerProperty(props, SDL.Prop.FileDialogWindowPointer, _glimpse.MainWindow.Handle.Handle);
            SDL.SetNumberProperty(props, SDL.Prop.FileDialogNfiltersNumber, filters.Length);
            SDL.SetBooleanProperty(props, SDL.Prop.FileDialogManyBoolean, allowMany);
            if (title != null)
                SDL.SetStringProperty(props, SDL.Prop.FileDialogTitleString, title);

            SDL.DialogFileFilter* dialogFilters =
                (SDL.DialogFileFilter*) NativeMemory.Alloc((nuint) (filters.Length * sizeof(SDL.DialogFileFilter)));
            for (int i = 0; i < filters.Length; i++)
            {
                ref readonly FileFilter filter = ref filters[i];
                dialogFilters[i].Name = (sbyte*) Marshal.StringToHGlobalAnsi(filter.Name);
                dialogFilters[i].Pattern = (sbyte*) Marshal.StringToHGlobalAnsi(filter.Pattern);
            }

            SDL.SetPointerProperty(props, SDL.Prop.FileDialogFiltersPointer, (nint) dialogFilters);

            SDL.FileDialogType dialogType = type switch
            {
                FileDialogType.SaveFile => SDL.FileDialogType.Savefile,
                FileDialogType.OpenFile => SDL.FileDialogType.Openfile,
                FileDialogType.OpenFolder => SDL.FileDialogType.Openfolder,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };

            _fileDialogInstances.Add(currentID, new FileDialogInstance(filters.Length, dialogFilters, callback));
            SDL.ShowFileDialogWithProperties(dialogType, _callback, currentID, props);

            SDL.DestroyProperties(props);
        }

        private void FileCallback(IntPtr userdata, sbyte** filelist, int filter)
        {
            _fileDialogInstances.Remove((int) userdata, out FileDialogInstance instance);

            try
            {
                if (filelist == null)
                {
                    _glimpse.Logger.Log($"An error occurred in the file dialog (instance {(int) userdata}): {SDL.GetError()}");
                    return;
                }

                if (filelist[0] == null)
                {
                    instance.Callback(null, filter);
                    return;
                }

                int index = 0;
                List<string> files = [];
                while (filelist[index] != null)
                {
                    string file = new string(filelist[index]);
                    files.Add(file);
                    index++;
                }

                instance.Callback(files.ToArray(), filter);
            }
            finally
            {
                for (int i = 0; i < instance.NumFilters; i++)
                {
                    Marshal.FreeHGlobal((nint) instance.Filters[i].Name);
                    Marshal.FreeHGlobal((nint) instance.Filters[i].Pattern);
                }

                NativeMemory.Free(instance.Filters);
            }
        }

        private struct FileDialogInstance
        {
            public int NumFilters;
            public SDL.DialogFileFilter* Filters;
            public Action<string[]?, int> Callback;

            public FileDialogInstance(int numFilters, SDL.DialogFileFilter* filters, Action<string[]?, int> callback)
            {
                NumFilters = numFilters;
                Filters = filters;
                Callback = callback;
            }
        }

        void ITableContext.NewColumn()
        {
            ImGui.TableNextColumn();
        }

        void ITableContext.NewRow()
        {
            ImGui.TableNextRow();
        }
    }
}