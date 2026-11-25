using Glimpse.API;
using Glimpse.API.Codecs;

namespace Glimpse.Audio.Codecs.Opus;

public class OpusStream : ICodecStream
{
    private readonly MixrSharp.Stream.Opus _opus;
    
    public TrackInfo TrackInfo { get; }

    public AudioFormat Format => _opus.Format.ToGlimpse();

    public ulong LengthInSamples => _opus.LengthInSamples;

    public OpusStream(string path)
    {
        _opus = new MixrSharp.Stream.Opus(path);
        // todo really? this is how i did it?? i'm fairly sure this means glimpse grabs the info multiple times, at least twice.
        TrackInfo = CodecUtils.TrackInfoFromFile(path);
    }
    
    public ulong GetBuffer(Span<byte> buffer)
    {
        return _opus.GetBuffer(buffer);
    }
    
    public void Seek(ulong sample)
    {
        _opus.SeekToSample(sample);
    }

    public void Dispose()
    {
        _opus.Dispose();
    }
}