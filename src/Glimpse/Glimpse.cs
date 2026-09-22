using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using Glimpse.API;
using Glimpse.API.Library;
using Glimpse.Audio;
using Glimpse.Configs;
using Glimpse.Forms;
using Glimpse.Graphics;
using Glimpse.Library;
using Glimpse.Platforms;
using Hexa.NET.ImGui;
using piko.SDL3;
using Version = System.Version;

namespace Glimpse;

public class Glimpse : IGlimpse, IDisposable
{
#if DEBUG
    public const string PipeServerName = "GlimpsePlayerDEBUG";
#else
    public const string PipeServerName = "GlimpsePlayer";
#endif
    
    private List<Window> _windows;
    private Dictionary<uint, Window> _windowIds;
    private AssemblyLoadContext _pluginsContext;
    private int _mainThreadID;
    private SDL.EventFilter _eventFilter;
    
    private NamedPipeServerStream _pipeServer;
    
    private bool _shouldFocusWindow;
    private List<string> _playFiles;
    private float _currentDeltaTime;

    public Logger Logger;

    public SemVer Version;
    
    public ConfigManager ConfigManager;

    public GlimpseConfig Config;

    public Platform Platform;

    public AudioPlayer Player;

    public Locale Locale;

    public MusicLibrary Library;
    
    public Dictionary<string, Plugin> Plugins;

    public Window MainWindow => _windows[0];

    public void AddWindow(Window window)
    {
        window.Glimpse = this;
        uint id = window.Create(Platform);
        _windows.Add(window);
        _windowIds.Add(id, window);
    }

