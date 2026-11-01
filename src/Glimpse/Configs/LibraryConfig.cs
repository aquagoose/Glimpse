namespace Glimpse.Configs;

public struct LibraryConfig() : IEquatable<LibraryConfig>
{
    public bool RefreshLibraryOnStartup = false;

    public bool RemoveMissingFilesFromLibrary = true;


    public bool Equals(LibraryConfig other)
    {
        return RefreshLibraryOnStartup == other.RefreshLibraryOnStartup &&
               RemoveMissingFilesFromLibrary == other.RemoveMissingFilesFromLibrary;
    }

    public override bool Equals(object? obj)
    {
        return obj is LibraryConfig other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(RefreshLibraryOnStartup, RemoveMissingFilesFromLibrary);
    }

    public static bool operator ==(LibraryConfig left, LibraryConfig right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LibraryConfig left, LibraryConfig right)
    {
        return !left.Equals(right);
    }
}