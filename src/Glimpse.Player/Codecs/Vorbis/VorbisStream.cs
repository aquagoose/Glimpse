﻿using System;
using MixrSharp;

namespace Glimpse.Player.Codecs.Vorbis;

public class VorbisStream : CodecStream
{
    private readonly MixrSharp.Stream.Vorbis _vorbis;

    public override TrackInfo TrackInfo { get; }
    
    public override AudioFormat Format => _vorbis.Format;

    public override ulong LengthInSamples => _vorbis.LengthInSamples;

    public VorbisStream(string path)
    {
        _vorbis = new MixrSharp.Stream.Vorbis(path);
        TrackInfo = TrackInfo.FromFile(path);
    }
    
    public override ulong GetBuffer(Span<byte> buffer)
        => _vorbis.GetBuffer(buffer);

    public override void Seek(ulong sample)
    {
        _vorbis.SeekToSample(sample);
    }

    public override void Dispose()
    {
        _vorbis.Dispose();
    }
}