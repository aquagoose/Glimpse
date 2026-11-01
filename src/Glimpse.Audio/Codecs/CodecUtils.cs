using Glimpse.API;
using Glimpse.API.Codecs;
using TagLib;
using File = TagLib.File;

namespace Glimpse.Audio.Codecs;

public static class CodecUtils
{
    public static MixrSharp.AudioFormat ToMixr(this AudioFormat format)
    {
        MixrSharp.DataType dataType = format.Type switch
        {
            DataType.Byte => MixrSharp.DataType.U8,
            DataType.SByte => MixrSharp.DataType.I8,
            DataType.Short => MixrSharp.DataType.I16,
            DataType.Int => MixrSharp.DataType.I32,
            DataType.Float => MixrSharp.DataType.F32,
            _ => throw new ArgumentOutOfRangeException()
        };

        return new MixrSharp.AudioFormat(dataType, format.SampleRate, format.Channels);
    }

    public static AudioFormat ToGlimpse(this MixrSharp.AudioFormat format)
    {
        DataType dataType = format.DataType switch
        {
            MixrSharp.DataType.I8 => DataType.SByte,
            MixrSharp.DataType.U8 => DataType.Byte,
            MixrSharp.DataType.I16 => DataType.Short,
            MixrSharp.DataType.I32 => DataType.Int,
            MixrSharp.DataType.F32 => DataType.Float,
            _ => throw new ArgumentOutOfRangeException()
        };

        return new AudioFormat(dataType, format.SampleRate, format.Channels);
    }
    
    public static TrackInfo TrackInfoFromFile(string path)
    {
        using File file = File.Create(path);

        uint trackNumber = file.Tag.Track;
        string title = file.Tag.Title ?? Path.GetFileNameWithoutExtension(path); // Better UX to show filename if song is not recognized. TODO: Put this in TrackInfo itself. Or perhaps handle it from within the UI?
        string? artist = file.Tag.FirstPerformer;
        string? album = file.Tag.Album;
        TimeSpan? length = file.Properties.Duration;
        string? genre = file.Tag.FirstGenre;
        string? albumArtist = file.Tag.FirstAlbumArtist;
        
        TrackInfo.Image albumArt = null;
        if (file.Tag.Pictures is { Length: > 0 })
        {
            IPicture picture = file.Tag.Pictures[0];
            albumArt = new TrackInfo.Image(picture.Data?.Data, picture.Filename, picture.MimeType);
        }

        return new TrackInfo(trackNumber, title, artist, album, length, genre, albumArtist, albumArt);
    }
}