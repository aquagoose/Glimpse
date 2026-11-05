using System.Runtime.InteropServices;
using Glimpse.Assets;
using Glimpse.Forms;
using Glimpse.Platforms;
using Hexa.NET.ImGui;
using piko.SDL3;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;
using Renderer = Glimpse.Graphics.Renderer;
using Size = System.Drawing.Size;
using Point = System.Drawing.Point;

namespace Glimpse;

public abstract unsafe class Window : IDisposable
{
    private bool _isCreated;
    private string _title;
    private Size _size;
    private float _scale;
    private float _pixelDensity;
    
    private SDL.Window _window;
    private Dictionary<ImGuiMouseCursor, SDL.Cursor> _cursors;
    private ImGuiMouseCursor _lastCursor;

    private List<Popup> _popups;

    public Glimpse Glimpse;
    
    public Renderer Renderer;

    public SDL.Window Handle => _window;

    public string Title
    {
        get
        {
            if (!_isCreated)
                return _title;

            return SDL.GetWindowTitle(_window);
        }
        set
        {
            if (!_isCreated)
                _title = value;
            else
                SDL.SetWindowTitle(_window, value);
        }
    }

    public Point Position
    {
        get
        {
            if (!_isCreated)
                return field;

            int x, y;
            SDL.GetWindowPosition(_window, &x, &y);

            return new Point(x, y);
        }
        set
        {
            if (!_isCreated)
                field = value;
            else
                SDL.SetWindowPosition(_window, value.X, value.Y);
        }
    }

    public Size Size
    {
        get
        {
            if (!_isCreated)
                return _size;

            SDL.GetWindowSize(_window, out int w, out int h);

            // sdl on x11 seems to return the window size in pixels, so we must divide it by the scale
            // to ensure its at the "correct" size value.
            if (OperatingSystem.IsLinux() && SDL.GetCurrentVideoDriver() == "x11")
            {
                w = (int) (w / _scale);
                h = (int) (h / _scale);
            }

            return new Size(w, h);
        }
        set
        {
            if (!_isCreated)
                _size = value;
            else
            {
                // x11 doesn't auto scale windows when resizing, so we must apply the scale here.
                if (OperatingSystem.IsLinux() && SDL.GetCurrentVideoDriver() == "x11")
                    SDL.SetWindowSize(_window, (int) (value.Width * _scale), (int) (value.Height * _scale));
                else
                    SDL.SetWindowSize(_window, value.Width, value.Height);
                
                Renderer.Resize(FramebufferSize);
            }
        }
    }

    public Size FramebufferSize
    {
        get
        {
            if (!_isCreated)
                return _size;

            SDL.GetWindowSizeInPixels(_window, out int w, out int h);

            return new Size(w, h);
        }
    }

    public bool Maximized
    {
        get
        {
            if (!_isCreated)
                return field;

            return (SDL.GetWindowFlags(_window) & SDL.WindowFlags.Maximized) != 0;
        }
        set
        {
            if (!_isCreated)
                field = value;
            else
            {
                if (value)
                    SDL.MaximizeWindow(_window);
                else
                    SDL.RestoreWindow(_window);
            }
        }
    }

    public float Scale => _scale;

    public float PixelDensity => _pixelDensity;

    protected Window()
    {
        Title = "Window";
        Size = new Size(800, 450);

        _popups = new List<Popup>();
    }

    protected virtual void Initialize() { }

    protected virtual void Update(float dt) { }

    public void AddPopup(Popup popup)
    {
        popup.Glimpse = Glimpse;
        popup.Renderer = Renderer;
        popup.Scale = _scale;
        popup.Open();
        _popups.Add(popup);
    }

    protected virtual void OnScaleChanged() { }

    public void NotifyScaleChanged()
    {
        _scale = SDL.GetWindowDisplayScale(_window);
        _pixelDensity = SDL.GetWindowPixelDensity(_window);
        OnScaleChanged();
    }

    internal uint Create(Platform platform)
    {
        SDL.GLSetAttribute(SDL.GLAttr.ContextMajorVersion, 3);
        SDL.GLSetAttribute(SDL.GLAttr.ContextMinorVersion, 3);
        SDL.GLSetAttribute(SDL.GLAttr.ContextProfileMask, (int) SDL.GLProfile.Core);
        SDL.GLSetAttribute(SDL.GLAttr.ContextFlags, (int) SDL.GLContextFlag.ForwardCompatibleFlag);
        SDL.GLSetAttribute(SDL.GLAttr.DepthSize, 0);
        SDL.GLSetAttribute(SDL.GLAttr.AlphaSize, 0);

        uint windowProps = SDL.CreateProperties();
        SDL.SetStringProperty(windowProps, SDL.Prop.WindowCreateTitleString, _title);
        SDL.SetBooleanProperty(windowProps, SDL.Prop.WindowCreateMaximizedBoolean, Maximized);
        SDL.SetBooleanProperty(windowProps, SDL.Prop.WindowCreateOpenglBoolean, true);
        SDL.SetBooleanProperty(windowProps, SDL.Prop.WindowCreateResizableBoolean, true);
        SDL.SetBooleanProperty(windowProps, SDL.Prop.WindowCreateHighPixelDensityBoolean, true);
        // Apparently hiding the window and then showing it later once everything is ready breaks wayland. The scale
        // values it reports are entirely incorrect. I think this is an SDL bug, or perhaps wayland just being stupid.
        // Either way, can't hide the window now. Thanks wayland!
        bool isWayland = SDL.GetCurrentVideoDriver() == "wayland";
        // todo reload the scale value once the window is shown instead ?
        if (!isWayland)
            SDL.SetBooleanProperty(windowProps, SDL.Prop.WindowCreateHiddenBoolean, true);

        // Attempt to set the window centered on the display the mouse cursor is on.
        // If that fails, just make the window centered. On platforms such as Wayland, this will do nothing.
        float mouseX, mouseY;
        SDL.GetGlobalMouseState(&mouseX, &mouseY);
        SDL.Point mousePoint = new() { X = (int) mouseX, Y = (int) mouseY };
        uint display = SDL.GetDisplayForPoint(&mousePoint);
        uint displayPos = display == 0 ? SDL.WindowposCentered : SDL.WindowPosCenteredDisplay(display);
        
        // windows doesn't auto-scale windows when created so we must do that here
        // todo create the window first then resize using the window display scale?
        float displayScale = 1;
        if (OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && !isWayland))
            displayScale = SDL.GetDisplayContentScale(display);
        
