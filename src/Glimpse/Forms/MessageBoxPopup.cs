using Hexa.NET.ImGui;

namespace Glimpse.Forms;

public class MessageBoxPopup : Popup
{
    private static int _idCounter;
    
    public int ID;
    public Buttons ButtonType;
    public string Title;
    public string Message;
    public Action? OnYes;
    public Action? OnNo;
    public bool AlwaysShowOnTop;

    public MessageBoxPopup(Buttons buttonType, string title, string message, Action? onYes = null, Action? onNo = null, bool alwaysShowOnTop = false)
    {
        ID = _idCounter++;
        ButtonType = buttonType;
        Title = title;
        Message = message;
        OnYes = onYes;
        OnNo = onNo;
        AlwaysShowOnTop = alwaysShowOnTop;
    }

    protected override void Update(float dt)
    {
        Locale locale = Glimpse.Locale;

        if (ImGui.OpenPopupModal($"{Title}##MB{ID}", showOnTopOfOtherModals: AlwaysShowOnTop))
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