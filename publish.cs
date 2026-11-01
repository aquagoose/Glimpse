#!/usr/bin/env -S dotnet --

// Glimpse Publish Script
// Builds and packages Glimpse for distribution.
// Primarily designed to Just Work™ for CI, however does have some dependencies depending on platform.
// Windows requires `makensis`, Linux requires `appimagetool`.
// These are only required for packaging, and not required if `--no-pack` is specified.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;

string outName = "Publish";
string? version = null;
string? runtime = null;
bool pack = true;
bool plugins = true;
bool noPDB = false;
bool aot = false;

int argPos = 0;
while (ReadArg(args, ref argPos, out string? arg))
{
    if (arg.StartsWith('-'))
    {
        switch (arg)
        {
            case "--output" or "-o":
            {
                if (!ReadArg(args, ref argPos, out string? newOutName))
                {
                    PrintError("No output name specified!");
                    return 1;
                }

                outName = newOutName;

                break;
            }

            case "--runtime" or "-r":
            {
                if (!ReadArg(args, ref argPos, out string? newRuntime))
                {
                    PrintError("No runtime identifier provided!");
                    return 1;
                }

                runtime = newRuntime;

                break;
            }

            case "--no-pack":
                pack = false;
                break;
            case "--no-plugins":
                plugins = false;
                break;
            case "--no-pdb":
                noPDB = true;
                break;
            case "--aot":
                plugins = false;
                aot = true;
                break;

            default:
            {
                PrintError($"Unrecognized argument \"{arg}\".");
                return 1;
            }
        }
    }
    else
    {
        version = arg;
    }
}

if (version == null)
{
    PrintError("No version specified!");
    return 1;
}

// auto determine the current runtime based on the OS and arch
if (runtime == null)
{
    if (OperatingSystem.IsWindows())
        runtime = "win";
    if (OperatingSystem.IsLinux())
        runtime = "linux";
    if (OperatingSystem.IsMacOS())
        runtime = "osx";

    switch (RuntimeInformation.OSArchitecture)
    {
        case Architecture.X64:
            runtime += "-x64";
            break;
        case Architecture.Arm64:
            runtime += "-arm64";
            break;
        default:
            PrintError($"Unsupported OS architecture \"{RuntimeInformation.OSArchitecture}\"");
            return 1;
    }
}

// until glimpse supports arm win/linux and intel osx (will it ever support that?) then this check is required
if (runtime is not ("win-x64" or "linux-x64" or "osx-arm64"))
{
    PrintError($"Runtime \"{runtime}\" is not supported!");
    return 1;
}

string publishDir = Path.Combine(Environment.CurrentDirectory, outName);
if (Directory.Exists(publishDir))
    Directory.Delete(publishDir, true);

// ================ Publish Glimpse ================

string glimpseSrcDir = Path.Combine(Environment.CurrentDirectory, "src", "Glimpse");
//string glimpsecliSrcDir = Path.Combine(Environment.CurrentDirectory, "src", "glimpsecli");

List<string> glimpsePublishArgs =
[
    "publish",
    glimpseSrcDir,
    "-c", "Release",
    "-r", runtime,
    "-o", publishDir,
    $"-p:GlimpseVersion=\"{version}\"",
    "-p:GenerateDocumentationFile=false"
];

if (aot)
    glimpsePublishArgs.Add("-p:PublishAot=true");
if (noPDB)
    glimpsePublishArgs.Add("-p:DebugType=none");

// build glimpse itself
if (!RunProcess("dotnet", glimpsePublishArgs))
{
    PrintError("Failed to build glimpse.", false);
    return 1;
}

// =================================================

// ==================== Plugins ====================
if (plugins)
{
    string pluginsBaseDir = Path.Combine(Environment.CurrentDirectory, "Plugins");
    string pluginsOutDir = Path.Combine(publishDir, "Plugins");
    string packageScriptLocation = Path.Combine(Environment.CurrentDirectory, "tools", "sdk", "package-plugin.cs");

    Directory.CreateDirectory(pluginsOutDir);

    // assumes that the built-in plugins are NOT in subdirectories
    foreach (string dir in Directory.GetDirectories(pluginsBaseDir))
    {
        // check if this directory has a plugin json. if it doesn't, it's probably not a plugin
        bool hasPluginJson = false;
        foreach (string file in Directory.GetFiles(dir))
        {
            if (Path.GetFileName(file) == "Plugin.json")
            {
                hasPluginJson = true;
                break;
            }
        }

        if (!hasPluginJson)
            continue;

        string pluginName = Path.GetFileName(dir);

        // run the packing script. i'd love to include this file directly but that's not supported in .net 10
        if (!RunProcess("dotnet", packageScriptLocation, dir, "--no-pack", "--glimpse-version", version))
        {
            PrintError($"Failed to package plugin \"{pluginName}\".", false);
            return 1;
        }

        // move the plugin into the "Plugins" directory
        Directory.Move(Path.Combine(Environment.CurrentDirectory, pluginName), Path.Combine(pluginsOutDir, pluginName));
    }
}
// =================================================