        SDL.SetNumberProperty(windowProps, SDL.Prop.WindowCreateWidthNumber, (uint) (_size.Width * displayScale));
        SDL.SetNumberProperty(windowProps, SDL.Prop.WindowCreateHeightNumber, (uint) (_size.Height * displayScale));
        
        SDL.SetNumberProperty(windowProps, SDL.Prop.WindowCreateXNumber, displayPos);
        SDL.SetNumberProperty(windowProps, SDL.Prop.WindowCreateYNumber, displayPos);
        
        _window = SDL.CreateWindowWithProperties(windowProps);

        if (_window.IsNull)
            throw new Exception($"Failed to open window: {SDL.GetError()}");
        
        _scale = SDL.GetWindowDisplayScale(_window);
        _pixelDensity = SDL.GetWindowPixelDensity(_window);

        //SDL.SetWindowSize(_window, _size.Width, _size.Height);

        // Do not set the icon on Windows or macOS, as they inherit the icon from the .ico or .icns file.
        // We check if the OS is *not* windows or macos specifically so that the icon is always set as a fallback.
        // On Linux systems, BSD etc the icon must be set manually.
        // TODO: Check the behaviour with a .desktop file. It might set the icon automatically?
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            using (Stream iconStream = Asset.GetAssetStream("Icons.Glimpse.png"))
            using (Image<Rgba32> icon = Image.Load<Rgba32>(iconStream))
            {
                byte[] pixels = new byte[icon.Width * icon.Height * sizeof(Rgba32)];
                icon.CopyPixelDataTo(pixels);

                SDL.Surface surface;
                fixed (byte* pData = pixels)
                {
                    surface = SDL.CreateSurfaceFrom(icon.Width, icon.Height, SDL.PixelFormat.Abgr8888, (nint) pData,
                        icon.Width * 4);
                }

                SDL.SetWindowIcon(_window, surface);
            }
        }

        _cursors = [];
        _lastCursor = ImGuiMouseCursor.Arrow;
        for (int i = 0; i < (int) ImGuiMouseCursor.Count; i++)
        {
            ImGuiMouseCursor cursor = (ImGuiMouseCursor) i;
            SDL.SystemCursor systemCursor = cursor switch
            {
                ImGuiMouseCursor.None => SDL.SystemCursor.Default,
                ImGuiMouseCursor.Arrow => SDL.SystemCursor.Default,
                ImGuiMouseCursor.TextInput => SDL.SystemCursor.Text,
                ImGuiMouseCursor.ResizeAll => SDL.SystemCursor.NeswResize,
                ImGuiMouseCursor.ResizeNs => SDL.SystemCursor.NsResize,
                ImGuiMouseCursor.ResizeEw => SDL.SystemCursor.EwResize,
                ImGuiMouseCursor.ResizeNesw => SDL.SystemCursor.NeswResize,
                ImGuiMouseCursor.ResizeNwse => SDL.SystemCursor.NwseResize,
                ImGuiMouseCursor.Hand => SDL.SystemCursor.Pointer,
                ImGuiMouseCursor.Wait => SDL.SystemCursor.Wait,
                ImGuiMouseCursor.Progress => SDL.SystemCursor.Progress,
                ImGuiMouseCursor.NotAllowed => SDL.SystemCursor.NotAllowed,
                _ => throw new ArgumentOutOfRangeException()
            };

            _cursors[(ImGuiMouseCursor) i] = SDL.CreateSystemCursor(systemCursor);
        }
        
        _isCreated = true;

        Renderer = new Renderer(_window, FramebufferSize);
        
        if (OperatingSystem.IsWindows())
            Size = _size;
        
        Initialize();
        
        if (OperatingSystem.IsWindows())
        {
            uint props = SDL.GetWindowProperties(_window);
            nint hwnd = SDL.GetPointerProperty(props, SDL.Prop.WindowWin32HwndPointer, 0);
            platform.InitializeMainWindow(hwnd);
        }

        if (!isWayland)
            SDL.ShowWindow(_window);

        return SDL.GetWindowID(_window);
    }

    internal void UpdateWindow(float dt)
    {
        ImGui.SetCurrentContext(Renderer.ImGui.ImGuiContext);
        ImGui.GetIO().DeltaTime = 1 / 60.0f;
        ImGui.NewFrame();

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


        ImGuiMouseCursor cursor = ImGui.GetMouseCursor();
        if (cursor != _lastCursor)
        {
            _lastCursor = cursor;
            SDL.SetCursor(_cursors[cursor]);
        }
    }

    internal void Present()
    {
        Renderer.ImGui.Render();
        Renderer.Present(true);
    }

    public virtual void Dispose()
    {
        SDL.DestroyWindow(_window);
        
        foreach ((_, SDL.Cursor cursor) in _cursors)
            SDL.DestroyCursor(cursor);
    }
    
    
}