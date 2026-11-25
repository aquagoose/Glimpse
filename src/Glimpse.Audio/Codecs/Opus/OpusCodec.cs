using Glimpse.API;
using Glimpse.API.Codecs;

namespace Glimpse.Audio.Codecs.Opus;

public class OpusCodec : ICodec
{
    public bool FileIsSupported(string path, string extension)
    {
        return extension == ".opus";
    }

    public TrackInfo GetTrackInfo(string path)
        => CodecUtils.TrackInfoFromFile(path);
    
    public ICodecStream CreateStream(string path)
    {
        return new OpusStream(path);
    }
}