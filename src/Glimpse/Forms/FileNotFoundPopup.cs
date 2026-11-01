using System.Drawing;
using Hexa.NET.ImGui;

namespace Glimpse.Forms;

public class FileNotFoundPopup(string songName) : Popup
{
    protected override void Update(float dt)
    {
        if (ImGui.OpenPopupModal(Glimpse.Locale.GetString("Popup.FileNotFound.Name")))
        {
            ImGui.Text(Glimpse.Locale.GetString("Popup.FileNotFound.Text", songName));
            
            if (ImGui.Button(Glimpse.Locale.GetString("Button.Ok")))
                Close();
            
            ImGui.EndPopup();
        }
    }
}