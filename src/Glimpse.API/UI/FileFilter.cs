namespace Glimpse.API.UI;

public struct FileFilter
{
    public string Name;

    public string Pattern;

    public FileFilter(string name, string pattern)
    {
        Name = name;
        Pattern = pattern;
    }
}