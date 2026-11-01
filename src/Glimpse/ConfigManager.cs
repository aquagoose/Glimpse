using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Glimpse.API;
using Glimpse.Configs;
using Glimpse.Library;

namespace Glimpse;

public class ConfigManager : IConfigManager
{
    private readonly Logger _logger;
    
    public ConfigManager(Logger logger)
    {
        _logger = logger;
    }

    public bool TryGetConfig<T>(string name, out T config) where T : IConfig
    {
        string fullPath = Path.Combine(IConfigManager.BaseDir, $"{name}.json");
        _logger.Log($"Trying to load config {fullPath}.");

        if (!File.Exists(fullPath))
        {
            _logger.Log("    ... failed.");
            config = default;
            return false;
        }

        string json = File.ReadAllText(fullPath);
        
        config = JsonSerializer.Deserialize<T>(json, GetDefaultSerializerOptions());
        
        _logger.Log("    ... loaded.");

        return config != null;
    }

    public void WriteConfig<T>(string name, T config) where T : IConfig
    {
        string fullPath = Path.Combine(IConfigManager.BaseDir, $"{name}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllText(fullPath, JsonSerializer.Serialize(config, GetDefaultSerializerOptions()));
    }

    public static JsonSerializerOptions GetDefaultSerializerOptions()
    {
        JsonSerializerOptions options = new JsonSerializerOptions()
        {
            IncludeFields = true,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        
        if (!JsonSerializer.IsReflectionEnabledByDefault)
            options.TypeInfoResolver = ConfigSerializerContext.Default;

        return options;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, IncludeFields = true, ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(GlimpseConfig))]
[JsonSerializable(typeof(Locale))]
[JsonSerializable(typeof(Locale.AvailableLocale))]
[JsonSerializable(typeof(Locale.LocaleSet))]
[JsonSerializable(typeof(Library.Library))]
[JsonSerializable(typeof(Theme))]
internal partial class ConfigSerializerContext : JsonSerializerContext;