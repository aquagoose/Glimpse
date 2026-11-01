using System.Numerics;
using System.Timers;
using Glimpse.API;
using MixrSharp;
using piko.SDL3;
using AudioFormat = Glimpse.API.Codecs.AudioFormat;
using DataType = Glimpse.API.Codecs.DataType;
using Timer = System.Timers.Timer;

namespace Glimpse.Audio;

public sealed unsafe class AudioDevice : IDisposable
{
    private readonly Context _context;
    private readonly uint _sampleRate;
    private readonly List<IAudioPlayer.AudioEffect> _effects;
    private readonly SDL.AudioStreamCallback _callback;

    private SDL.AudioStream _device;
    private readonly Timer _stopTimer;

    public float MasterVolume;

    public AudioDevice(Context context, uint sampleRate, List<IAudioPlayer.AudioEffect> effects)
    {
        _context = context;
        _sampleRate = sampleRate;
        _effects = effects;
        MasterVolume = 1.0f;
        _callback = AudioCallback;
        
        if (!SDL.Init(SDL.InitFlags.Audio))
            throw new Exception($"Failed to initialize SDL: {SDL.GetError()}");

        // stops the audio device after 10 seconds of inactivity when running
        _stopTimer = new Timer(10000)
        {
            AutoReset = false,
            Enabled = false
        };
        _stopTimer.Elapsed += StopDeviceCallback;
    }

    /// <summary>
    /// Start the device playback. You must call this before playback will begin!
    /// </summary>
    public void Play()
    {
        // cancel the stop timer if it's running
        _stopTimer.Stop();

        // don't create a new device if one already exists.
        if (!_device.IsNull)
            return;

        SDL.AudioSpec spec = new()
        {
            Freq = (int) _sampleRate,
            Format = SDL.AudioFormat.F32le,
            Channels = 2
        };

        _device = SDL.OpenAudioDeviceStream(SDL.AudioDeviceDefaultPlayback, &spec, _callback, 0);
        if (_device.IsNull)
            throw new Exception($"Failed to open audio device: {SDL.GetError()}");

        SDL.ResumeAudioStreamDevice(_device);
    }

    /// <summary>
    /// Stop the device playback. You should call this once the device is no longer needed, for example when stopped,
    /// or after a long enough pause.
    /// </summary>
    public void Pause()
    {
        // start the stop timer that stops the startings
        _stopTimer.Start();
    }

    private void StopDeviceCallback(object? sender, ElapsedEventArgs elapsedEventArgs)
    {
        Console.WriteLine("Stop!");
        // don't try to destroy a nonexistent device. in theory this is unreachable
        if (_device.IsNull)
            return;

        SDL.PauseAudioStreamDevice(_device);
        SDL.DestroyAudioStream(_device);
        _device = new SDL.AudioStream(); // todo piko: IHandle.Null static?
    }
    
    public void Dispose()
    {
        _stopTimer.Stop();
        _stopTimer.Elapsed -= StopDeviceCallback;
        SDL.DestroyAudioStream(_device);
        SDL.QuitSubSystem(SDL.InitFlags.Audio);
    }
    
    private void AudioCallback(nint userData, SDL.AudioStream stream, int additionalAmount, int totalAmount)
    {
        const int bufferSize = 512;
        float* buffer = stackalloc float[bufferSize];
        AudioFormat format = new AudioFormat(DataType.Float, _sampleRate, 2);
        
        while (additionalAmount > 0)
        {
            int total = int.Min(additionalAmount, bufferSize);
            Span<float> bufferSlice = new Span<float>(buffer, total / 4);
            // bypass effect processing if there are no effects, as mixr handles the limiting + volume control
            // faster than the C# code can, so avoid if possible.
            if (_effects.Count == 0)
            {
                _context.MasterVolume = MasterVolume;
                _context.MixToStereoF32Buffer(bufferSlice);
            }
            else
            {
                // when processing effects, we bypass mixr's volume control and limiting.
                _context.MasterVolume = 1.0f;
                _context.MixToStereoF32Buffer(bufferSlice);
                
                // apply the effects to the output buffer...
                foreach (IAudioPlayer.AudioEffect effect in _effects)
                    effect(bufferSlice, in format);

                // apply global volume control + limiting
                // we apply the volume control AFTER the effects to ensure that regardless of the effect volume,
                // our output volume never surpasses the desired amount.
                // we use SIMD to try and speed this process up a bit, although i'm not sure how the speed compares
                // to just letting mixr do it itself
                // this is just a workaround until mixr has effect processing built-in, then all effect methods
                // will be piped through it
                int i = 0;
                int vectorStart = bufferSlice.Length - Vector<float>.Count;
                for (; i <= vectorStart; i += Vector<float>.Count)
                {
                    Vector<float> floats = Vector.Create(bufferSlice[i..]);
                    floats = Vector.Clamp(floats * MasterVolume, new Vector<float>(-1.0f), new Vector<float>(1.0f));
                    floats.CopyTo(bufferSlice[i..]);
                }

                for (; i < bufferSlice.Length; i++)
                    bufferSlice[i] = float.Clamp(bufferSlice[i] * MasterVolume, -1.0f, 1.0f);
            }

            SDL.PutAudioStreamData(stream, (nint) buffer, total);
            additionalAmount -= total;
        }
    }
}