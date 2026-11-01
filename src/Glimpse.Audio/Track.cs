using Glimpse.API;
using Glimpse.API.Codecs;
using Glimpse.Audio.Codecs;
using MixrSharp;
using AudioFormat = Glimpse.API.Codecs.AudioFormat;
using DataType = Glimpse.API.Codecs.DataType;

namespace Glimpse.Audio;

public class Track : IDisposable
{
    //public event OnUpdateBuffers UpdateBuffers;
    
    private ICodecStream? _stream;
    private ILogger? _logger;
    
    private MixrSharp.AudioFormat _format;
    private AudioSource? _source;

    private byte[] _audioBuffer;
    private AudioBuffer[]? _buffers;
    private int _currentBuffer;

    private ulong _elapsedBytes;
    private ulong _bytesConsumed;

    private object _lockObj;
    private Action _onFinish;

    public readonly TrackInfo Info;
    
    public readonly int LengthInSeconds;

    public double ElapsedSeconds
    {
        get
        {
            ulong elapsedSamples = _elapsedBytes / _format.BytesPerSample / _format.Channels;
            elapsedSamples += _source.Position;

            // TODO: Make this better.
            return (double) elapsedSamples / _format.SampleRate;
        }
    }

    public double SecondsConsumed
    {
        get
        {
            ulong samplesConsumed = _bytesConsumed / _format.BytesPerSample / _format.Channels;
            samplesConsumed += _source.Position;
            return (double) samplesConsumed / _format.SampleRate;
        }
    }

    public double Speed
    {
        get => _source.Speed;
        set => _source.Speed = value;
    }
    
    public TrackState State
    {
        get
        {
            return _source.State switch
            {
                SourceState.Stopped => TrackState.Stopped,
                SourceState.Paused => TrackState.Paused,
                SourceState.Playing => TrackState.Playing,
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }
    
    internal Track(Context context, ICodecStream stream, TrackInfo info, Action onFinish, ILogger? logger)
    {
        _stream = stream;
        _onFinish = onFinish;
        _logger = logger;
        Info = info;

        _format = stream.Format.ToMixr();
        _logger?.Log($"DataType: {_format.DataType}");
        _logger?.Log($"SampleRate: {_format.SampleRate}");
        _logger?.Log($"Channels: {_format.Channels}");

        LengthInSeconds = (int) (_stream.LengthInSamples / _format.SampleRate);
        _logger?.Log($"LengthInSeconds: {LengthInSeconds}");

        _logger?.Log("Creating source.");
        _source = context.CreateSource(new SourceDescription(SourceType.Pcm, _format));
        
        _audioBuffer = new byte[_format.SampleRate * _format.Channels * _format.BytesPerSample];

        // The source will loop the last buffer if it runs out of buffers. It won't sound nice but at least it will
        // continue to play.
        _source.Looping = true;

        _lockObj = new object();
        
        _logger?.Log("Creating audio buffers.");
        _buffers = new AudioBuffer[2];
        for (int i = 0; i < _buffers.Length; i++)
        {
            ulong amount = _stream.GetBuffer(_audioBuffer);
            if (amount == 0)
            {
                _source.Looping = false;
                break;
            }
            
            _buffers[i] = context.CreateBuffer(new ReadOnlySpan<byte>(_audioBuffer, 0, (int) amount));
            _source.SubmitBuffer(_buffers[i]);
        }
        
        _source.BufferFinished += BufferFinished;
        _source.StateChanged += StateChanged;
    }

    public void Play()
    {
        _logger?.Log("Playing.");
        _source.Play();
    }

    public void Pause()
    {
        _logger?.Log("Pausing.");
        _source.Pause();
    }

    public void Seek(double second)
    {
        _logger?.Log($"Seeking to {second}s.");
        
        SourceState state = _source.State;
        _logger?.Log("  Pausing source.");
        _source.Pause();
        _logger?.Log("  Seeking stream.");
        _stream.Seek((ulong) (second * _format.SampleRate));
        _logger?.Log("  Clearing buffers.");
        _source.ClearBuffers();
        _currentBuffer = 0;
        _logger?.Log("  Updating buffers.");
        for (int i = 0; i < _buffers.Length; i++)
        {
            ulong amount = _stream.GetBuffer(_audioBuffer);
            if (amount == 0)
            {
                _source.Looping = false;
                break;
            }

            _buffers[i].Update(new ReadOnlySpan<byte>(_audioBuffer, 0, (int) amount));
            _source.SubmitBuffer(_buffers[i]);
        }

        if (state == SourceState.Playing)
        {
            _logger?.Log("  Playing.");
            _source.Play();
        }

        _elapsedBytes = (ulong) (second * _format.SampleRate * _format.Channels * _format.BytesPerSample);
    }
    
    private void BufferFinished()
    {
        _elapsedBytes += (ulong) _audioBuffer.Length;
        _bytesConsumed += (ulong) _audioBuffer.Length;
        
        Task.Run(() =>
        {
            //UpdateBuffers();
            lock (_lockObj)
            {
                if (_stream == null || _buffers == null || _source == null)
                    return;
                
                ulong bytesProcessed = _stream.GetBuffer(_audioBuffer);

                if (bytesProcessed == 0)
                {
                    // Disable looping so the source can successfully stop.
                    _source.Looping = false;
                    return;
                }

                if ((int) bytesProcessed < _audioBuffer.Length)
                    _buffers[_currentBuffer].Update(_audioBuffer[..(int) bytesProcessed]);
                else
                    _buffers[_currentBuffer].Update(_audioBuffer);
                _source.SubmitBuffer(_buffers[_currentBuffer]);

                _currentBuffer++;
                if (_currentBuffer >= _buffers.Length)
                    _currentBuffer = 0;
            }
        });
    }
    
    private void StateChanged(SourceState state)
    {
        _logger?.Log($"Source state changed to {state}.");
        if (state == SourceState.Stopped && !_source.Looping)
        {
            _logger?.Log("  ... Calling _onFinish()");
            _onFinish();
        }
    }

    public void Dispose()
    {
        lock (_lockObj)
        {
            // TODO: Now that the bug in glimpsecli causing crashes is fixed, this shouldn't cause issues anymore and
            //   there shouldn't be a need for a load of null checking.
            _logger?.Log($"Dispose {Info.Title}");
            _logger?.Log("Disposing source.");
            _source?.Dispose();
            _source = null;
            _logger?.Log("Disposing buffers.");
            if (_buffers != null)
            {
                foreach (AudioBuffer buffer in _buffers)
                    buffer?.Dispose();
            }
            _buffers = null;
            _logger?.Log("Disposing stream.");
            _stream?.Dispose();
            _stream = null;
        }
    }

    //public delegate void OnUpdateBuffers();
}