    public unsafe void Run(Window window, string[] args)
    {
        _playFiles = [];
        
        Logger = new Logger();
        
        _pipeServer = new NamedPipeServerStream(PipeServerName, PipeDirection.In);
        _pipeServer.BeginWaitForConnection(OnPipeServerConnection, this);

        // get the ass
        Assembly? ass = Assembly.GetEntryAssembly();
        // no ass
        if (ass == null)
            Version = new SemVer();
        else
        {
            // i love as- no nevermind
            AssemblyInformationalVersionAttribute? infoVersion =
                ass.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

#if !DEBUG
            try
            {
#endif
                Version = new SemVer(infoVersion!.InformationalVersion);
#if !DEBUG
            }
            catch (Exception e)
            {
                Logger.Log($"Could not parse version number! Using fallback. {e}.");

                Version? assVersion = ass.GetName().Version;
                // ass does not have a version
                if (assVersion == null)
                    Version = new SemVer();
                else
                    Version = new SemVer(assVersion.Major, assVersion.Minor, assVersion.Build, assVersion.Revision);
            }
#endif
        }
            
        Logger.Log($"Glimpse {Version}");
        SDL.SetAppMetadataProperty(SDL.Prop.AppMetadataNameString, "Glimpse");
        SDL.SetAppMetadataProperty(SDL.Prop.AppMetadataVersionString, Version.ToString());
        SDL.SetAppMetadataProperty(SDL.Prop.AppMetadataIdentifierString, "com.aquagoose.glimpse");
        SDL.SetAppMetadataProperty(SDL.Prop.AppMetadataCreatorString, "aquagoose");
        //SDL.SetAppMetadataProperty(SDL.Prop.AppMetadataCopyrightString, "Copyright (C) aquagoose 2026");
        SDL.SetAppMetadataProperty(SDL.Prop.AppMetadataUrlString, "https://www.glimpseplayer.com");
        SDL.SetAppMetadataProperty(SDL.Prop.AppMetadataTypeString, "mediaplayer");
        
        Logger.Log("Creating config manager.");
        ConfigManager = new ConfigManager(Logger);
        
        Platform = Platform.AutoDetect();
        Logger.Log($"Detected platform {Platform.GetType()}");
        
        Logger.Log("Loading player configuration.");
        if (!ConfigManager.TryGetConfig(GlimpseConfig.ConfigName, out Config))
        {
            Logger.Log("   ... Failed: Searching for alpha config.");
            
            Config = new GlimpseConfig();
            // Seamlessly transition an alpha configuration (< 0.1.0) over to the new config system. 
            if (ConfigManager.TryGetConfig(OldConfig.ConfigName, out OldConfig oldConfig))
            {
                Logger.Log("   ... Populating new config.");
                oldConfig.PopulateNewConfig(ref Config);
                string configPath = Path.Combine(IConfigManager.BaseDir, OldConfig.ConfigName + ".json");
                File.Delete(configPath); // Delete old config as its no longer needed, and will prevent confusion if the user tries to edit the file.
            }
            else
                Logger.Log("   ... Failed: Creating new config.");
            
            ConfigManager.WriteConfig(GlimpseConfig.ConfigName, Config);
        }
        
        SDL.SetHint(SDL.Hint.MouseFocusClickthrough, "1");
        SDL.SetHint(SDL.Hint.VideoAllowScreensaver, "1");
        //SDL.SetHint(SDL.Hints.VideoWaylandScaleToDisplay, "1");
        
        if (!SDL.Init(SDL.InitFlags.Video | SDL.InitFlags.Events))
            throw new Exception("Failed to initialize SDL.");

        _windows = new List<Window>();
        _windowIds = new Dictionary<uint, Window>();

        Player = new AudioPlayer(Logger, new PlayerSettings(Config.Audio.SampleRate, Config.Audio.Volume, Config.Audio.SpeedAdjust));

        Logger.Log("Loading locales.");
        
        const string defaultLocale = "en-gb";
        string requestedLocale = Config.General.Language ?? CultureInfo.CurrentUICulture.Name.ToLower();
        Logger.Log($"Requesting locale '{requestedLocale}'.");
        if (Locale.AvailableLocales.Locales.ContainsKey(requestedLocale))
        {
            Locale = Locale.LoadLocale(requestedLocale);
            Config.General.Language = requestedLocale;
        }
        else
        {
            Logger.Log($"Requested locale '{requestedLocale}' is not available. Loading default locale '{defaultLocale}'.");
            Locale = Locale.LoadLocale(defaultLocale);
            Config.General.Language = defaultLocale;
        }

        /*if (!ConfigManager.TryGetConfig(MusicDatabase.DatabaseName, out Database))
        {
            Database = new MusicDatabase();
            ConfigManager.WriteConfig(MusicDatabase.DatabaseName, Database);
        }*/

        //Database!.Logger = Logger;
        //Database!.Refresh();
        Library = new MusicLibrary(Logger, Player);
        //Database.Index(); // todo setting to index on startup
        
        AddWindow(window);
        
#if !PUBLISH_AOT
        Logger.Log("Searching for 'Plugins' directory.");
        string pluginsLocation = Utils.GetPath("Plugins");
        _pluginsContext = new AssemblyLoadContext("Plugins");
        Plugins = [];
        
        if (Directory.Exists(pluginsLocation))
        {
            Logger.Log($"Searching for plugins in {pluginsLocation}");
            foreach (string file in Directory.GetFiles(pluginsLocation, "*.json", SearchOption.AllDirectories))
                TryLoadPlugin(file, out _);
        }
#endif

        _mainThreadID = Environment.CurrentManagedThreadId;
        _eventFilter = WindowExposedEventWatch;
        
        SDL.AddEventWatch(_eventFilter, 0);

        if (args.Length > 0)
        {
            foreach (string arg in args)
                ProcessFile(arg, ref _playFiles);
        }
        
        Stopwatch sw = Stopwatch.StartNew();

        while (_windows.Count > 0)
        {
            SDL.Event winEvent;

            SDL.WindowFlags flags = SDL.GetWindowFlags(SDL.GetWindowFromID(_windowIds.First().Key));
            if ((flags & SDL.WindowFlags.Minimized) != 0 && SDL.WaitEvent(&winEvent) ||
                (flags & SDL.WindowFlags.InputFocus) == 0 && SDL.WaitEventTimeout(&winEvent, 250))
            {
                ProcessEvent(winEvent);
            }

            while (SDL.PollEvent(&winEvent))
                ProcessEvent(winEvent);

            if (_shouldFocusWindow)
            {
                _shouldFocusWindow = false;
                SDL.Window handle = MainWindow.Handle;
                SDL.RaiseWindow(handle);
            }

            if (_playFiles.Count > 0)
            {
                Player.QueueTracks(_playFiles, QueueSlot.Clear);
                // in case the user provides a file with invalid track paths,
                // keep advancing until a valid track is found.
                int trackIndex = 0;
                while (!Player.TryChangeTrack(trackIndex))
                    trackIndex++;
                Player.Play();
                _playFiles.Clear();
            }

            _currentDeltaTime = (float) sw.Elapsed.TotalSeconds;
            sw.Restart();

            foreach (Window wnd in _windows)
            {
                if (ImGui.GetIO().WantTextInput)
                {
                    if (!SDL.TextInputActive(wnd.Handle))
                        SDL.StartTextInput(wnd.Handle);
                }
                else if (SDL.TextInputActive(wnd.Handle))
                    SDL.StopTextInput(wnd.Handle);

                wnd.SetActive();
                wnd.UpdateWindow(_currentDeltaTime);
                wnd.Present();
            }
        }
        
        SDL.RemoveEventWatch(_eventFilter, 0);
    }

