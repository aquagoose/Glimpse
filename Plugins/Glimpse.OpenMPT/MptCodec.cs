using Glimpse.API;
using Glimpse.API.Codecs;
using OpenMPT.NET;

namespace Glimpse.OpenMPT;

public class MptCodec : ICodec
{
    public MptConfig Config;

    public MptCodec(MptConfig config)
    {
        Config = config;
    }
    
    public bool FileIsSupported(string path, string extension)
    {
        try
        {
            // todo does openmpt have some built in way of detecting if a track is supported? it must do....
            // try n load the module. if it fails, it's not supported!
            Module module = Module.FromMemory(File.ReadAllBytes(path));
            module.Dispose();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public TrackInfo GetTrackInfo(string path)
    {
        using MptStream stream = new MptStream(path, Config);
        return stream.TrackInfo;
    }

    public ICodecStream CreateStream(string path)
    {
        return new MptStream(path, Config);
    }
}