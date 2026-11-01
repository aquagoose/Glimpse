using System.Text.Json.Serialization;
using Glimpse.API;

namespace Glimpse.Configs;

[method: JsonConstructor]
public struct GlimpseConfig() : IConfig, IEquatable<GlimpseConfig>
{
    public const string ConfigName = "Config";

    public GeneralConfig General = new();
    
    public AppearanceConfig Appearance = new();

    public LibraryConfig Library = new();

    public AudioConfig Audio = new();

    public PluginsConfig Plugins = new();

    public bool Equals(GlimpseConfig other)
    {
        return General.Equals(other.General) && Appearance.Equals(other.Appearance) && Library.Equals(other.Library) &&
               Audio.Equals(other.Audio) && Plugins.Equals(other.Plugins);
    }

    public override bool Equals(object? obj)
    {
        return obj is GlimpseConfig other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(General, Appearance, Library, Audio, Plugins);
    }

    public static bool operator ==(GlimpseConfig left, GlimpseConfig right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GlimpseConfig left, GlimpseConfig right)
    {
        return !left.Equals(right);
    }
}