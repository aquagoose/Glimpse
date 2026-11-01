using Hexa.NET.ImGui;

namespace Glimpse.Forms;

public class MessageBoxPopup : Popup
{
    public Buttons ButtonType;
    public string Title;
    public string Message;
    public Action? OnYes;
    public Action? OnNo;

    public MessageBoxPopup(Buttons buttonType, string title, string message, Action? onYes = null, Action? onNo = null)
    {
        ButtonType = buttonType;
        Title = title;
        Message = message;
        OnYes = onYes;
        OnNo = onNo;
    }

    protected override void Update(float dt)
    {
        Locale locale = Glimpse.Locale;

        if (ImGui.OpenPopupModal(Title))
        {
            ImGui.TextUnformatted(Message);

            switch (ButtonType)
            {
                case Buttons.YesNo:
                {
                    if (ImGui.Button(locale.GetString("Button.Yes")))
                    {
                        OnYes?.Invoke();
                        Close();
                    }

                    ImGui.SameLine();

                    if (ImGui.Button(locale.GetString("Button.No")))
                    {
                        OnNo?.Invoke();
                        Close();
                    }
                    break;
                }

                case Buttons.Ok:
                {
                    if (ImGui.Button(locale.GetString("Button.Ok")))
                    {
                        OnYes?.Invoke();
                        Close();
                    }

                    break;
                }
            }

            ImGui.EndPopup();
        }
    }

    public enum Buttons
    {
        YesNo,
        Ok
    }
}