    private unsafe bool WindowExposedEventWatch(nint userdata, SDL.Event* @event)
    {
        int threadId = Environment.CurrentManagedThreadId;
        if (threadId != _mainThreadID)
            //throw new Exception($"Event {(SDL.EventType) @event.Type} was sent on a separate thread.");
            Logger.Log($"Event {(SDL.EventType) @event->Type} was sent on a separate thread!!! Thread ID: {threadId}, Main Thread ID: {_mainThreadID}");
        
        switch ((SDL.EventType) @event->Type)
        {
            case SDL.EventType.WindowResized:
            {
                Window wnd = _windowIds[@event->Window.WindowID];
                wnd.SetActive();
                wnd.Renderer.Resize(wnd.FramebufferSize);
                break;
            }
            
            case SDL.EventType.WindowDisplayScaleChanged:
            {
                Logger.Log("Window Display Scale Changed");
                Window wnd = _windowIds[@event->Window.WindowID];
                wnd.SetActive();
                
                // windows being windows does not auto resize the window correctly
                // so we must resize the window by the inverse of the current window scale
                if (OperatingSystem.IsWindows())
                {
                    Size winSize = wnd.Size;
                    float scaleDiff = SDL.GetWindowDisplayScale(wnd.Handle) / wnd.Scale;
                    wnd.Size = new Size((int) (winSize.Width * scaleDiff), (int) (winSize.Height * scaleDiff));
                }

                wnd.Renderer.Resize(wnd.FramebufferSize);
                wnd.NotifyScaleChanged();
                Logger.Log("done.");
                break;
            }
            
            case SDL.EventType.WindowExposed:
            {
                bool isResized = @event->Window.Data1 == 1;
                if (isResized)
                {
                    foreach (Window wnd in _windows)
                    {
                        wnd.SetActive();
                        wnd.UpdateWindow(_currentDeltaTime);
                        wnd.Present();
                    }
                }

                break;
            }
            
            default:
                return true;
        }

        return false;
    }

    private ImGuiMouseButton? SdlButtonToImGui(SDL.MouseButtonFlags button)
    {
        return button switch
        {
            SDL.MouseButtonFlags.Left => ImGuiMouseButton.Left,
            SDL.MouseButtonFlags.Right => ImGuiMouseButton.Right,
            SDL.MouseButtonFlags.Middle => ImGuiMouseButton.Middle,
            _ => null
        };
    }

