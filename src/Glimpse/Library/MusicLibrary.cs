using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Glimpse.API;
using Glimpse.API.Library;
using Glimpse.Audio;
using Track = Glimpse.API.Library.Track;

namespace Glimpse.Library;

public class MusicLibrary : IMusicLibrary
{
    private const string PlaylistFileExtension = ".m3u";

    public const string DatabaseName = "Library";
    public const uint DatabaseVersion = 1;

    private readonly Glimpse _glimpse;
    private readonly AudioPlayer _player;
    private readonly string _databasePath;
    private readonly string _playlistsBasePath;
    
    private Task? _indexTask;
    private string? _currentlyIndexedPath;

    /// <summary>
    /// A list of directories that have been explicitly added to the library.
    /// </summary>
    private readonly HashSet<string> _libraryPaths;
    
    /// <summary>
    /// A list of directories, within the Library Paths, that have been removed from the library.
    /// This is done so that any new subdirectories within the Library Paths will automatically be added, without
    /// Glimpse (or the user) needing to do anything.
    /// </summary>
    private readonly HashSet<string> _excludedDirectories;

    private ConcurrentDictionary<string, Track> _tracks;
    private ConcurrentDictionary<string, Album> _albums;
    private ConcurrentDictionary<string, Artist> _artists;
    private ConcurrentDictionary<string, Genre> _genres;

    public event IMusicLibrary.OnChanged Updated;
    
    public bool IsIndexing => !_indexTask?.IsCompleted ?? false;

    public string? CurrentlyIndexedFile => _currentlyIndexedPath;

    public IReadOnlyCollection<string> LibraryPaths => _libraryPaths;

    public IReadOnlyCollection<string> ExcludedDirectories => _excludedDirectories;

    public MusicLibrary(Glimpse glimpse, AudioPlayer player)
    {
        _glimpse = glimpse;
        _player = player;
        _databasePath = Path.Combine(IConfigManager.BaseDir, $"{DatabaseName}.json");
        _playlistsBasePath = Path.Combine(IConfigManager.BaseDir, "Playlists");

        _libraryPaths = [];
        _excludedDirectories = [];
        
        _tracks = [];
        _albums = [];
        _artists = [];
        _genres = [];
        
        Updated = delegate { };

        // initialize the favourites playlist and playlist directory if they don't exist.
        if (!File.Exists(GetPlaylistPath(IMusicLibrary.FavoritesPlaylistName)))
        {
            Directory.CreateDirectory(_playlistsBasePath);
            File.Create(GetPlaylistPath(IMusicLibrary.FavoritesPlaylistName)).Dispose();
        }

        // Handle first-time usage.
        if (!File.Exists(_databasePath))
        {
            SaveLibrary(false);
            return;
        }

        // TODO: Handle null
        Library library = JsonSerializer.Deserialize<Library>(File.ReadAllText(_databasePath), ConfigManager.GetDefaultSerializerOptions());

        _libraryPaths = library.LibraryPaths.ToHashSet();
        _excludedDirectories = library.ExcludedDirectories.ToHashSet();
        
        foreach (Track track in library.Tracks)
            _tracks.TryAdd(track.Path, track);
        
        foreach (Album album in library.Albums)
            _albums.TryAdd(album.Name, album);
        
        foreach (Artist artist in library.Artists)
            _artists.TryAdd(artist.Name, artist);

        foreach (Genre genre in library.Genres)
            _genres.TryAdd(genre.Name, genre);
        
        if (_glimpse.Config.Library.RefreshLibraryOnStartup)
            Index();
    }
    
    public SizedCollection<Track> GetTracks()
    {
        var tracks = _tracks.Values;
        IEnumerable<Track> trackEnumerable = tracks.OrderBy(track => track.Album).ThenBy(track => track.TrackNumber);

        return new SizedCollection<Track>(trackEnumerable, (uint) tracks.Count);
    }
    
    public SizedCollection<Album> GetAlbums()
    {
        var albums = _albums.Values;
        IEnumerable<Album> albumEnumerable = albums.OrderBy(album => album.Name);

        return new SizedCollection<Album>(albumEnumerable, (uint) albums.Count);
    }
    
