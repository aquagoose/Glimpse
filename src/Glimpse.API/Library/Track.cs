namespace Glimpse.API.Library;

public record Track
{
    public string Path;
    
    public uint? TrackNumber;
    
    public string? Title;

    public string? Artist;

    public string? Album;

    public TimeSpan? Length;

    public string? Genre;

    public string? AlbumArtist;

    public uint? TotalTracks;

    public uint? DiscNumber;

    public uint? TotalDiscs;

    public byte Rating;

    public uint PlayCount;

    public DateTime? LastPlayed;

    public Track() { }

    public Track(string path, TrackInfo? info, Track? oldTrack = null)
    {
        Path = path;
        TrackNumber = info?.TrackNumber;
        Title = info?.Title;
        Artist = info?.Artist;
        Album = info?.Album;
        Length = info?.Length;
        Genre = info?.Genre;
        AlbumArtist = info?.AlbumArtist;
        TotalTracks = info?.TotalTracks;
        DiscNumber = info?.DiscNumber;
        TotalDiscs = info?.TotalDiscs;
        Rating = oldTrack?.Rating ?? 0;
        PlayCount = oldTrack?.PlayCount ?? 0;
        LastPlayed = oldTrack?.LastPlayed ?? null;
    }
}