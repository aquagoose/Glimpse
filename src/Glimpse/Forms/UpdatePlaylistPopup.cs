using Hexa.NET.ImGui;

namespace Glimpse.Forms;

public class UpdatePlaylistPopup : Popup
{
    private string _title;
    private string _textBoxHint;
    private string _buttonText;
    private readonly Action<string> _onUpdate;
    private string _text;

    public UpdatePlaylistPopup(string title, string textBoxHint, string buttonText, Action<string> onUpdate, string text = "")
    {
        _title = title;
        _textBoxHint = textBoxHint;
        _buttonText = buttonText;
        _onUpdate = onUpdate;
        _text = text;
    }

    protected override void Update(float dt)
    {
        Locale locale = Glimpse.Locale;

        if (ImGui.OpenPopupModal(_title))
        {
            ImGui.InputTextWithHint("##PlaylistName", _textBoxHint, ref _text, 1000);

            if (ImGui.Button(_buttonText))
            {
                _onUpdate(_text);
                Close();
            }

            ImGui.SameLine();

            if (ImGui.Button(locale.GetString("Button.Cancel")))
                Close();

            ImGui.EndPopup();
        }
    }
}