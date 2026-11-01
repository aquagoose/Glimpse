namespace Glimpse.API;

public interface IConfigManager
{
    public static string BaseDir
    {
        get
        {
#if DEBUG
            return Path.Combine(AppContext.BaseDirectory, "Config");
#else
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Glimpse");
#endif
        }
    }

    public bool TryGetConfig<T>(string name, out T config) where T : IConfig;
    
    public void WriteConfig<T>(string name, T config) where T : IConfig;
}