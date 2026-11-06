using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Glimpse.API;
using Glimpse.API.Codecs;
using Glimpse.Audio.Codecs.Flac;
using Glimpse.Audio.Codecs.Mp3;
using Glimpse.Audio.Codecs.Vorbis;
using Glimpse.Audio.Codecs.Wav;
using MixrSharp;

namespace Glimpse.Audio;

public class AudioPlayer : IAudioPlayer, IDisposable
{
    public event IAudioPlayer.OnTrackChanged TrackChanged = delegate { };

    public event IAudioPlayer.OnStateChanged StateChanged = delegate { };

    private readonly ILogger? _logger;
    private readonly Context _context;
    private readonly AudioDevice _device;
    
    private Track? _activeTrack;
    private TrackInfo? _currentTrackInfo;
    private TrackState _currentTrackState;

    private int _currentTrackIndex;
    private bool _shouldShuffle;

    private PlayerSettings _settings;
    private readonly List<ICodec> _codecs;
    private readonly List<IAudioPlayer.AudioEffect> _effects;

    public readonly List<string> QueuedTracks;
    public readonly List<int> PlayOrder;
    
    public IReadOnlyList<ICodec> Codecs => _codecs;

    public float Volume
    {
        get => _device.MasterVolume;
        set => _device.MasterVolume = value;
    }

    public double Speed
    {
        get => field;
        set
        {
            double speed = double.Clamp(value, 0, 32);
            field = speed;
            _activeTrack?.Speed = speed;
        }
    }

    public RepeatMode Repeat { get; set; }

