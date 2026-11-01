using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Glimpse.API;
using Glimpse.API.Codecs;
using OpenMPT.NET;

namespace Glimpse.OpenMPT;

public class MptStream : ICodecStream
{
    private const uint SampleRate = 44100;
    
    private readonly Module _module;

    private int _position;

    public TrackInfo TrackInfo { get; }

    public AudioFormat Format =>
        new AudioFormat(DataType.Float, SampleRate, 2);

    public ulong LengthInSamples => (ulong) (_module.DurationInSeconds * SampleRate);

    public MptStream(string path, MptConfig config)
    {
        _module = Module.FromMemory(File.ReadAllBytes(path));
        _module.Config.EmulateAmigaResampler = config.EmulateAmigaResampler;
        _module.Config.EndBehavior = config.FadeOutAtEnd ? EndBehavior.FadeOut : EndBehavior.Stop;
        _module.RenderParams.InterpolationFilter = config.ResamplerFilter;

        ModuleMetadata metadata = _module.Metadata;
        TrackInfo = new TrackInfo(null, metadata.Title ?? Path.GetFileNameWithoutExtension(path), metadata.Artist, null,
            TimeSpan.FromSeconds(_module.DurationInSeconds), null, null, null);
    }
    
    public ulong GetBuffer(Span<byte> buffer)
    {
        Span<float> floatBuffer = MemoryMarshal.Cast<byte, float>(buffer);
        ulong samples = _module.ReadInterleavedStereo(SampleRate, floatBuffer);
        return samples * 4 * 2; // Convert samples to bytes. samples * sizeof(float) * 2 (channels)
    }

    public void Seek(ulong sample)
    {
        _module.Seek(sample / (double) SampleRate);
    }

    public void Dispose()
    {
        _module.Dispose();
    }
}