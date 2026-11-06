/* {Nz} Track Info to TXT Tool || v.1.0 | 04.09.2026 ||
========================================================

What do?:
Creates text files from played songs that other programs can access. Useful for broadcasting.
Exports wanted files in your Glimpse installation folder (Default: %ProgramFiles%\Glimpse).
Creates: 1. CurrentTrack.txt that shows currently playing track, and if enabled 2. LastSession.txt, that will save all played songs in a single file (Wipes on restart of Glimpse).
Uses. 'Artist - Track' format (oldest first for LastSession.txt)

Capabilities:
- CurrentTrack.txt
- LastSession.txt


TODO:
- Option for session files for each session (i.e. "26-07-2026-session.txt", 26-07-2026-session_2.txt", "27-07-2026-session.txt"...)
- Include track length to session files.
- Option to include album name.
- Option to custom format how the track name is written.
- Include custom name for the session file ("Nizzine's_RaveState3000_DJ_Set_27-07-2026.txt") using formatting "{CustomName}{Date}" in a text field.
- Disclude tracks that have been played less than 10 seconds or <30% of the track length.
- Add option to reverse LastSession.txt order to "Newest first".
-
*/

using System.Text.Json;
using System;
using System.IO;
using Glimpse.API;
using Hexa.NET.ImGui;

namespace NzTrackInfoToTxtTool
{
    public class TrackInfoToTxtTool : IPlugin
    {
        public string Name => "Track Info to TXT Tool";
        public string Description = "Creates text files from played songs\nthat other programs can access.\nUseful for i.e. broadcasting";
        private const string CurrentTrackOutputPath = "CurrentTrack.txt"; // do not add backshlash infront becesu it root on linux n mac
        private const string LastSessionOutputPath = "LastSession.txt";

        private IGlimpse _glimpse;
        private MyPluginConfig _config;
        private MyPluginConfig _savedConfig;
        private IConfigManager _configManager;
        public bool IsInitialized { get; private set; }


        public void Initialize(IGlimpse glimpse)
        {
            _glimpse = glimpse;
            _configManager = glimpse.ConfigManager;

            _config = LoadConfig();
            _savedConfig = _config with { };

            if (!File.Exists(CurrentTrackOutputPath))
                File.Create(CurrentTrackOutputPath).Dispose();

            // This _should_ create the file if it does not exist and empties it on launch:
            File.WriteAllText(LastSessionOutputPath, "");


            _glimpse.Player.StateChanged += OnStateChanged;
            IsInitialized = true;
        }

        public void DisplayGui()
        {
            ImGui.BeginChild("PluginText", ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.AutoResizeY);
            {
                ImGui.SeparatorText(Name);
                ImGui.TextUnformatted(Description);
                ImGui.EndChild();
            }
            ImGui.Separator();
            ImGui.TextUnformatted("Settings:");

            bool multiTrackToggle = _config.MultiTrackToggle;
            if (ImGui.Checkbox("Save Songs From Latest Session To A Separate File", ref multiTrackToggle))
                _config.MultiTrackToggle = multiTrackToggle;
            ImGui.SetItemTooltip("Will create a LastSession.txt that will save all\nthe songs you have played during the time Glimpse is on.\nWill be wiped on restart every time.");

            bool includeAlbum = _config.IncludeAlbum;
            if (ImGui.Checkbox("Include Album Name", ref includeAlbum))
                _config.IncludeAlbum = includeAlbum;
            ImGui.SetItemTooltip("Will include album name.");

            if (ImGui.Button("Save Plugin Settings"))
                SaveConfig();
        }

        private void OnStateChanged(TrackState state)
        {
            if (state != TrackState.Playing)
                return;

            TrackInfo track = _glimpse.Player.CurrentTrack;

            string title = track?.Title ?? "Unknown Title";
            string artist = track?.Artist ?? "Unknown Artist";
            string album = track?.Album ?? "Unknown Album";
            string line = $"{artist} - {title}";
            if (_config.IncludeAlbum)
            {
                line += $" - {album}";
            }

            try
            {
                if (_config.MultiTrackToggle)
                {
                    File.AppendAllText(LastSessionOutputPath, line + "\n");
                }
                // Always write to CurrentTrack.txt
                File.WriteAllText(CurrentTrackOutputPath, line);
            }
            catch (IOException ex)
            {
                _glimpse.Logger.Log($"[TrackInfoToTXT] Failed to write track info: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!IsInitialized)
                return;

            _glimpse.Player.StateChanged -= OnStateChanged;
            IsInitialized = false;
        }

        private MyPluginConfig LoadConfig()
        {
            if (_configManager.TryGetConfig(Name, out MyPluginConfig config))
                return config;

            return new MyPluginConfig();
        }

        public void SaveConfig()
        {
            _configManager.WriteConfig(Name, _config);
            _savedConfig = _config with { };
        }
    }

    public record MyPluginConfig : IConfig
    {
        public bool MultiTrackToggle { get; set; }
        public bool IncludeAlbum { get; set; }
    }
}

// End of human written text.

// See you soon in version 1.1!