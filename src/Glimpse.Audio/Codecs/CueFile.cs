namespace Glimpse.Audio.Codecs;

public class CueFile
{
    public static void Parse(string path)
    {
        string? title = null;
        string? albumArtist = null;
        
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            
            string[] splitLine = line.Trim().Split(' ');
            switch (splitLine[0].ToLower())
            {
                case "title":
                    title = splitLine[1].Trim('"');
                    break;
                case "performer":
                    albumArtist = splitLine[1].Trim('"');
                    break;
            }
        }
    }

    private static TimeSpan ParseTime(string time)
    {
        uint frames = 0;
        
        string[] splitTime = time.Trim().Split(':');
    }

    public readonly struct Track
    {
        public readonly string? Title;
        public readonly string? Artist;
        public readonly uint TrackIndex;
        public readonly TimeSpan Offset;

        public Track(string? title, string? artist, uint trackIndex, TimeSpan offset)
        {
            Title = title;
            Artist = artist;
            TrackIndex = trackIndex;
            Offset = offset;
        }

        public static Track Parse(IEnumerable<string> lines)
        {
            string? title = null;
            string? artist = null;
            uint index = 0;

            foreach (string line in lines)
            {
                string[] splitLine = line.Split(' ');
                switch (splitLine[0].ToLower())
                {
                    case "title":
                        title = splitLine[1].Trim('"');
                        break;
                    case "performer":
                        artist = splitLine[1].Trim('"');
                        break;
                    case "index":
                        index = uint.Parse(splitLine[1]);
                        break;
                }
            }
        }
    }
}