    public SizedCollection<Artist> GetArtists()
    {
        var artists = _artists.Values;
        IEnumerable<Artist> artistEnumerable = artists.OrderBy(artist => artist.Name);

        return new SizedCollection<Artist>(artistEnumerable, (uint) artists.Count);
    }
    
    public SizedCollection<Genre> GetGenres()
    {
        var genres = _genres.Values;
        IEnumerable<Genre> genreEnumerable = genres.OrderBy(genre => genre.Name);

        return new SizedCollection<Genre>(genreEnumerable, (uint) genres.Count);
    }

    public SizedCollection<Playlist> GetPlaylists()
    {
        _glimpse.Logger.Log("Refreshing playlists.");
        string[] playlistFiles = Directory.GetFiles(_playlistsBasePath, $"*{PlaylistFileExtension}");

        List<Playlist> playlists = [];
        uint numPlaylists = 0;

        foreach (string file in playlistFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);

            if (fileName == IMusicLibrary.FavoritesPlaylistName)
                continue;

            // todo: maybe should have a debug assertion here
            if (!TryGetPlaylist(fileName, out Playlist? playlist))
                continue;

            playlists.Add(playlist);
            numPlaylists++;
        }

        return new SizedCollection<Playlist>(playlists, numPlaylists);
    }

    // TODO: Rename this method to EnumerateLibraryPaths() ?
    public IReadOnlyCollection<string> GetLibraryPaths()
    {
        List<string> paths = [];
        foreach (string libraryPath in _libraryPaths)
        {
            if (!Directory.Exists(libraryPath))
            {
                _glimpse.Logger.Log($"ERROR: Library path \"{libraryPath}\" does not exist! Skipping...");
                continue;
            }

            paths.Add(libraryPath);
            
            foreach (string directory in Directory.GetDirectories(libraryPath, "*", SearchOption.AllDirectories))
            {
                if (_excludedDirectories.Contains(directory))
                    continue;

                foreach (string excluded in _excludedDirectories)
                {
                    if (directory.StartsWith(excluded))
                        goto SKIP;
                }
                
                paths.Add(directory);
                
                SKIP: ;
            }
        }

        return paths;
    }
    
    public void AddLibraryPath(string path, bool includeSubdirectories = true)
    {
        if (!includeSubdirectories)
            throw new NotImplementedException("Cannot add library path and not include subdirectories yet!");

        if (!_excludedDirectories.Remove(path))
            _libraryPaths.Add(path);
        
        SaveLibrary(false);
    }
    
    public void RemoveLibaryPath(string path, bool includeSubdirectories = true)
    {
        if (!includeSubdirectories)
            throw new NotImplementedException("Cannot remove library path but keep subdirectories yet!");
        
        if (!_libraryPaths.Remove(path))
            _excludedDirectories.Add(path); 
        
        SaveLibrary(false);
    }

    public void RemoveAllLibraryPaths()
    {
        _libraryPaths.Clear();
        _excludedDirectories.Clear();
    }

    public void Index()
    {
        _indexTask = Task.Run(IndexLibrary);
    }
    
    public bool TryGetTrack(string path, [NotNullWhen(true)] out Track? track)
    {
        if (!_tracks.TryGetValue(path, out Track trk))
        {
            track = null;
            return false;
        }

        track = trk;
        return true;
    }
    
    public bool TryGetTracksForAlbum(string albumName, out SizedCollection<Track> tracks)
    {
        if (!_albums.TryGetValue(albumName, out Album album))
        {
            tracks = [];
            return false;
        }

        tracks = GetTracksFromPathList(album.Tracks);
        return true;
    }

    public bool TryGetTracksForArtist(string artistName, out SizedCollection<Track> tracks)
    {
        if (!_artists.TryGetValue(artistName, out Artist artist))
        {
            tracks = [];
            return false;
        }

        tracks = GetTracksFromPathList(artist.Tracks);
        return true;
    }
    
    public bool TryGetTracksForGenre(string genreName, out SizedCollection<Track> tracks)
    {
        if (!_genres.TryGetValue(genreName, out Genre genre))
        {
            tracks = [];
            return false;
        }

        tracks = GetTracksFromPathList(genre.Tracks);
        return true;
    }

    public bool TryGetTracksForPlaylist(string playlistName, out SizedCollection<Track> tracks)
    {
        if (!TryGetPlaylist(playlistName, out Playlist? playlist))
        {
            tracks = [];
            return false;
        }

        // we don't want to order tracks in the playlist.
        // glimpse should show the order in which the songs were added to the playlist.
        // also should not ignore unknown tracks. playlists may contain tracks from outside the library,
        // and they should still be displayed correctly. this does require fetching the track information
        // each time though...
        // todo cache the results of the unknown tracks for faster lookups?
        tracks = GetTracksFromPathList(playlist.Tracks, false, false);
        return true;
    }

    public bool TryGetAlbum(string albumName, [NotNullWhen(true)] out Album? album)
    {
        return _albums.TryGetValue(albumName, out album);
    }

    public bool TryGetArtist(string artistName, [NotNullWhen(true)] out Artist? artist)
    {
        return _artists.TryGetValue(artistName, out artist);
    }

    public bool TryGetGenre(string genreName, [NotNullWhen(true)] out Genre? genre)
    {
        return _genres.TryGetValue(genreName, out genre);
    }

    public bool TryGetPlaylist(string playlistName, [NotNullWhen(true)] out Playlist? playlist)
    {
        string playlistPath = GetPlaylistPath(playlistName);
        if (!File.Exists(playlistPath))
        {
            playlist = null;
            return false;
        }

        HashSet<string> trackPaths = [];
        foreach (string file in File.ReadLines(playlistPath))
        {
            string trimmedFile = file.Trim();
            // ignore gaps in the file
            if (string.IsNullOrWhiteSpace(trimmedFile))
                continue;

            trackPaths.Add(trimmedFile);
        }

        playlist = new Playlist(playlistName, trackPaths);
        return true;
    }

    public bool UpdateTrack(Track track)
    {
        _tracks[track.Path] = track;
        SaveLibrary(true);

        // TODO: Check if track exists in the library.
        return true;
    }

    public bool UpdatePlaylist(Playlist playlist)
    {
        Directory.CreateDirectory(_playlistsBasePath);
        string playlistPath = GetPlaylistPath(playlist.Name);
        using StreamWriter writer = File.CreateText(playlistPath);
        foreach (string track in playlist.Tracks)
            writer.WriteLine(track);

        return true;
    }

    // TODO: These methods MUST call SaveLibrary, but implement Transactions first.
    public bool InsertOrUpdateTrack(Track track)
    {
        bool trackExists = _tracks.ContainsKey(track.Path);
        _tracks[track.Path] = track;
        return !trackExists;
    }
    
    public bool InsertOrUpdateAlbum(Album album)
    {
        bool albumExists = _albums.ContainsKey(album.Name);
        _albums[album.Name] = album;
        return !albumExists;
    }
    
    public bool InsertOrUpdateArtist(Artist artist)
    {
        bool artistExists = _artists.ContainsKey(artist.Name);
        _artists[artist.Name] = artist;
        return !artistExists;
    }
    
    public bool InsertOrUpdateGenre(Genre genre)
    {
        bool genreExists = _genres.ContainsKey(genre.Name);
        _genres[genre.Name] = genre;
        return !genreExists;
    }

    public bool TryDeleteTrack(string path)
    {
        if (!_tracks.TryRemove(path, out Track track))
            return false;

        if (_albums.TryGetValue(track.Album, out Album album))
            album.Tracks.Remove(track.Path);
        if (_artists.TryGetValue(track.Artist, out Artist artist))
            artist.Tracks.Remove(track.Path);
        if (_genres.TryGetValue(track.Path, out Genre genre))
            genre.Tracks.Remove(track.Path);
        
        SaveLibrary(true);
        return true;
    }

    public bool TryDeletePlaylist(string name)
    {
        string playlistPath = GetPlaylistPath(name);
        if (!File.Exists(playlistPath))
            return false;
        File.Delete(playlistPath);
        return true;
    }

    private string GetPlaylistPath(string playlistName)
    {
        return Path.Combine(_playlistsBasePath, $"{playlistName}{PlaylistFileExtension}");
    }

    private SizedCollection<Track> GetTracksFromPathList(IReadOnlyCollection<string> paths, bool order = true, bool ignoreUnknownTracks = true)
    {
        List<Track> trackList = [];
        foreach (string path in paths)
        {
            if (!TryGetTrack(path, out Track? track))
            {
                if (ignoreUnknownTracks)
                    continue;

                _player.TryGetTrackInfoForFile(path, out TrackInfo? info);
                track = new Track(path, info);
            }
            
            trackList.Add(track);
        }

        IEnumerable<Track> trackEnumerable;
        if (order)
            trackEnumerable = trackList.OrderBy(track => track.TrackNumber).ThenBy(track => track.Title);
        else
            trackEnumerable = trackList;
        return new SizedCollection<Track>(trackEnumerable, (uint) trackList.Count);
    }

    private void SaveLibrary(bool emitChanged)
    {
        _glimpse.Logger.Log($"Saving library to {_databasePath}.");
        Library library = new Library(DatabaseVersion, _libraryPaths, _excludedDirectories,
            (IReadOnlyCollection<Track>) _tracks.Values, (IReadOnlyCollection<Album>) _albums.Values,
            (IReadOnlyCollection<Artist>) _artists.Values, (IReadOnlyCollection<Genre>) _genres.Values);
        string json = JsonSerializer.Serialize(library, ConfigManager.GetDefaultSerializerOptions());
        File.WriteAllText(_databasePath, json);

        if (emitChanged)
            Updated();
    }

    private void IndexLibrary()
    {
        // gets all track paths currently in the library.
        // used to track which files no longer exist, by removing tracks from this list that DO still exist.
        // ToHashSet is not a particularly fast operation, but it's not called often, so eh.
        HashSet<string> trackPathsInCurrentLibrary = _tracks.Keys.ToHashSet();
        
        foreach (string libraryPath in _libraryPaths)
        {
            _glimpse.Logger.Log($"Indexing library path: {libraryPath}");

            // TODO: Could probably be made more efficient using recursive-esque pattern?
            foreach (string directory in GetLibraryPaths())
            {
                foreach (string path in Directory.GetFiles(directory))
                {
                    _glimpse.Logger.Log($"  Indexing {path}");
                    _currentlyIndexedPath = path;
                    if (!_player.TryGetTrackInfoForFile(path, out TrackInfo? info))
                    {
                        _glimpse.Logger.Log("    ... failed.");
                        continue;
                    }

                    trackPathsInCurrentLibrary.Remove(path);

                    // the output is valid regardless of whether the track exists or not,
                    // so we can just ignore the return value.
                    _ = TryGetTrack(path, out Track? oldTrack);
                    Track track = new Track(path, info, oldTrack);
                    _tracks[path] = track;

                    string albumName = track.Album ?? string.Empty;
                    if (!_albums.TryGetValue(albumName, out Album? album))
                    {
                        album = new Album(albumName, []);
                        _albums[album.Name] = album;
                    }

                    album.Tracks.Add(track.Path);

                    if (track.Artist != null)
                    {
                        if (!_artists.TryGetValue(track.Artist, out Artist? artist))
                        {
                            artist = new Artist(track.Artist, []);
                            _artists[artist.Name] = artist;
                        }

                        artist.Tracks.Add(track.Path);
                    }

                    if (track.Genre != null)
                    {
                        if (!_genres.TryGetValue(track.Genre, out Genre? genre))
                        {
                            genre = new Genre(track.Genre, []);
                            _genres[genre.Name] = genre;
                        }

                        genre.Tracks.Add(track.Path);
                    }
                    
                    // TODO: Test CPU and disk usage with and without this sleep. I have a strong suspicion this 1ms
                    //       sleep is MUCH gentler on the CPU and disk.
                    Thread.Sleep(1);
                }
            }
        }

        // as the loop removes each track from the hashset as it goes, anything left in here should be tracks that
        // no longer exist on the user's filesystem.
        // if the setting is enabled, we should remove those tracks from the library.
        if (_glimpse.Config.Library.RemoveMissingFilesFromLibrary)
        {
            foreach (string path in trackPathsInCurrentLibrary)
            {
                // try to delete the track. if it fails for some reason, oh well. carry on
                _ = TryDeleteTrack(path);
            }
        }

        _currentlyIndexedPath = null;
        SaveLibrary(false);
        _glimpse.Logger.Log("Indexing complete!");
    }

    public delegate void OnUpdate();
}