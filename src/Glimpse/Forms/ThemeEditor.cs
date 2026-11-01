using System.Runtime.InteropServices;
using System.Text.Json;
using Hexa.NET.ImGui;
using piko.SDL3;

namespace Glimpse.Forms;

public class ThemeEditor : Popup
{
    private SDL.DialogFileCallback _fileCallback;
    private Theme _theme;
    private Theme _workingTheme;

    public unsafe ThemeEditor()
    {
        _fileCallback = FileCallback;
    }
    
    public override void Open()
    {
        _theme = Theme.FromImGuiStyle("My Theme", false, ImGui.GetStyle().Colors);
        _workingTheme = _theme;
    }

    protected override unsafe void Update(float dt)
    {
        const string popupName = "Theme Editor";
        bool hasChanges = _workingTheme != _theme;

        ImGui.SetNextWindowSize(ScaleVec(500, 600));
        if (ImGui.Begin(popupName, (hasChanges ? ImGuiWindowFlags.UnsavedDocument : ImGuiWindowFlags.None) | ImGuiWindowFlags.NoDocking))
        {
            ImGui.InputText("Theme Name", ref _theme.Name, 1000);
            Theme.ColorScheme scheme = _workingTheme.DarkColors!.Value;

            ImGui.BeginChild("ThemeColors", ScaleVec(0, 500));
            
            ImGui.ColorEdit4("Text", ref scheme.Text, "Text color.");
            
            ImGui.ColorEdit4("Main Background", ref scheme.MainBackground, "The main Glimpse background color.");
            
            ImGui.ColorEdit4("Popup: Title", ref scheme.PopupTitle, "Popup title background color.");
            ImGui.ColorEdit4("Popup: Background", ref scheme.PopupBackground, "The background color of popups.");
            ImGui.ColorEdit4("Popup: Dim Background", ref scheme.PopupDimBackground, "The color that the background will be dimmed by when a popup is shown.");
            
            ImGui.ColorEdit4("Container", ref scheme.Container, "Container backgrounds, such as text boxes, checkboxes, and sliders.");
            ImGui.ColorEdit4("Container: Hovered", ref scheme.ContainerHovered, "Container backgrounds, such as text boxes, checkboxes, and sliders, when hovered by the mouse.");
            ImGui.ColorEdit4("Container: Clicked", ref scheme.ContainerClicked, "Container backgrounds, such as text boxes, checkboxes, and sliders, when clicked by the mouse.");
            
            ImGui.ColorEdit4("Scrollbar", ref scheme.Scrollbar, "The scrollbar color.");
            ImGui.ColorEdit4("Scrollbar: Background", ref scheme.ScrollbarBackground, "The background that a scrollbar is contained in.");
            ImGui.ColorEdit4("Scrollbar: Hovered", ref scheme.ScrollbarHovered, "The scrollbar color when hovered by the mouse.");
            ImGui.ColorEdit4("Scrollbar: Clicked", ref scheme.ScrollbarClicked, "The scrollbar color when clicked by the mouse.");
            
            ImGui.ColorEdit4("Checkmark", ref scheme.Checkmark, "The color of the checkmark in a checkbox.");
            
            ImGui.ColorEdit4("Slider Grip", ref scheme.SliderGrip, "The grip color of a slider.");
            ImGui.ColorEdit4("Slider Grip: Clicked", ref scheme.SliderGripClicked, "The grip color of a slider, when clicked by the mouse.");
            
            ImGui.ColorEdit4("Button", ref scheme.Button, "The button color.");
            ImGui.ColorEdit4("Button: Hovered", ref scheme.ButtonHovered, "The button color, when hovered by the mouse.");
            ImGui.ColorEdit4("Button: Clicked", ref scheme.ButtonClicked, "The button color, when clicked by the mouse.");
            
            ImGui.ColorEdit4("List Entry: Selected", ref scheme.ListEntrySelected, "The color of a list & table entry, when selected.");
            ImGui.ColorEdit4("List Entry: Hovered", ref scheme.ListEntryHovered, "The color of a list & table entry, when hovered by the mouse.");
            ImGui.ColorEdit4("List Entry: Clicked", ref scheme.ListEntryClicked, "The color of a list & table entry, when clicked by the mouse.");
            
            ImGui.ColorEdit4("Separator", ref scheme.Separator, "The table separator color.");
            ImGui.ColorEdit4("Separator: Hovered", ref scheme.SeparatorHovered, "Table separator color, when hovered by the mouse.");
            ImGui.ColorEdit4("Separator: Clicked", ref scheme.SeparatorClicked, "Table separator color, when clicked by the mouse.");
            
            ImGui.ColorEdit4("Tab", ref scheme.Tab, "The tab color.");
            ImGui.ColorEdit4("Tab: Hovered", ref scheme.TabHovered, "The tab color, when hovered by the mouse.");
            ImGui.ColorEdit4("Tab: Active", ref scheme.TabActive, "The tab color, when this current tab is active.");
            
            ImGui.ColorEdit4("Seek Bar", ref scheme.SeekBar, "The color of the seek bar.");
            
            ImGui.ColorEdit4("Table Header", ref scheme.TableHeader, "The table header color.");
            
            ImGui.ColorEdit4("Link", ref scheme.Link, "Text links.");

            ImGui.EndChild();
            
            _workingTheme.DarkColors = scheme;
            _workingTheme.ApplyImGuiStyle(false, ImGui.GetStyle().Colors);

            if (ImGui.Button("Save"))
            {
                string? location = Environment.GetFolderPath(Environment.SpecialFolder.MyComputer);
                if (string.IsNullOrWhiteSpace(location))
                    location = null;

                nint filterName = Marshal.StringToHGlobalAnsi("Glimpse Theme JSON");
                nint filterPattern = Marshal.StringToHGlobalAnsi("json");
                SDL.DialogFileFilter filter = new SDL.DialogFileFilter((sbyte*) filterName, (sbyte*) filterPattern);
                SDL.ShowSaveFileDialog(_fileCallback, 0, Glimpse.MainWindow.Handle, &filter, 1, location);
                Marshal.FreeHGlobal(filterPattern);
                Marshal.FreeHGlobal(filterName);
            }
            
            ImGui.SameLine();

            if (ImGui.Button("Close"))
            {
                if (hasChanges)
                {
                    AddPopup(new MessageBoxPopup(MessageBoxPopup.Buttons.YesNo, "Unsaved Changes",
                        "Are you sure you want to close? You have unsaved changes.", Close));
                }
                else
                    Close();
            }

            ImGui.End();
        }
    }

    private unsafe void FileCallback(nint userdata, sbyte** filelist, int filter)
    {
        if (filelist == null)
            throw new Exception($"Save file dialog failed: {SDL.GetError()}");

        sbyte** files = filelist;

        while (*files != null)
        {
            string path = new string(*files);
            Glimpse.Logger.Log($"Saving theme to \"{path}\"");
            _theme = _workingTheme;
            File.WriteAllText(path, JsonSerializer.Serialize(_theme, ConfigManager.GetDefaultSerializerOptions()));
            
            files++;
        }
    }
}