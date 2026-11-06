using System.Numerics;
using Glimpse.API;
using Hexa.NET.ImGui;

namespace Glimpse.Forms;

public class UpdateAvailablePopup : Popup
{
    private readonly SemVer _version;
    private readonly string _downloadURL;

    private readonly Action _onSkip;

    public static string SkipVersionPath => Path.Combine(IConfigManager.BaseDir, "SkipVersion.txt");
    
    public UpdateAvailablePopup(SemVer version, string downloadURL, Action onSkip)
    {
        _version = version;
        _downloadURL = downloadURL;
        _onSkip = onSkip;
    }
    
    protected override void Update(float dt)
    {
        Locale locale = Glimpse.Locale;
        
        if (ImGui.OpenPopupModal(locale.GetString("Popup.UpdateAvailable.Name"), ScaleVec(300, 145)))
        {
            ImGui.Text(locale.GetString("Popup.UpdateAvailable.Text", _version.ToString(), Glimpse.Version.ToString()));

            ImGui.Spacing();
            
            if (ImGui.Button(locale.GetString("Button.Yes")))
            {
                Utils.OpenLink(_downloadURL);
                Close();
            }
            ImGui.SetItemTooltipUnformatted(locale.GetString("Popup.UpdateAvailable.YesButtonTooltip", _downloadURL));

            ImGui.SameLine();
            
            if (ImGui.Button(locale.GetString("Button.No")))
                Close();
            
            ImGui.SameLine();

            if (ImGui.Button(locale.GetString("Button.Skip")))
            {
                // write the version to a separate txt file. on startup, this value is compared with the update version
                // this is a separate file because it didn't feel like it fit into the main config or last state config.
                File.WriteAllText(SkipVersionPath, _version.ToString());
                _onSkip();
                Close();
            }
            ImGui.SetItemTooltipUnformatted(locale.GetString("Popup.UpdateAvailable.SkipButtonTooltip"));
            
            ImGui.EndPopup();
        }
    }
}