using System.Text;

namespace Glimpse.Audio.Codecs;

public record CueFile
{
    public string? Title;
    public string? AlbumArtist;
    public List<File> Files;

    public CueFile(string? title, string? albumArtist, List<File> files)
    {
        Title = title;
        AlbumArtist = albumArtist;
        Files = files;
    }

    public static CueFile Parse(string path)
    {
        string? title = null;
        string? albumArtist = null;

        List<File> files = [];
        File? currentFile = null;
        Track? currentTrack = null;

        string[] lines = System.IO.File.ReadAllLines(path);
        
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            
            if (string.IsNullOrWhiteSpace(line))
                continue;
            
            IReadOnlyList<string> splitLine = ParseLine(line, out int indent);
            switch (splitLine[0].ToLower())
            {
                case "title":
                {
                    if (currentTrack is Track track)
                        track.Title = splitLine[1];
                    else
                        title = splitLine[1];
                    break;
                }

                case "performer":
                {
                    if (currentTrack is Track track)
                        track.Artist = splitLine[1];
                    else
                        albumArtist = splitLine[1];
                    break;
                }

                case "index":
                {
                    if (currentTrack is not Track track)
                        throw new InvalidDataException("Invalid token.");

                    track.TrackIndex = uint.Parse(splitLine[1]);
                    track.Offset = ParseTime(splitLine[2]);
                    break;
                }

                case "track":
                {
                    if (currentFile is not File file)
                        throw new InvalidDataException("Invalid token.");
                    
                    if (currentTrack is Track track)
                        file.Tracks.Add(track);

                    uint trackNumber = uint.Parse(splitLine[1]);
                    currentTrack = new Track(trackNumber, null, null, 0, TimeSpan.Zero);
                    break;
                }

                case "file":
                {
                    if (currentFile is File file)
                    {
                        if (currentTrack is Track track)
                            file.Tracks.Add(track);
                        files.Add(file);
                    }
                    
                    string filePath = splitLine[1];
                    currentFile = new File(filePath, []);
                    break;
                }
            }
        }
        
        if (currentFile is File f)
        {
            if (currentTrack is Track track)
                f.Tracks.Add(track);
            files.Add(f);
        }

        return new CueFile(title, albumArtist, files);
    }

    public static TimeSpan ParseTime(string time)
    {
        // cue files use CD timecodes, shown in mm:ss:ff.
        // one frame is 1/75th of a second
        // this method implements the formula: ff + (ss * 75) + (mm * 60 * 75)
        // however leaves room for longer times if those are present for some reason.
        
        const uint framesPerSecond = 75;
        uint frames = 0;
        
        string[] splitTime = time.Trim().Split(':');
        for (int i = 0; i < splitTime.Length - 1; i++)
        {
            // for each point greater than the seconds column, we must multiply by 60 to the power of the index
            // (so the mm column will use pow(60, 1))
            uint secsMultiplier = (uint) double.Pow(60, (splitTime.Length - 1 - i) - 1);
            frames += uint.Parse(splitTime[i]) * secsMultiplier * framesPerSecond;
        }

        frames += uint.Parse(splitTime[^1]);

        double seconds = frames / (double) framesPerSecond;
        return TimeSpan.FromSeconds(seconds);
    }

    private static StringBuilder _sb = new StringBuilder();
    
    private static IReadOnlyList<string> ParseLine(string line, out int indent)
    {
        List<string> split = [];
        bool isInWhiteSpace = true;
        bool isInString = false;

        indent = 0;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            switch (c)
            {
                case ' ' when !isInString:
                {
                    if (isInWhiteSpace)
                        indent++;
                    else
                    {
                        split.Add(_sb.ToString());
                        _sb.Clear();
                    }

                    continue;
                }
                
                case '"':
                    isInString = !isInString;
                    continue;
            }

            isInWhiteSpace = false;
            _sb.Append(c);
        }
        
        if (_sb.Length > 0)
            split.Add(_sb.ToString());

        _sb.Clear();

        return split;
    }

    public record File
    {
        public string Path;
        public List<Track> Tracks;

        public File(string path, List<Track> tracks)
        {
            Path = path;
            Tracks = tracks;
        }
    }

    public record Track
    {
        public uint? TrackNumber;
        public string? Title;
        public string? Artist;
        public uint TrackIndex;
        public TimeSpan Offset;

        public Track(uint? trackNumber, string? title, string? artist, uint trackIndex, TimeSpan offset)
        {
            TrackNumber = trackNumber;
            Title = title;
            Artist = artist;
            TrackIndex = trackIndex;
            Offset = offset;
        }
    }
}