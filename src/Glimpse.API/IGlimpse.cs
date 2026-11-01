using Glimpse.API.Library;

namespace Glimpse.API;

public interface IGlimpse
{
    public SemVer Version { get; }
    
    public ILogger Logger { get; }
    
    public IConfigManager ConfigManager { get; }
    
    public IAudioPlayer Player { get; }
    
    public IMusicLibrary Library { get; }
    
    public ILocale Locale { get; }

    public void AddButton(string id, string icon, string? tooltip, Action onClick);

    public void RemoveButton(string id);
    
    public void AddContextMenuItem(string id, ContextMenuType type, string text, string? tooltip, Action onClick);

    public void RemoveContextMenuItem(string id);
}