    public ShuffleMode Shuffle
    {
        get;
        set
        {
            field = value;

            _shouldShuffle = false;
            
            // dont shuffle if there's nothing to shuffle
            if (PlayOrder.Count == 0)
                return;

            switch (value)
            {
                case ShuffleMode.Off:
                    // reset the current track index to the currently playing index, and then order the list
                    _currentTrackIndex = PlayOrder[_currentTrackIndex];
                    for (int i = 0; i < PlayOrder.Count; i++)
                        PlayOrder[i] = i;
                    break;
                case ShuffleMode.Default:
                {
                    Random random = Random.Shared;
                    // move the current track to the beginning of the queue, as shuffled queues will always start at 0
                    (PlayOrder[0], PlayOrder[_currentTrackIndex]) = (PlayOrder[_currentTrackIndex], PlayOrder[0]);
                    _currentTrackIndex = 0;
                    
                    // shuffle the entire queue except for the first track
                    for (int i = 1; i < PlayOrder.Count; i++)
                    {
                        int newIndex = random.Next(1, PlayOrder.Count);
                        (PlayOrder[newIndex], PlayOrder[i]) = (PlayOrder[i], PlayOrder[newIndex]);
                    }
                    
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }
    }

    public TimeSpan ElapsedTime => TimeSpan.FromSeconds(_activeTrack?.ElapsedSeconds ?? 0);

    public TimeSpan ConsumedTime => TimeSpan.FromSeconds(_activeTrack?.SecondsConsumed ?? 0);

    public TimeSpan TrackLength => TimeSpan.FromSeconds(_activeTrack?.LengthInSeconds ?? 0);

    public TrackInfo? CurrentTrack => _currentTrackInfo;

    public TrackState TrackState => _currentTrackState;

    public int CurrentTrackIndex => _currentTrackIndex;

    public string CurrentTrackPath => QueuedTracks.Count == 0 ? string.Empty : QueuedTracks[PlayOrder[_currentTrackIndex]];

    public AudioPlayer(ILogger? logger, PlayerSettings settings)
    {
        _logger = logger;
        _settings = settings;
        _effects = [];
        
        _logger?.Log("Creating context.");
        _context = new Context(_settings.SampleRate);
        
        _logger?.Log("Creating device.");
        _device = new AudioDevice(_context, _settings.SampleRate, _effects);
        
        Volume = _settings.Volume;
        Speed = settings.SpeedAdjust;

        _logger?.Log("Initializing codecs.");
        _codecs = [new Mp3Codec(), new FlacCodec(), new VorbisCodec(), new WavCodec()];

        QueuedTracks = [];
        PlayOrder = [];
    }

    /// <summary>
    /// Queue a track at the given slot.
    /// </summary>
    /// <param name="path">The path to the track file.</param>
    /// <param name="slot">The <see cref="QueueSlot"/> to insert the track at.</param>
    public void QueueTrack(string path, QueueSlot slot, bool autoPlay = true)
    {
        _logger?.Log($"Queueing track {path}");

        bool isFirstQueue = autoPlay && QueuedTracks.Count == 0;

        switch (slot)
        {
            case QueueSlot.AtEnd:
                int index = QueuedTracks.Count;
                QueuedTracks.Add(path);
                PlayOrder.Add(index);
                break;
            case QueueSlot.NextTrack:
                InsertTrackAtIndex(_currentTrackIndex + 1, path);
                break;
            case QueueSlot.Clear:
                QueuedTracks.Clear();
                QueuedTracks.Add(path);
                PlayOrder.Clear();
                PlayOrder.Add(0); // no tracks so we can just add the 0th track
                isFirstQueue = true;
                _shouldShuffle = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
        }

        Debug.Assert(QueuedTracks.Count == PlayOrder.Count);
        
        if (isFirstQueue)
            Play(0);
    }

    public void QueueTracks(IEnumerable<string> paths, QueueSlot slot)
    {
        if (slot == QueueSlot.Clear)
        {
            QueuedTracks.Clear();
            PlayOrder.Clear();
            slot = QueueSlot.AtEnd;
        }
        
        foreach (string path in paths)
            QueueTrack(path, slot, false);

        // queue is ready for shuffling
        _shouldShuffle = true;
    }

    public void RemoveTrackFromQueue(int queueIndex)
    {
        int trackIndex = PlayOrder[queueIndex];
        QueuedTracks.RemoveAt(trackIndex);
        
        PlayOrder.RemoveAt(queueIndex);
        Span<int> playOrder = CollectionsMarshal.AsSpan(PlayOrder);
        for (int i = 0; i < playOrder.Length; i++)
        {
            if (playOrder[i] >= queueIndex)
                playOrder[i]--;
        }

        if (queueIndex == _currentTrackIndex)
        {
            _currentTrackIndex--;
            NextTrack(true);
        }
        else if (_currentTrackIndex > queueIndex)
            _currentTrackIndex--;
    }

    public bool Play(string path)
    {
        // todo BREAKING CHANGE: QueueTrack should return a boolean value!
        QueueTrack(path, QueueSlot.Clear, true);
        return true;
    }

    public bool Play(int queueIndex)
    {
        Debug.Assert(QueuedTracks.Count == PlayOrder.Count);
        
        _logger?.Log($"Changing to track {queueIndex}.");

        if (queueIndex >= QueuedTracks.Count || queueIndex < 0)
        {
            Stop();
            return false;
        }
        
        _currentTrackIndex = queueIndex;
        
        // only shuffle when changing the track for the first time. this ensures that the correct track is selected
        if (_shouldShuffle)
            Shuffle = Shuffle; // set the shuffle mode to itself, forcing a shuffle. is it stupid? yes. does it work? yes

        string path = QueuedTracks[PlayOrder[_currentTrackIndex]];

        if (!File.Exists(path))
            return false;
        
        //_logger?.Log("  Locking device.");
        //_device.Lock();
        Track? oldTrack = _activeTrack;
        
        _logger?.Log($"  Creating codec stream from file {path}");
        if (!TryCreateStreamFromFile(path, out ICodecStream? stream))
            return false;
        
        TrackInfo info = stream.TrackInfo;

        _logger?.Log("  Creating track.");
        _activeTrack = new Track(_context, stream, info, OnTrackFinish, _logger);
        _activeTrack.Speed = Speed;
        //_activeTrack.UpdateBuffers += () => Stop();

        TrackChanged(info, path);
        Play();
        
        _logger?.Log("  Disposing the old track.");
        oldTrack?.Dispose();
        
        //_logger?.Log("  Unlocking device.");
        //_device.Unlock();

        return true;
    }

    public void Play()
    {
        if (_activeTrack == null)
            return;
        
        _logger?.Log("Start playback.");
        _activeTrack.Play();
        ChangeState(TrackState.Playing);
        _logger?.Log("  Playing device.");
        _device.Play();
    }
    
    public void Pause()
    {
        if (_activeTrack == null)
            return;
        
        _activeTrack.Pause();
        _device.Pause();
        ChangeState(TrackState.Paused);
    }

    public void Stop()
    {
        if (_activeTrack == null)
            return;
        
        _logger?.Log("Stopping.");
        
        _logger?.Log("  Pausing device.");
        _device.Pause();
        _logger?.Log("  Disposing active track.");
        _activeTrack.Dispose();
        _activeTrack = null;
        
        _logger?.Log("  Clearing queued tracks.");
        QueuedTracks.Clear();
        PlayOrder.Clear();
        _currentTrackIndex = 0;
        ChangeState(TrackState.Stopped);
    }

    private void NextTrack(bool ignoreRepeatOne)
    {
        do
        {
            switch (Repeat)
            {
                case RepeatMode.Off:
                    _currentTrackIndex++;
                    break;
                case RepeatMode.RepeatQueue:
                    _currentTrackIndex++;
                    if (_currentTrackIndex >= QueuedTracks.Count)
                        _currentTrackIndex = 0;
                    break;
                case RepeatMode.RepeatOne:
                    if (ignoreRepeatOne)
                        _currentTrackIndex++;
                    
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (_currentTrackIndex >= QueuedTracks.Count)
            {
                Stop();
                return;
            }
        } while (!Play(_currentTrackIndex));
    }

    public void Next()
    {
        NextTrack(true);
    }

    public void Previous()
    {
        do
        {
            _currentTrackIndex--;

            if (_currentTrackIndex < 0)
                _currentTrackIndex = 0;
        } while (!Play(_currentTrackIndex));
    }

    public void Seek(double second)
    {
        _activeTrack.Seek(second);
        ChangeState(TrackState);
    }

    public void RegisterCodec(ICodec codec)
    {
        _codecs.Add(codec);
    }
    
    public void DeregisterCodec(ICodec codec)
    {
        _codecs.Remove(codec);
    }

    public void AddEffect(IAudioPlayer.AudioEffect effect)
    {
        _effects.Add(effect);
    }
    
    public void RemoveEffect(IAudioPlayer.AudioEffect effect)
    {
        _effects.Remove(effect);
    }

    public bool TryGetTrackInfoForFile(string path, out TrackInfo? info)
    {
        _logger?.Log("Checking for codec support.");
        if (!FileIsSupported(path, out ICodec codec))
        {
            info = null;
            return false;
        }

        _logger?.Log("    Getting track info.");
        try
        {
            info = codec.GetTrackInfo(path);
        }
        catch (Exception e)
        {
            _logger?.Log($"    Failed! Exception: ====================================\n{e}");
            info = null;
            return false;
        }

        return true;
    }
    
    private bool FileIsSupported(string path, out ICodec outCodec)
    {
        string extension = Path.GetExtension(path).ToLower();
        foreach (ICodec codec in Codecs)
        {
            if (codec.FileIsSupported(path, extension))
            {
                outCodec = codec;
                return true;
            }
        }

        outCodec = null;
        return false;
    }

    private bool TryCreateStreamFromFile(string path, [NotNullWhen(true)] out ICodecStream? stream)
    {
        _logger?.Log("Checking for codec support.");
        if (FileIsSupported(path, out ICodec codec))
        {
            _logger?.Log("  Creating stream.");
            try
            {
                stream = codec.CreateStream(path);
            }
            catch (Exception e)
            {
                _logger?.Log($"Failed to create codec stream: {e}");
                stream = null;
                return false;
            }

            return true;
        }

        _logger?.Log($"File type {Path.GetExtension(path)} not supported!");
        stream = null;
        return false;
    }

    private void InsertTrackAtIndex(int index, string path)
    {
        _logger?.Log($"Inserting track '{path}' at index {index}.");
        if (index >= QueuedTracks.Count)
        {
            int insertIndex = QueuedTracks.Count;
            QueuedTracks.Add(path);
            PlayOrder.Add(insertIndex);
        }
        else
        {
            QueuedTracks.Insert(index, path);
            for (int i = 0; i < PlayOrder.Count; i++)
            {
                if (PlayOrder[i] >= index)
                    PlayOrder[i]++;
            }
            
            PlayOrder.Insert(index, index);
        }
        
        Debug.Assert(QueuedTracks.Count == PlayOrder.Count);
    }

    private void ChangeState(TrackState state)
    {
        _logger?.Log($"Changing state to {state}.");
        _currentTrackInfo = _activeTrack?.Info;
        _currentTrackState = state;
        StateChanged(state);
    }

    private void OnTrackFinish()
    {
        NextTrack(false);
    }

    public void Dispose()
    {
        _logger?.Log("Disposing track.");
        _activeTrack?.Dispose();
        _logger?.Log("Disposing device.");
        _device.Dispose();
        _logger?.Log("Disposing context.");
        _context.Dispose();
    }
}