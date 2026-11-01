using System.Diagnostics.CodeAnalysis;

namespace Glimpse.API.Library;

public interface IMusicLibrary
{
    /// <summary>
    /// The name of the favorites playlist. This will not be returned by <see cref="GetPlaylists"/>.
    /// </summary>
    public const string FavoritesPlaylistName = "Favorites";

    /// <summary>
    /// This event is invoked whenever the library is updated, for example, when a <see cref="Track"/> is updated or deleted.
    /// </summary>
    public event OnChanged Updated;
    
    public bool IsIndexing { get; }
    
    public string? CurrentlyIndexedFile { get; }
    
    public IReadOnlyCollection<string> LibraryPaths { get; }
    
    public IReadOnlyCollection<string> ExcludedDirectories { get; }
    
    /// <summary>
    /// Gets the <see cref="Track"/>s in the library.
    /// </summary>
    public SizedCollection<Track> GetTracks();

    /// <summary>
    /// Gets the <see cref="Album"/>s in the library.
    /// </summary>
    public SizedCollection<Album> GetAlbums();

    /// <summary>
    /// Gets the <see cref="Artist"/>s in the library.
    /// </summary>
    public SizedCollection<Artist> GetArtists();

    /// <summary>
    /// Gets the <see cref="Genre"/>s in the library.
    /// </summary>
    public SizedCollection<Genre> GetGenres();

    /// <summary>
    /// Gets the <see cref="Playlist"/>s in the library.
    /// </summary>
    /// <remarks>This will <b>NOT</b> return the Favorites playlist, as this is a special playlist.</remarks>
    public SizedCollection<Playlist> GetPlaylists();

    /// <summary>
    /// Gets a full list of directories that will be indexed by the music library.
    /// </summary>
    /// <returns>A list of directories added to the music library.</returns>
    /// <remarks>Contrary to the data provided, internally, Glimpse only keeps track of directories that are
    /// <b>excluded</b> from the library. Therefore, Glimpse must calculate this every time the function is called.
    /// As such this can be a rather slow function to call, so only call it once, then cache the result.</remarks>
    public IReadOnlyCollection<string> GetLibraryPaths();

    /// <summary>
    /// Add a new directory to the library, that will be indexed.
    /// </summary>
    /// <param name="path">The path to the directory.</param>
    /// <param name="includeSubdirectories">If <see langword="true"/>, then all subdirectories will be added. Otherwise,
    /// only the given directory will be added.</param>
    /// <remarks>This does not need to be manually called whenever a new subdirectory is added
    /// (if <paramref name="includeSubdirectories"/> is <see langword="true"/>), as Glimpse will automatically add any
    /// new subdirectories to the library.</remarks>
    public void AddLibraryPath(string path, bool includeSubdirectories = true);

    /// <summary>
    /// Remove a directory in the library from indexing.
    /// </summary>
    /// <param name="path">The path to the directory.</param>
    /// <param name="includeSubdirectories">If <see langword="true"/>, all subdirectories will also be removed.</param>
    public void RemoveLibaryPath(string path, bool includeSubdirectories = true);

    /// <summary>
    /// Removes <b>ALL</b> library paths.
    /// </summary>
    public void RemoveAllLibraryPaths();

    /// <summary>
    /// Re-index the music library.
    /// </summary>
    /// <remarks>This function runs asynchronously.</remarks>
    public void Index();

    /// <summary>
    /// Get a <see cref="Track"/> from the library.
    /// </summary>
    /// <param name="path">The path to the track.</param>
    /// <returns>The <see cref="Track"/>.</returns>
    public bool TryGetTrack(string path, [NotNullWhen(true)] out Track? track);

    public bool TryGetAlbum(string albumName, [NotNullWhen(true)] out Album? album);

    public bool TryGetArtist(string artistName, [NotNullWhen(true)] out Artist? artist);

    public bool TryGetGenre(string genreName, [NotNullWhen(true)] out Genre? genre);

    public bool TryGetPlaylist(string playlistName, [NotNullWhen(true)] out Playlist? playlist);

    public bool TryGetTracksForAlbum(string albumName, out SizedCollection<Track> tracks);

    public bool TryGetTracksForArtist(string artistName, out SizedCollection<Track> tracks);

    public bool TryGetTracksForGenre(string genreName, out SizedCollection<Track> tracks);

    public bool TryGetTracksForPlaylist(string playlistName, out SizedCollection<Track> tracks);
    
    /// <summary>
    /// Update a <see cref="Track"/> in the library.
    /// </summary>
    /// <param name="track">The <see cref="Track"/> to update.</param>
    public bool UpdateTrack(Track track);

    public bool UpdatePlaylist(Playlist playlist);

    public bool InsertOrUpdateTrack(Track track);

    public bool InsertOrUpdateAlbum(Album album);

    public bool InsertOrUpdateArtist(Artist artist);

    public bool InsertOrUpdateGenre(Genre genre);

    public bool TryDeleteTrack(string path);

    public bool TryDeletePlaylist(string name);

    public delegate void OnChanged();
}