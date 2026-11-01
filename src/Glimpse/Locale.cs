using System.Text.Json;
using System.Text.Json.Serialization;
using Glimpse.API;
using Glimpse.Assets;

namespace Glimpse;

public class Locale : ILocale
{
    public string ID { get; }
    
    public string DisplayName { get; }

    public readonly Dictionary<string, string> Strings;
    
    public Locale(string id, string displayName, Dictionary<string, string> strings)
    {
        ID = id;
        DisplayName = displayName;
        Strings = strings;
    }

    public string GetString(string str)
    {
        if ((!Strings.TryGetValue(str, out string? localeString) || string.IsNullOrEmpty(localeString)) && !BackupLocale.Strings.TryGetValue(str, out localeString))
            return str;

        return localeString;
    }

    public string GetString(string str, params object[] format)
    {
        if ((!Strings.TryGetValue(str, out string? localeString) || string.IsNullOrEmpty(localeString)) && !BackupLocale.Strings.TryGetValue(str, out localeString))
            return str;
            
        return string.Format(localeString, format);
    }

    private static readonly Locale BackupLocale;
    
    public static readonly LocaleSet AvailableLocales;

    static Locale()
    {
        using Stream json = Asset.GetAssetStream("Locales.Locales.json");
        AvailableLocales = JsonSerializer.Deserialize<LocaleSet>(json, ConfigManager.GetDefaultSerializerOptions());
        BackupLocale = LoadLocale("en-gb");
    }

    public static Locale LoadLocale(string id)
    {
        using Stream json = Asset.GetAssetStream($"Locales.{AvailableLocales.Locales[id].Path}");
        return JsonSerializer.Deserialize<Locale>(json, ConfigManager.GetDefaultSerializerOptions());
    }

    IReadOnlyDictionary<string, string> ILocale.Strings => Strings;

    public readonly struct AvailableLocale
    {
        public readonly string DisplayName;
        public readonly string Path;

        [JsonConstructor]
        public AvailableLocale(string displayName, string path)
        {
            DisplayName = displayName;
            Path = path;
        }
    }

    public readonly struct LocaleSet
    {
        public readonly string FallbackLocale;
        public readonly Dictionary<string, AvailableLocale> Locales;

        [JsonConstructor]
        public LocaleSet(string fallbackLocale, Dictionary<string, AvailableLocale> locales)
        {
            FallbackLocale = fallbackLocale;
            Locales = locales;
        }
    }
}