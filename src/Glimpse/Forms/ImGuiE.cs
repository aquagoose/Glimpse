using System.Numerics;
using Hexa.NET.ImGui;

namespace Glimpse.Forms;

/// <summary>
/// ImGui Extensions
/// </summary>
public static class ImGuiE
{
    extension(ImGui)
    {
        public static void SetItemTooltipUnformatted(string text)
        {
            if (ImGui.BeginItemTooltip())
            {
                ImGui.TextUnformatted(text);
                ImGui.End();
            }
        }

        public static bool BeginTabItemTooltip(string name, string tooltip, bool select = false)
        {
            bool open = ImGui.BeginTabItem(name, select ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None);
            ImGui.SetItemTooltipUnformatted(tooltip);
            return open;
        }

        public static void SetColumnTooltip(string text)
        {
            Vector2 textSize = ImGui.CalcTextSize(text);
            float width = ImGui.GetColumnWidth(-1);
            if (textSize.X > width)
                SetItemTooltipUnformatted(text);
        }

        public static bool SelectButton(string name, ImTextureRef image, Vector2 size, bool selected)
        {
            if (!selected)
                ImGui.PushStyleColor(ImGuiCol.Button, 0);

            bool pressed = ImGui.ImageButton(name, image, size);

            if (!selected)
                ImGui.PopStyleColor();

            return pressed;
        }

        public static bool TextButton(string text)
        {
            ImGui.TextUnformatted(text);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    return true;
            }

            return false;
        }

        public static bool ColorEdit4(string label, ref Vector4 color, string tooltip)
        {
            bool isEdited = ImGui.ColorEdit4(label, ref color, ImGuiColorEditFlags.AlphaBar);
            ImGui.SetItemTooltipUnformatted(tooltip);
            return isEdited;
        }

        public static bool OpenPopupModal(string name, Vector2? size = null, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
        {
            if (!ImGui.IsPopupOpen(name))
                ImGui.OpenPopup(name);

            ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                                           ImGuiWindowFlags.HorizontalScrollbar | flags; 

            Vector2 viewportSize = ImGui.GetMainViewport().Size;
            if (size is Vector2 windowSize)
            {
                // Ensure the popup remains interactable if the main window is smaller than the popup
                ImGui.SetNextWindowSize(Vector2.Min(windowSize, viewportSize));
            }
            else
                windowFlags |= ImGuiWindowFlags.AlwaysAutoResize; // A null size will want an auto sized window.

            ImGui.SetNextWindowPos(ImGui.GetMainViewport().Size / 2, ImGuiCond.Always, new Vector2(0.5f));
            return ImGui.BeginPopupModal(name, windowFlags);
        }

        public static void TextLink(string name, string url)
        {
            if (ImGui.TextLink(name))
                Utils.OpenLink(url);
            ImGui.SetItemTooltipUnformatted(url);
        }
    }
}