using System.Diagnostics;
using System.Numerics;
using Glimpse.Forms.Widgets;
using Glimpse.Library;
using Hexa.NET.ImGui;
using piko.SDL3;
using Image = Glimpse.Graphics.Image;

namespace Glimpse.Forms;

public class ManageLibraryPopup : Popup
{
    private ManageLibraryWidget _widget;
    private float _refreshProgressTimer;

    public override void Open()
    {
        _widget = new ManageLibraryWidget(this);
    }

    protected override void Update(float dt)
    {
        Locale locale = Glimpse.Locale;

        if (ImGui.OpenPopupModal(locale.GetString("Popup.ManageLibrary.Name"), ScaleVec(600, 500)))
        {
            _widget.Update(locale);
            
            if (Glimpse.Library.IsIndexing)
            {
                _refreshProgressTimer += dt;
                if (_refreshProgressTimer >= 1)
                    _refreshProgressTimer -= 1;
            }
            else
                _refreshProgressTimer = 0;
            
            if (ImGui.Button(locale.GetString("Popup.ManageLibrary.UpdateButton")))
                Glimpse.Library.Index();
            ImGui.SetItemTooltipUnformatted(locale.GetString("Popup.ManageLibrary.UpdateButton.Tooltip"));
            
            ImGui.SameLine();
            if (ImGui.Button(locale.GetString("Button.Close")))
                Close();

            if (Glimpse.Library.CurrentlyIndexedFile != null)
            {
                ImGui.SameLine();
                ImGui.Text(locale.GetString("Popup.ManageLibrary.IndexingStatus", Path.GetFileName(Glimpse.Library.CurrentlyIndexedFile)));
                ImGui.SetItemTooltipUnformatted(Glimpse.Library.CurrentlyIndexedFile);
            }
            
            ImGui.ProgressBar(-_refreshProgressTimer, ScaleVec(580, 10), "");
            ImGui.EndPopup();
        }
    }

    public override void Dispose()
    {
        _widget.Dispose();
    }
}