    private ImGuiKey SdlKeyToImGui(SDL.Keycode key)
    {
        return key switch
        {
            SDL.Keycode.Tab => ImGuiKey.Tab,
            SDL.Keycode.Left => ImGuiKey.LeftArrow,
            SDL.Keycode.Right => ImGuiKey.RightArrow,
            SDL.Keycode.Up => ImGuiKey.UpArrow,
            SDL.Keycode.Down => ImGuiKey.DownArrow,
            SDL.Keycode.Pageup => ImGuiKey.PageUp,
            SDL.Keycode.Pagedown => ImGuiKey.PageDown,
            SDL.Keycode.Home => ImGuiKey.Home,
            SDL.Keycode.End => ImGuiKey.End,
            SDL.Keycode.Insert => ImGuiKey.Insert,
            SDL.Keycode.Delete => ImGuiKey.Delete,
            SDL.Keycode.Backspace => ImGuiKey.Backspace,
            SDL.Keycode.Space => ImGuiKey.Space,
            SDL.Keycode.Return => ImGuiKey.Enter,
            SDL.Keycode.Escape => ImGuiKey.Escape,
            SDL.Keycode.Lctrl => ImGuiKey.LeftCtrl,
            SDL.Keycode.Lshift => ImGuiKey.LeftShift,
            SDL.Keycode.Lalt => ImGuiKey.LeftAlt,
            SDL.Keycode.Lgui => ImGuiKey.LeftSuper,
            SDL.Keycode.Rctrl => ImGuiKey.RightCtrl,
            SDL.Keycode.Rshift => ImGuiKey.RightShift,
            SDL.Keycode.Ralt => ImGuiKey.RightAlt,
            SDL.Keycode.Rgui => ImGuiKey.RightSuper,
            SDL.Keycode.Menu => ImGuiKey.Menu,
            SDL.Keycode._0 => ImGuiKey.Key0,
            SDL.Keycode._1 => ImGuiKey.Key1,
            SDL.Keycode._2 => ImGuiKey.Key2,
            SDL.Keycode._3 => ImGuiKey.Key3,
            SDL.Keycode._4 => ImGuiKey.Key4,
            SDL.Keycode._5 => ImGuiKey.Key5,
            SDL.Keycode._6 => ImGuiKey.Key6,
            SDL.Keycode._7 => ImGuiKey.Key7,
            SDL.Keycode._8 => ImGuiKey.Key8,
            SDL.Keycode._9 => ImGuiKey.Key9,
            SDL.Keycode.A => ImGuiKey.A,
            SDL.Keycode.B => ImGuiKey.B,
            SDL.Keycode.C => ImGuiKey.C,
            SDL.Keycode.D => ImGuiKey.D,
            SDL.Keycode.E => ImGuiKey.E,
            SDL.Keycode.F => ImGuiKey.F,
            SDL.Keycode.G => ImGuiKey.G,
            SDL.Keycode.H => ImGuiKey.H,
            SDL.Keycode.I => ImGuiKey.I,
            SDL.Keycode.J => ImGuiKey.J,
            SDL.Keycode.K => ImGuiKey.K,
            SDL.Keycode.L => ImGuiKey.L,
            SDL.Keycode.M => ImGuiKey.M,
            SDL.Keycode.N => ImGuiKey.N,
            SDL.Keycode.O => ImGuiKey.O,
            SDL.Keycode.P => ImGuiKey.P,
            SDL.Keycode.Q => ImGuiKey.Q,
            SDL.Keycode.R => ImGuiKey.R,
            SDL.Keycode.S => ImGuiKey.S,
            SDL.Keycode.T => ImGuiKey.T,
            SDL.Keycode.U => ImGuiKey.U,
            SDL.Keycode.V => ImGuiKey.V,
            SDL.Keycode.W => ImGuiKey.W,
            SDL.Keycode.X => ImGuiKey.X,
            SDL.Keycode.Y => ImGuiKey.Y,
            SDL.Keycode.Z => ImGuiKey.Z,
            SDL.Keycode.F1 => ImGuiKey.F1,
            SDL.Keycode.F2 => ImGuiKey.F2,
            SDL.Keycode.F3 => ImGuiKey.F3,
            SDL.Keycode.F4 => ImGuiKey.F4,
            SDL.Keycode.F5 => ImGuiKey.F5,
            SDL.Keycode.F6 => ImGuiKey.F6,
            SDL.Keycode.F7 => ImGuiKey.F7,
            SDL.Keycode.F8 => ImGuiKey.F8,
            SDL.Keycode.F9 => ImGuiKey.F9,
            SDL.Keycode.F10 => ImGuiKey.F10,
            SDL.Keycode.F11 => ImGuiKey.F11,
            SDL.Keycode.F12 => ImGuiKey.F12,
            SDL.Keycode.F13 => ImGuiKey.F13,
            SDL.Keycode.F14 => ImGuiKey.F14,
            SDL.Keycode.F15 => ImGuiKey.F15,
            SDL.Keycode.F16 => ImGuiKey.F16,
            SDL.Keycode.F17 => ImGuiKey.F17,
            SDL.Keycode.F18 => ImGuiKey.F18,
            SDL.Keycode.F19 => ImGuiKey.F19,
            SDL.Keycode.F20 => ImGuiKey.F20,
            SDL.Keycode.F21 => ImGuiKey.F21,
            SDL.Keycode.F22 => ImGuiKey.F22,
            SDL.Keycode.F23 => ImGuiKey.F23,
            SDL.Keycode.F24 => ImGuiKey.F24,
            SDL.Keycode.Apostrophe => ImGuiKey.Apostrophe,
            SDL.Keycode.Comma => ImGuiKey.Comma,
            SDL.Keycode.Minus => ImGuiKey.Minus,
            SDL.Keycode.Period => ImGuiKey.Period,
            SDL.Keycode.Slash => ImGuiKey.Slash,
            SDL.Keycode.Semicolon => ImGuiKey.Semicolon,
            SDL.Keycode.Equals => ImGuiKey.Equal,
            SDL.Keycode.Leftbracket => ImGuiKey.LeftBracket,
            SDL.Keycode.Backslash => ImGuiKey.Backslash,
            SDL.Keycode.Rightbracket => ImGuiKey.RightBracket,
            SDL.Keycode.Grave => ImGuiKey.GraveAccent,
            SDL.Keycode.Capslock => ImGuiKey.CapsLock,
            SDL.Keycode.Scrolllock => ImGuiKey.ScrollLock,
            SDL.Keycode.Numlockclear => ImGuiKey.NumLock,
            SDL.Keycode.Printscreen => ImGuiKey.PrintScreen,
            SDL.Keycode.Pause => ImGuiKey.Pause,
            SDL.Keycode.Kp0 => ImGuiKey.Keypad0,
            SDL.Keycode.Kp1 => ImGuiKey.Keypad1,
            SDL.Keycode.Kp2 => ImGuiKey.Keypad2,
            SDL.Keycode.Kp3 => ImGuiKey.Keypad3,
            SDL.Keycode.Kp4 => ImGuiKey.Keypad4,
            SDL.Keycode.Kp5 => ImGuiKey.Keypad5,
            SDL.Keycode.Kp6 => ImGuiKey.Keypad6,
            SDL.Keycode.Kp7 => ImGuiKey.Keypad7,
            SDL.Keycode.Kp8 => ImGuiKey.Keypad8,
            SDL.Keycode.Kp9 => ImGuiKey.Keypad9,
            SDL.Keycode.KpDecimal => ImGuiKey.KeypadDecimal,
            SDL.Keycode.KpDivide => ImGuiKey.KeypadDivide,
            SDL.Keycode.KpMultiply => ImGuiKey.KeypadMultiply,
            SDL.Keycode.KpMinus => ImGuiKey.KeypadSubtract,
            SDL.Keycode.KpPlus => ImGuiKey.KeypadAdd,
            SDL.Keycode.KpEnter => ImGuiKey.KeypadEnter,
            SDL.Keycode.KpEquals => ImGuiKey.KeypadEqual,
            /*SDL.Keycode.AppBack => ImGuiKey.AppBack,
            SDL.Keycode.AppForward => ImGuiKey.AppForward,
            SDL.Keycode.Oem102 => ImGuiKey.Oem102,
            SDL.Keycode.GamepadStart => ImGuiKey.GamepadStart,
            SDL.Keycode.GamepadBack => ImGuiKey.GamepadBack,
            SDL.Keycode.GamepadFaceLeft => ImGuiKey.GamepadFaceLeft,
            SDL.Keycode.GamepadFaceRight => ImGuiKey.GamepadFaceRight,
            SDL.Keycode.GamepadFaceUp => ImGuiKey.GamepadFaceUp,
            SDL.Keycode.GamepadFaceDown => ImGuiKey.GamepadFaceDown,
            SDL.Keycode.GamepadDpadLeft => ImGuiKey.GamepadDpadLeft,
            SDL.Keycode.GamepadDpadRight => ImGuiKey.GamepadDpadRight,
            SDL.Keycode.GamepadDpadUp => ImGuiKey.GamepadDpadUp,
            SDL.Keycode.GamepadDpadDown => ImGuiKey.GamepadDpadDown,
            SDL.Keycode.GamepadL1 => ImGuiKey.GamepadL1,
            SDL.Keycode.GamepadR1 => ImGuiKey.GamepadR1,
            SDL.Keycode.GamepadL2 => ImGuiKey.GamepadL2,
            SDL.Keycode.GamepadR2 => ImGuiKey.GamepadR2,
            SDL.Keycode.GamepadL3 => ImGuiKey.GamepadL3,
            SDL.Keycode.GamepadR3 => ImGuiKey.GamepadR3,
            SDL.Keycode.GamepadLStickLeft => ImGuiKey.GamepadLStickLeft,
            SDL.Keycode.GamepadLStickRight => ImGuiKey.GamepadLStickRight,
            SDL.Keycode.GamepadLStickUp => ImGuiKey.GamepadLStickUp,
            SDL.Keycode.GamepadLStickDown => ImGuiKey.GamepadLStickDown,
            SDL.Keycode.GamepadRStickLeft => ImGuiKey.GamepadRStickLeft,
            SDL.Keycode.GamepadRStickRight => ImGuiKey.GamepadRStickRight,
            SDL.Keycode.GamepadRStickUp => ImGuiKey.GamepadRStickUp,
            SDL.Keycode.GamepadRStickDown => ImGuiKey.GamepadRStickDown,
            SDL.Keycode.MouseLeft => ImGuiKey.MouseLeft,
            SDL.Keycode.MouseRight => ImGuiKey.MouseRight,
            SDL.Keycode.MouseMiddle => ImGuiKey.MouseMiddle,
            SDL.Keycode.MouseX1 => ImGuiKey.MouseX1,
            SDL.Keycode.MouseX2 => ImGuiKey.MouseX2,
            SDL.Keycode.MouseWheelX => ImGuiKey.MouseWheelX,
            SDL.Keycode.MouseWheelY => ImGuiKey.MouseWheelY,
            SDL.Keycode.ReservedForModCtrl => ImGuiKey.ReservedForModCtrl,
            SDL.Keycode.ReservedForModShift => ImGuiKey.ReservedForModShift,
            SDL.Keycode.ReservedForModAlt => ImGuiKey.ReservedForModAlt,
            SDL.Keycode.ReservedForModSuper => ImGuiKey.ReservedForModSuper,
            SDL.Keycode.NamedKeyEnd => ImGuiKey.NamedKeyEnd,
            SDL.Keycode.NamedKeyCount => ImGuiKey.NamedKeyCount,
            SDL.Keycode.LCtrl => ImGuiKey.ModCtrl,
            SDL.Keycode.ModShift => ImGuiKey.ModShift,
            SDL.Keycode.ModAlt => ImGuiKey.ModAlt,
            SDL.Keycode.ModSuper => ImGuiKey.ModSuper,
            SDL.Keycode.ModMask => ImGuiKey.ModMask*/
            _ => ImGuiKey.None
        };
    }
    