// ==================== Cleanup ====================

string cwd = Environment.CurrentDirectory;
Environment.CurrentDirectory = publishDir;

// MixrSharp bundles silk's SDL2. we don't need that and it just takes up space, so remove it.
File.Delete("Silk.NET.SDL.dll");

// depending on the runtime, delete files we don't need.
// should this be in the project file as a target? probably!
// do i know how to do that? no!
// can i be bothered to learn? no!
// can i just do it here instead? yes!
if (runtime.StartsWith("win"))
{
    File.Delete("libmixr.so");
    File.Delete("libmixr.dylib");
    File.Delete("libempress.so");
    File.Delete("SDL2.dll");
}
else if (runtime.StartsWith("linux"))
{
    File.Delete("mixr.dll");
    File.Delete("libmixr.dylib");
    File.Delete("libSDL2-2.0.so");
}
else if (runtime.StartsWith("osx"))
{
    File.Delete("mixr.dll");
    File.Delete("libmixr.so");
    File.Delete("libempress.so");
    File.Delete("libSDL2-2.0.dylib");
}

Environment.CurrentDirectory = cwd;

// =================================================

// ===================== Pack ======================
if (pack)
{
    if (runtime.StartsWith("win"))
    {
        string nsiDir = Path.Combine(Environment.CurrentDirectory, "packaging", "windows");
        string installerName = $"InstallGlimpse-{version}.exe";

        // zip it up before building the nsi so we don't bundle vc redist
        using MemoryStream zipStream = new MemoryStream();
        ZipFile.CreateFromDirectory(publishDir, zipStream);
        Console.WriteLine(zipStream.Length);

        // todo use httpclient
        // download visual studio c++ runtime
        using (WebClient client = new WebClient())
            client.DownloadFile("https://aka.ms/vc14/vc_redist.x64.exe", Path.Combine(publishDir, "vc_redist.x64.exe"));

        if (!RunProcess("makensis", $"-DVERSION={version}", $"-DPUBLISHDIR={publishDir}", Path.Combine(nsiDir, "glimpse.nsi")))
        {
            PrintError("Failed to package NSIS file.", false);
            return 1;
        }

        // hack to clear the contents of the publish directory
        Directory.Delete(publishDir, true);
        Directory.CreateDirectory(publishDir);

        using FileStream zipWriteStream = File.Create(Path.Combine(publishDir, $"{outName}.zip"));
        zipStream.WriteTo(zipWriteStream);

        File.Move(Path.Combine(nsiDir, installerName), Path.Combine(publishDir, installerName));
    }
    else if (runtime.StartsWith("linux"))
    {
        string packagingDir = Path.Combine(Environment.CurrentDirectory, "packaging", "linux");

        // ========= Tarball =========

        string tarballDir = Path.Combine(packagingDir, "tarball");
        string tarballBinDir = Path.Combine(tarballDir, "bin");

        if (Directory.Exists(tarballBinDir))
            Directory.Delete(tarballBinDir, true);

        // nondestructively copy all files in the publish directory to the specified directory
        void CopyPublishFilesToDir(string dirName)
        {
            Directory.CreateDirectory(dirName);
            foreach (string file in Directory.GetFiles(publishDir, "*", SearchOption.AllDirectories))
            {
                string dir = Path.GetRelativePath(publishDir, Path.GetDirectoryName(file));
                Directory.CreateDirectory(Path.Combine(dirName, dir));
                File.Copy(file, Path.Combine(dirName, dir, Path.GetFileName(file)));
            }
        }

        CopyPublishFilesToDir(tarballBinDir);

        using MemoryStream tarStream = new MemoryStream();
        TarFile.CreateFromDirectory(tarballDir, tarStream, false);
        Console.WriteLine(tarStream.Length);

        // ===========================

        // ======== AppImage =========

        string appImageDir = Path.Combine(packagingDir, "appimage");
        string appImageUsrDir = Path.Combine(appImageDir, "usr");
        string appImageBinaryDir = Path.Combine(appImageUsrDir, "bin");
        string appImageDest = $"{outName}.AppImage";

        // reset state
        if (Directory.Exists(appImageUsrDir))
            Directory.Delete(appImageUsrDir, true);
        File.Delete(Path.Combine(appImageDir, ".DirIcon"));

        CopyPublishFilesToDir(appImageBinaryDir);

        RunProcess("appimagetool-x86_64.AppImage", appImageDir, appImageDest);

        // ===========================

        // hack to clear the contents of the publish directory
        Directory.Delete(publishDir, true);
        Directory.CreateDirectory(publishDir);

        File.Move(appImageDest, Path.Combine(publishDir, appImageDest));

        using FileStream tarWriteStream = File.Create(Path.Combine(publishDir, $"{outName}.tar.gz"));
        using GZipStream gZipStream = new GZipStream(tarWriteStream, CompressionLevel.Optimal);
        tarStream.WriteTo(gZipStream);

        // cleanup garbage
        Directory.Delete(tarballBinDir, true);

        Directory.Delete(appImageUsrDir, true);
        File.Delete(Path.Combine(appImageDir, ".DirIcon"));
    }
    else if (runtime.StartsWith("osx"))
    {
        string macosDir = Path.Combine(Environment.CurrentDirectory, "packaging", "macos");
        const string glimpseAppName = "Glimpse.app";

        if (Directory.Exists(glimpseAppName))
            Directory.Delete(glimpseAppName, true);

        Directory.CreateDirectory(glimpseAppName);
        Directory.CreateDirectory(Path.Combine(glimpseAppName, "Contents", "Resources"));

        // replace the GLIMPSE_VERSION placeholder in Info.plist with the actual version
        // then write to the correct location.
        string plist = File.ReadAllText(Path.Combine(macosDir, "Info.plist"));
        plist = plist.Replace("GLIMPSE_VERSION", version);
        File.WriteAllText(Path.Combine(glimpseAppName, "Contents", "Info.plist"), plist);

        File.Copy(Path.Combine(macosDir, "Glimpse.icns"), Path.Combine(glimpseAppName, "Contents", "Resources", "Glimpse.icns"));
        Directory.Move(publishDir, Path.Combine(glimpseAppName, "Contents", "MacOS"));

        // since we've moved the publish directory, we need to create it again
        Directory.CreateDirectory(publishDir);

        ZipFile.CreateFromDirectory(glimpseAppName, Path.Combine(publishDir, $"{outName}.zip"),
            CompressionLevel.Optimal, true);
        Directory.Delete(glimpseAppName, true);
    }
}

