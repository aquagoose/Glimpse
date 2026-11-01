using System.Diagnostics;
using Glimpse.Audio;
using Glimpse.Library;
using Hexa.NET.ImGui;
using Track = Glimpse.API.Library.Track;

namespace Glimpse.Forms;

public sealed class RemoveTrackPopup : Popup
{
    private readonly string _path;
    private readonly bool _deleteFromFileSystem;

    private Track _track = null!;
    
    public RemoveTrackPopup(string path, bool deleteFromFileSystem)
    {
        _path = path;
        _deleteFromFileSystem = deleteFromFileSystem;
    }

    public override void Open()
    {
        bool success = Glimpse.Library.TryGetTrack(_path, out _track!);
        Debug.Assert(success);
    }

    protected override void Update(float dt)
    {
        Locale locale = Glimpse.Locale;

        if (ImGui.OpenPopupModal(locale.GetString("Popup.Remove.Name")))
        {
            MusicLibrary library = Glimpse.Library;
            AudioPlayer player = Glimpse.Player;

            if (_deleteFromFileSystem)
            {
                ImGui.TextUnformatted(locale.GetString("Popup.Remove.DeleteTrackWarning", _track.Title));
            }
            else
                ImGui.TextUnformatted(locale.GetString("Popup.Remove.Confirmation", _track.Title));

            if (ImGui.Button(locale.GetString("Button.Yes")))
            {
                if (_track.Path == player.CurrentTrackPath)
                    player.Stop();

                if (!library.TryDeleteTrack(_track.Path))
                    Glimpse.Logger.Log($"ERROR: Could not delete track \"{_track}\" from the database!");
                
                if (_deleteFromFileSystem)
                    File.Delete(_track.Path);
                
                //db.Refresh();
                Close();
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button(locale.GetString("Button.No")))
                Close();
            
            ImGui.EndPopup();
        }
    }
}