    public void Dispose()
    {
        ConfigManager.WriteConfig(GlimpseConfig.ConfigName, Config);
        
        if (Plugins != null)
        {
            Logger.Log("Disposing all plugins.");
            foreach ((string name, Plugin plugin) in Plugins)
            {
                Logger.Log($"Disposing plugin {name}");
                plugin.Instance.Dispose();
            }
        }
        
        Player.Dispose();
        //ConfigManager.WriteConfig(MusicDatabase.DatabaseName, Database);
        Platform.Dispose();
        _pipeServer.Close();
        
        SDL.Quit();
    }

    private void ProcessEvent(SDL.Event winEvent)
    {
        switch ((SDL.EventType) winEvent.Type)
        {
            case SDL.EventType.WindowCloseRequested:
            {
                if (!_windowIds.TryGetValue(winEvent.Window.WindowID, out Window wnd))
                    break;
                
                wnd.Dispose();
                _windowIds.Remove(winEvent.Window.WindowID);
                _windows.Remove(wnd);
                break;
            }

            case SDL.EventType.Quit:
            {
                foreach (Window window in _windows)
                    window.Dispose();

                _windows.Clear();
                _windowIds.Clear();

                break;
            }

            case SDL.EventType.MouseMotion:
            {
                Window wnd = _windowIds[winEvent.Motion.WindowID];
                ImGui.SetCurrentContext(wnd.Renderer.ImGui.ImGuiContext);

                ImGui.GetIO().AddMousePosEvent(winEvent.Motion.X * MainWindow.PixelDensity,
                    winEvent.Motion.Y * MainWindow.PixelDensity);
                break;
            }

            case SDL.EventType.MouseButtonDown:
            {
                Window wnd = _windowIds[winEvent.Button.WindowID];
                ImGui.SetCurrentContext(wnd.Renderer.ImGui.ImGuiContext);
                
                if (SdlButtonToImGui((SDL.MouseButtonFlags) winEvent.Button.Button) is { } button)
                    ImGui.GetIO().AddMouseButtonEvent((int) button, true);
                break;
            }
            
            case SDL.EventType.MouseButtonUp:
            {
                Window wnd = _windowIds[winEvent.Button.WindowID];
                ImGui.SetCurrentContext(wnd.Renderer.ImGui.ImGuiContext);
                
                if (SdlButtonToImGui((SDL.MouseButtonFlags) winEvent.Button.Button) is { } button) // todo piko: this isn't mapped to MouseButtonFlags?
                    ImGui.GetIO().AddMouseButtonEvent((int) button, false);
                break;
            }

            case SDL.EventType.MouseWheel:
            {
                Window wnd = _windowIds[winEvent.Button.WindowID];
                ImGui.SetCurrentContext(wnd.Renderer.ImGui.ImGuiContext);
                
                ImGui.GetIO().AddMouseWheelEvent(-winEvent.Wheel.X, winEvent.Wheel.Y);
                break;
            }

            case SDL.EventType.TextInput:
            {
                Window wnd = _windowIds[winEvent.Text.WindowID];
                ImGui.SetCurrentContext(wnd.Renderer.ImGui.ImGuiContext);

                unsafe
                {
                    foreach (char c in new string((sbyte*) winEvent.Text.Text))
                        ImGui.GetIO().AddInputCharacter(c);
                }

                break;
            }

            case SDL.EventType.KeyDown:
            {
                if (winEvent.Key.Repeat)
                    break;
                
                ImGui.GetIO().AddKeyEvent(SdlKeyToImGui(winEvent.Key.Key), true);
                break;
            }

            case SDL.EventType.KeyUp:
            {
                ImGui.GetIO().AddKeyEvent(SdlKeyToImGui(winEvent.Key.Key), false);
                break;
            }

            case SDL.EventType.DropFile:
            {
                string fileName;
                unsafe { fileName = new string(winEvent.Drop.Data); } // lol
                ProcessFile(fileName, ref _playFiles);

                break;
            }
        }
    }

