using System.Text.Json.Serialization;

namespace Glimpse.Configs;

public struct AppearanceConfig() : IEquatable<AppearanceConfig>
{
    [JsonConverter(typeof(JsonStringEnumConverter<PreferredColorScheme>))]
    public PreferredColorScheme PreferredColorScheme = PreferredColorScheme.SyncToOS;
    
    public string Theme = global::Glimpse.Theme.DefaultTheme;
    
    public bool SwapTransportControls = false;

    public bool ConfineAlbumArtToSquare = true;

    public bool SaveWindowStateOnExit = true;

    public bool Equals(AppearanceConfig other)
    {
        return PreferredColorScheme == other.PreferredColorScheme && Theme == other.Theme &&
               SwapTransportControls == other.SwapTransportControls &&
               ConfineAlbumArtToSquare == other.ConfineAlbumArtToSquare &&
               SaveWindowStateOnExit == other.SaveWindowStateOnExit;
    }

    public override bool Equals(object? obj)
    {
        return obj is AppearanceConfig other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine((int) PreferredColorScheme, Theme, SwapTransportControls, ConfineAlbumArtToSquare,
            SaveWindowStateOnExit);
    }

    public static bool operator ==(AppearanceConfig left, AppearanceConfig right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(AppearanceConfig left, AppearanceConfig right)
    {
        return !left.Equals(right);
    }
}