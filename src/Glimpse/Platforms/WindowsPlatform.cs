using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media;
using Windows.Storage.Streams;
using Glimpse.API;

namespace Glimpse.Platforms;

public unsafe class WindowsPlatform : Platform
{
    //private ISystemMediaTransportControls* _transportControls;
    private SystemMediaTransportControls _transportControls;

    private InMemoryRandomAccessStream? _albumArtStream;

    public override string FileManagerName => "Explorer";

    public override void InitializeMainWindow(IntPtr hwnd)
    {
        _transportControls = SystemMediaTransportControlsInterop.GetForWindow(hwnd); 
        _transportControls.ButtonPressed += MediaButtonPressed;
        
        _transportControls.IsEnabled = true;
        _transportControls.IsPlayEnabled = true;
        _transportControls.IsPauseEnabled = true;
        _transportControls.IsNextEnabled = true;
        _transportControls.IsPreviousEnabled = true;
        _transportControls.IsStopEnabled = true;
    }

    private void MediaButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        TransportButton button = args.Button switch
        {
            SystemMediaTransportControlsButton.Play => TransportButton.Play,
            SystemMediaTransportControlsButton.Pause => TransportButton.Pause,
            SystemMediaTransportControlsButton.Stop => TransportButton.Stop,
            SystemMediaTransportControlsButton.Record => throw new NotSupportedException(),
            SystemMediaTransportControlsButton.FastForward => throw new NotSupportedException(),
            SystemMediaTransportControlsButton.Rewind => throw new NotSupportedException(),
            SystemMediaTransportControlsButton.Next => TransportButton.Next,
            SystemMediaTransportControlsButton.Previous => TransportButton.Previous,
            SystemMediaTransportControlsButton.ChannelUp => throw new NotSupportedException(),
            SystemMediaTransportControlsButton.ChannelDown => throw new NotSupportedException(),
            _ => throw new ArgumentOutOfRangeException()
        };

        InvokeButtonPressed(button, null);
    }

    public override void OpenFileInExplorer(string path)
    {
        fixed (char* pPath = path)
        {
            nint list = ILCreateFromPathW(pPath);
            SHOpenFolderAndSelectItems(list, 0, 0, 0);
            ILFree(list);
        }
    }

    public override void SetPlayState(TrackState state, TrackInfo? info, TimeSpan position)
    {
        switch (state)
        {
            case TrackState.Stopped:
                _transportControls.PlaybackStatus = MediaPlaybackStatus.Closed;
                break;
            case TrackState.Paused:
                _transportControls.PlaybackStatus = MediaPlaybackStatus.Paused;
                break;
            case TrackState.Playing:
                _transportControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
        
        if (info == null)
            _transportControls.DisplayUpdater.ClearAll();
        else
        {
            _albumArtStream?.Dispose();
            _transportControls.DisplayUpdater.Thumbnail = null;
            
            if (info.AlbumArt != null)
            {
                _albumArtStream = new InMemoryRandomAccessStream();
                _albumArtStream.WriteAsync(info.AlbumArt.Data.AsBuffer()).GetAwaiter().GetResult();
                _transportControls.DisplayUpdater.Thumbnail =
                    RandomAccessStreamReference.CreateFromStream(_albumArtStream);
            }

            _transportControls.DisplayUpdater.AppMediaId = "Glimpse.Glimpse";
            _transportControls.DisplayUpdater.Type = MediaPlaybackType.Music;
            _transportControls.DisplayUpdater.MusicProperties.TrackNumber = info.TrackNumber ?? 0;
            _transportControls.DisplayUpdater.MusicProperties.Title = info.Title;
            _transportControls.DisplayUpdater.MusicProperties.Artist = info.Artist;
            _transportControls.DisplayUpdater.MusicProperties.AlbumTitle = info.Album;
        }
        
        _transportControls.DisplayUpdater.Update();
    }

    public override void Dispose() { }

    [DllImport("shell32")]
    private static extern nint ILCreateFromPathW(char* path);

    [DllImport("shell32")]
    private static extern int SHOpenFolderAndSelectItems(nint pidlFolder, uint cidl, nint apidl, uint dwFlags);

    [DllImport("shell32")]
    private static extern void ILFree(nint list);
}