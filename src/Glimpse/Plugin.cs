using Glimpse.API;

namespace Glimpse;

public class Plugin
{
    public string ID;

    public string Name;

    public string Author;

    public string? Description;

    public IPlugin Instance;

    public Plugin(string id, string name, string author, string? description, IPlugin instance)
    {
        ID = id;
        Name = name;
        Author = author;
        Description = description;
        Instance = instance;
    }
}