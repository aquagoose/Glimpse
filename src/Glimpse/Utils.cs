using piko.SDL3;

namespace Glimpse;

public static class Utils
{
    public static string GetPath(string path)
    {
        return Path.Combine(AppContext.BaseDirectory, path);
    }
    
    public static void OpenLink(string link)
    {
        SDL.OpenURL(link);
    }

    public static string FormatTimespan(TimeSpan timeSpan, bool forceHours = false)
    {
        int hours = (int) timeSpan.TotalHours;
        int minutes = timeSpan.Minutes;
        int seconds = timeSpan.Seconds;

        // Only display the hour if necessary.
        // If the hour does need to be displayed, display the total number of hours, going past 24 if needed.
        if (hours > 0 || forceHours)
            return $"{hours:00}:{minutes:00}:{seconds:00}";
        
        return $"{minutes:00}:{seconds:00}";
    }
}