    private void ProcessFile(string file, ref List<string> playFiles)
    {
        string extension = Path.GetExtension(file);

        switch (extension.ToLower())
        {
            case ".gplg":
            {
                string pluginsLocation = Utils.GetPath("Plugins");
                string pluginName = Path.GetFileNameWithoutExtension(file);
                string outDir = Path.Combine(pluginsLocation, pluginName);
                Logger.Log($"Copying plugin '{pluginName}' to {outDir}.");
                if (Directory.Exists(outDir))
                    Directory.Delete(outDir, true);
                ZipFile.ExtractToDirectory(file, outDir);
                try
                {
                    if (TryLoadPlugin(Path.Combine(outDir, "Plugin.json"), out string id))
                    {
                        MainWindow.AddPopup(new MessageBoxPopup(MessageBoxPopup.Buttons.Ok, "Plugin Installed",
                            $"Plugin \"{id}\" installed! Go to the settings to enable it."));
                    }
                    else
                        MainWindow.AddPopup(new MessageBoxPopup(MessageBoxPopup.Buttons.Ok, "Plugin Updated",
                            $"Plugin \"{id}\" was updated but Glimpse cannot currently reload plugins.\nRestart Glimpse to load the new plugin."));
                }
                catch (Exception e)
                {
#if DEBUG
                    throw;
#else
                    MainWindow.AddPopup(new MessageBoxPopup(MessageBoxPopup.Buttons.Ok, "Install Failed",
                        $"Failed to install plugin:\n{e}"));
#endif
                }

                break;
            }

            case ".txt":
            case ".m3u":
            case ".m3u8":
            {
                string text = File.ReadAllText(file);
                string[] splitLines = text.Split('\n');

                foreach (string path in splitLines)
                {
                    string trimmedPath = path.Trim();
                    if (string.IsNullOrWhiteSpace(trimmedPath))
                        continue;

                    playFiles.Add(path);
                }

                break;
            }

            default:
            {
                // if the dropped file is a supported audio file, play it
                if (Player.TryGetTrackInfoForFile(file, out _)) // todo expose FileIsSupported method !!!
                    playFiles = [file];
                else
                {
                    Logger.Log($"Attempted to do something with dropped file '{file}', but it was not a supported file.");
                    MainWindow.AddPopup(new MessageBoxPopup(MessageBoxPopup.Buttons.Ok, "Can't Play", "Can't play that file."));
                }

                break;
            }
        }
    }

