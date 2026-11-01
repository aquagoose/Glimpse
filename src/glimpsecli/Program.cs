using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Glimpse.API;
using Glimpse.Audio;

namespace glimpsecli;

public static class GlimpseCli
{
    private static StringBuilder _sb = new StringBuilder();
    
    public static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return;
        }
        
        List<string> files = new List<string>();
        float? volume = null;
        double? speed = null;
        bool shuffle = false;
        bool repeat = false;
        int startingTrack = 0;

        int argIndex = 0;
        while (ReadArg(args, ref argIndex, out string arg))
        {
            if (arg.StartsWith('-'))
            {
                switch (arg)
                {
                    case "--help" or "-h":
                        PrintHelp();
                        return;
                    
                    case "--volume" or "-v":
                    {
                        if (ReadArg(args, ref argIndex, out arg) && float.TryParse(arg, out float vol))
                        {
                            volume = vol;
                            continue;
                        }
                        
                        Console.WriteError("Volume was not parsable.");
                        return;
                    }
                
                    case "--speed" or "-s":
                    {
                        if (ReadArg(args, ref argIndex, out arg) && double.TryParse(arg, out double spd))
                        {
                            speed = spd;
                            continue;
                        }
                        
                        Console.WriteError("Speed was not parsable.");
                        return;
                    }

                    case "--track" or "-t":
                    {
                        if (ReadArg(args, ref argIndex, out arg) && int.TryParse(arg, out int trackNumber))
                        {
                            startingTrack = trackNumber - 1;
                            continue;
                        }
                        
                        Console.WriteError("Track number was not parsable.");
                        return;
                    }
                    
                    case "--shuffle":
                        shuffle = true;
                        break;
                    
                    case "--repeat":
                        repeat = true;
                        break;
                    
                    default:
                        Console.WriteError($"Invalid argument \"{arg}\".");
                        return;
                }
            }
            else
            {
                string fileName = arg.Trim();

                if (File.Exists(fileName))
                {
                    files.Add(fileName);
                }
                else if (Directory.Exists(fileName))
                {
                    foreach (string file in Directory.EnumerateFiles(fileName, "*.*", SearchOption.AllDirectories).Where(s => Path.GetExtension(s) is ".mp3" or ".ogg" or ".wav" or ".flac"))
                        files.Add(file);
                }
                else
                {
                    Console.WriteError($"The file or directory \"{fileName}\" was not found.");
                    return;
                }
            }
        }
        
        if (files.Count == 0)
        {
            Console.WriteError("No file was provided.");
            return;
        }

        if (shuffle)
        {
            Random random = new Random();
            List<string> shuffled = new List<string>(files.Count);

            while (shuffled.Count < files.Count)
            {
                int randomId = random.Next(files.Count);
                string file = files[randomId];
                if (shuffled.Contains(file))
                    continue;
                shuffled.Add(file);
            }

            files = shuffled;
        }

        PlayerSettings settings = new PlayerSettings()
        {
            SampleRate = 48000,
            Volume = volume ?? 1.0f,
            SpeedAdjust = speed ?? 1.0f
        };
        AudioPlayer player = new AudioPlayer(null, settings);
        player.Repeat = repeat ? RepeatMode.RepeatQueue : RepeatMode.Off;
        
        foreach (string path in files)
            player.QueueTrack(path, QueueSlot.AtEnd, false);

        player.Play(startingTrack);
        
        PrintConsoleText(player.CurrentTrack, 0, (int) player.TrackLength.TotalSeconds, player.TrackState,
            player.CurrentTrackIndex, files.Count, true);
        
        Console.CancelKeyPress += (_, _) =>
        {
            ResetConsole();
        };

        // Ensure the console is reset properly if there is any unhandled exception.
        AppDomain.CurrentDomain.UnhandledException += (_, _) =>
        {
            ResetConsole();
        };
        
        Console.CursorVisible = false;

        while (player.TrackState != TrackState.Stopped)
        {
            int elapsed = (int) player.ElapsedTime.TotalSeconds;
            int total = (int) player.TrackLength.TotalSeconds;
            
            PrintConsoleText(player.CurrentTrack, elapsed, total, player.TrackState, player.CurrentTrackIndex, files.Count);

            if (Console.KeyAvailable)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);

                switch (key.Key)
                {
                    case ConsoleKey.P:
                    case ConsoleKey.Spacebar:
                    {
                        if (player.TrackState == TrackState.Playing)
                            player.Pause();
                        else
                            player.Play();

                        break;
                    }
                    
                    case ConsoleKey.Q:
                        player.Stop();
                        break;

                    case ConsoleKey.OemPeriod:
                    case ConsoleKey.RightArrow:
                    {
                        player.Next();
                        break;
                    }

                    case ConsoleKey.OemComma:
                    case ConsoleKey.LeftArrow:
                    {
                        player.Previous();
                        break;
                    }
                    
                    case ConsoleKey.Z: // Volume down
                    case ConsoleKey.DownArrow:
                    {
                        float newVolume = player.Volume - 0.05f;
                        player.Volume = float.Clamp(newVolume, 0, 1);
                        break;
                    }
                    case ConsoleKey.X: // Volume up
                    case ConsoleKey.UpArrow:
                    {
                        float newVolume = player.Volume + 0.05f;
                        player.Volume = float.Clamp(newVolume, 0, 1);
                        break;
                    }
                    case ConsoleKey.C: // Reset volume
                        player.Volume = 1.0f;
                        break;
                }
            }
            
            Thread.Sleep(125);
        }
        
        
        ResetConsole();
        player.Dispose();
    }

    private static void PrintConsoleText(TrackInfo? info, int elapsed, int total, TrackState state, int track, int totalTracks, bool setupConsole = false)
    {
        _sb.Clear();

        string title = info?.Title ?? "Unknown Title";
        string artist = info?.Artist ?? "Unknown Artist";
        string album = info?.Album ?? "Unknown Album";
        
        _sb.AppendLine($"Track:  {track + 1} / {totalTracks}");
        _sb.AppendLine($"Title:  {title}");
        _sb.AppendLine($"Artist: {artist}");
        _sb.AppendLine($"Album:  {album}");

        _sb.AppendLine();
        
        _sb.AppendLine(state.ToString());
        _sb.Append($"{elapsed / 60}:{elapsed % 60:00} [");

        int progress = (int) (((double) elapsed / total) * 51) - 1;
    
        for (int i = 0; i < 50; i++)
        {
            if (i <= progress)
                _sb.Append('=');
            else
                _sb.Append(' ');
        }
    
        _sb.AppendLine($"] {total / 60}:{total % 60:00}\n");

        //_sb.Append("\u2423 Pause  \u2190 Prev  \u2192 Next  Q Quit");
        _sb.Append("\e[7mP\e[0mause  ");
        _sb.Append("\e[7m,\e[0mPrev  ");
        _sb.Append("\e[7m.\e[0mNext  ");
        _sb.Append("\e[7mQ\e[0muit");

        string str = _sb.ToString();
        string[] splitStr = str.Split('\n');

        Console.CursorLeft = 0;
        int startIndex = 0;
        // If the console is being setup, then we should just print the text as there's no previous text to overwrite.
        if (!setupConsole)
        {
            // Ensure we are always overwriting the previous text.
            int y = Console.CursorTop - splitStr.Length;
            if (y < 0)
            {
                // Prevent the cursor from going into the negatives,
                // and ensures the result "looks" correct by cutting off the top of the text that isn't visible.
                startIndex = 0 - y;
                y = 0;
            }

            Console.CursorTop = y;
        }

        for (int i = startIndex; i < splitStr.Length; i++)
        {
            // Print line-by-line, padding any remaining space if there is any, to fully overwrite all old text.
            Console.Write(splitStr[i]);
            int padAmount = Console.BufferWidth - Console.CursorLeft;
            if (padAmount > 0)
                Console.WriteLine(new string(' ', padAmount));
            else
                Console.WriteLine();
        }
    }

    private static void ResetConsole()
    {
        Console.ResetColor();
        Console.CursorVisible = true;
    }

    private static bool ReadArg(string[] args, ref int index, out string arg)
    {
        arg = null;
        
        if (index >= args.Length)
            return false;

        arg = args[index++];
        return true;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
                          glimpsecli

                          Usage: glimpsecli [options] <files/directories>

                          Options:
                              --track <n>, -t <n>
                                  Start at track n.
                              --volume <v>, -v <v>
                                  Change the playback volume, where a value of 1.0 is 100% volume.
                              --speed <s>, -s <s>
                                  Change the playback speed, where a value of 1.0 is 100% speed;
                              --shuffle
                                  Shuffle the playback.
                              --repeat
                                  Continuously plays the given track/queue on repeat.
                          """);
    }

    extension(Console)
    {
        private static void WriteError(string text)
        {
            PrintHelp();
            Console.WriteLine();
            Console.WriteLine($"\e[31mERROR: {text}\e[0m");
        }
    }
}