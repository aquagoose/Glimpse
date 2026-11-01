using System.IO.Pipes;
using System.Net.Sockets;
using Glimpse.API;
using Glimpse.Forms;
using piko.SDL3;

namespace Glimpse;

public static class Program
{
    [STAThread]
    public static unsafe void Main(string[] args)
    {
#if !DEBUG
        Console.WriteLine("Checking for an already existing instance.");
        NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", Glimpse.PipeServerName, PipeDirection.Out);
        try
        {
            pipeClient.Connect(100);
            Console.WriteLine("Found!");
            BinaryWriter writer = new BinaryWriter(pipeClient);
            
            if (args.Length > 0)
            {
                string cwd = Environment.CurrentDirectory;
                
                foreach (string path in args)
                {
                    writer.Write((byte) CommunicationFlags.PlayFile);
                    
                    // Ensure the absolute path is always written otherwise the existing Glimpse instance won't know
                    // what to do with it.
                    if (Path.IsPathRooted(path))
                        writer.Write(path);
                    else
                        writer.Write(Path.Combine(cwd, path));
                }
            }
            else
                writer.Write((byte) CommunicationFlags.Focus);

            return;
        }
        catch (TimeoutException) { }
        catch (SocketException) { }

        pipeClient.Dispose();
        
        AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
        {
            string msg = eventArgs.ExceptionObject?.ToString() ??
                         "An error occurred, but there was no message attached.";
            SDL.ShowSimpleMessageBox(SDL.MessageBoxFlags.Error, "Glimpse", msg, new SDL.Window());
        };

        try
#endif
        {
            using Glimpse glimpse = new Glimpse();
            glimpse.Run(new GlimpsePlayer(), args);
        }
#if !DEBUG
        catch (Exception e)
        {
            // try and log the exception if possible.
            // for whatever reason sometimes the logger tries to open itself multiple times, throws an exception, then
            // overwrites the original exception.
            // todo fix this, ideally by moving away from LastSession.log to a named Glimpse-yyyy-mm-dd-hh-mm-ss.log file
            try
            {
                Logger logger = new Logger();
                logger.Log(e.ToString());
            }
            catch (Exception) { }

            const string title = "Glimpse";
            
            string logLocation = Path.Combine(IConfigManager.BaseDir, "LastSession.log");
            string message = $"Oops! Glimpse crashed.\nLog file at: {logLocation}\n\nPlease send log file + the following error to the developers:\n{e}";

            SDL.ShowSimpleMessageBox(SDL.MessageBoxFlags.Error, title, message, new SDL.Window());
        }
#endif
    }

    [Flags]
    public enum CommunicationFlags : byte
    {
        None = 0,
        
        PlayFile = 1 << 0,
        
        Focus = 1 << 1,
    }
}