    private bool TryLoadPlugin(string pluginJsonFile, out string id)
    {
        string json = File.ReadAllText(pluginJsonFile);
        JsonNode pluginJson = JsonNode.Parse(json)!;

        id = pluginJson["ID"].ToString();
        string name = pluginJson["Name"].ToString();
        string author = pluginJson["Author"].ToString();
        string? description = pluginJson["Description"]?.ToString();
        string entryPoint = pluginJson["EntryPoint"].ToString();
        JsonArray? dependencies = pluginJson["Dependencies"]?.AsArray();

        if (Plugins.ContainsKey(id))
            return false;
        
        Logger.Log($"Loading plugin \"{name}\" ({id}) at {entryPoint}");

        string currentFileDir = Path.GetDirectoryName(pluginJsonFile);
        string entryPointPath = Path.Combine(currentFileDir, entryPoint);
        Assembly assembly = _pluginsContext.LoadFromAssemblyPath(entryPointPath);

        if (dependencies != null)
        {
            foreach (string dep in dependencies)
            {
                string depPath = Path.Combine(currentFileDir, dep);
                _pluginsContext.LoadFromAssemblyPath(depPath);
            }
        }
        
        Type pluginType = assembly.GetTypes().First(type => type.IsAssignableTo(typeof(IPlugin)));
        IPlugin plugin = (IPlugin) Activator.CreateInstance(pluginType)!; // can ignore the nullable as plugins should not be nullable
        Plugins.Add(id, new Plugin(id, name, author, description, plugin));
        
        if (Config.Plugins.EnabledPlugins.Contains(id))
            plugin.Initialize(this);

        return true;
    }

