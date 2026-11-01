using System.Numerics;
using Glimpse.Graphics;

namespace Glimpse.Forms;

public abstract class Popup : IDisposable
{
    private List<Popup> _popups;

    public bool IsRemoved;

    public Glimpse Glimpse;

    public Renderer Renderer;

    public float Scale;

    protected Popup()
    {
        _popups = [];
    }

    public virtual void Open() { }

    protected abstract void Update(float dt);

    public void UpdatePopup(float dt)
    {
        Update(dt);

        for (int i = 0; i < _popups.Count; i++)
        {
            Popup popup = _popups[i];
            popup.UpdatePopup(dt);

            if (popup.IsRemoved)
            {
                popup.Dispose();
                _popups.RemoveAt(i);
                i--;
            }
        }
    }

    public virtual void Close()
    {
        IsRemoved = true;
    }
    
    public Vector2 ScaleVec(float x, float y)
    {
        float scale = Scale;
        return new Vector2((int) (x * scale), (int) (y * scale));
    }

    public Vector2 ScaleVec(float scalar)
        => ScaleVec(scalar, scalar);

    protected void AddPopup(Popup popup)
    {
        popup.Glimpse = Glimpse;
        popup.Renderer = Renderer;
        popup.Scale = Scale;
        popup.Open();
        _popups.Add(popup);
    }

    public virtual void Dispose() { }
}