// =================================================

return 0;

bool ReadArg(string[] args, ref int argPos, [NotNullWhen(true)] out string? arg)
{
    if (argPos >= args.Length)
    {
        arg = null;
        return false;
    }

    arg = args[argPos++];
    return true;
}

void PrintHelp()
{
    Console.WriteLine("""
                      USAGE: publish [OPTIONS] <version>
                      Publish and package (unless specified) Glimpse with the given version number.

                      Options:
                          --output <name>, -o <name>
                              Set the output name.
                              If not provided, the output name will be "Publish"
                      
                          --runtime <runtime>, -r <runtime>
                              Set the .NET runtime identifier to build for.
                              If not provided, the RID for the current OS will be used.

                          --no-pack
                              Do not package Glimpse into the platform's installer format.
                              
                          --no-plugins
                              Do not build or include plugins.
                              
                          --no-pdb
                              Do not export PDB files.
                              
                          --aot
                              Compile using NativeAOT. This will disable plugin support.
                      """);
}

void PrintError(string error, bool printHelp = true)
{
    if (printHelp)
    {
        PrintHelp();
        Console.WriteLine();
    }

    Console.WriteLine($"\e[31mERROR: {error}\e[0m");
}

bool RunProcess(string processName, params IEnumerable<string> args)
{
    Process process = new Process()
    {
        StartInfo = new ProcessStartInfo(processName, args)
    };

    if (!process.Start())
        return false;

    process.WaitForExit();
    return process.ExitCode == 0;
}