    private void OnPipeServerConnection(IAsyncResult asyncResult)
    {
        try
        {
            _pipeServer.EndWaitForConnection(asyncResult);
        }
        catch (SocketException)
        {
            return;
        }
        catch (IOException)
        {
            return;
        }

        _shouldFocusWindow = true;
        // Only copy to the global _playFiles when ready, as this may be called on a separate thread.
        // Helps prevent race conditions.
        List<string> playFiles = [];

        BinaryReader reader = new BinaryReader(_pipeServer);
        
        int b;
        while ((b = _pipeServer.ReadByte()) != -1)
        {
            Program.CommunicationFlags flags = (Program.CommunicationFlags) b;

            if ((flags & Program.CommunicationFlags.PlayFile) != 0)
                ProcessFile(reader.ReadString(), ref playFiles);
        }

        _playFiles = playFiles;

        _pipeServer.Disconnect();
        _pipeServer.BeginWaitForConnection(OnPipeServerConnection, this);
    }

    SemVer IGlimpse.Version => Version;
    ILogger IGlimpse.Logger => Logger;
    IConfigManager IGlimpse.ConfigManager => ConfigManager;
    IAudioPlayer IGlimpse.Player => Player;
    IMusicLibrary IGlimpse.Library => Library;
    ILocale IGlimpse.Locale => Locale;
    
    public void AddButton(string name, string icon, string? tooltip, Action onClick)
    {
        GlimpsePlayer player = (GlimpsePlayer) MainWindow;
        Image image = player.Renderer.CreateImage(icon);
        ((GlimpsePlayer) MainWindow).AddCustomButton(new GlimpsePlayer.CustomButton(name, image, tooltip, onClick));
    }
}