namespace Glimpse.API.Library;

public record Playlist
(
    string Name,
    HashSet<string> Tracks
);