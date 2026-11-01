#!/usr/bin/env -S dotnet --

// aot is enabled by default apparently ?? so have to disable it to allow json serialization to work properly
#:property PublishAot=false

// ----------------------------- Glimpse SDK Plugin Packager -----------------------------
// -- Build & package plugins so they're ready to be installed as a single package file --
// ---------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

bool pack = true;
string? projectName = null;
string? pluginDir = null;
string? glimpseVersion = null;

int argPos = 0;
while (ReadArg(args, ref argPos, out string? arg))
{
    if (arg.StartsWith('-'))
    {
        switch (arg)
        {
            case "-h" or "--help":
                PrintHelp();
                return 0;
            
            // Set a project file directly.
            case "-p" or "--project":
            {
                if (!ReadArg(args, ref argPos, out arg))
                {
                    PrintError("Missing project name!");
                    return 1;
                }

                // It's likely people will add .csproj, so we must remove it.
                projectName = arg.Replace(".csproj", "");
                break;
            }
            
            // Does not pack into a gplg file.
            case "--no-pack":
                pack = false;
                break;

            case "--glimpse-version":
            {
                if (!ReadArg(args, ref argPos, out string? version))
                {
                    PrintError(
                        "No version was provided.\nNOTE: Unless you happen to be Glimpse's build/publish script, don't use this flag!!!\nThis flag is for the Glimpse publish script ONLY.");
                    return 1;
                }

                glimpseVersion = version;

                break;
            }

            default:
                PrintError($"Unrecognized argument \"{arg}\"!");
                return 1;
        }
    }
    else if (pluginDir == null)
        pluginDir = arg.TrimEnd('/'); // Remove trailing '/' so that Path.GetFileName returns a proper value. Hack? yes
    else
    {
        PrintError("Can only specify 1 plugin directory!");
        return 1;
    }
}

if (pluginDir == null)
{
    PrintError("No plugin dir defined!");
    return 1;
}

// Ensure the plugin directory is always an absolute path. Why? Because
pluginDir = Path.GetFullPath(pluginDir);

if (!Directory.Exists(pluginDir))
{
    PrintError($"Directory '{pluginDir}' does not exist.");
    return 1;
}

// Most people probably won't be passing in a project name.
if (projectName == null)
    projectName = Path.GetFileName(pluginDir);

string publishDir = Path.Combine(pluginDir, "Publish");
string pluginJsonFile = Path.Combine(pluginDir, "Plugin.json");
string outDir = Path.Combine(pluginDir, $"{projectName}_Out");

if (!File.Exists(pluginJsonFile))
{
    PrintError("Plugin.json not found. A plugin MUST contain a Plugin.json file.");
    return 1;
}

// Delete existing dirs if necessary to prevent errors when overwriting (and to ensure clean slate)
if (Directory.Exists(publishDir))
    Directory.Delete(publishDir, true);

if (Directory.Exists(outDir))
    Directory.Delete(outDir, true);

// Publish the plugin...
Process process = new()
{
    StartInfo = new ProcessStartInfo("dotnet")
    {
        Arguments = $"publish {Path.Combine(pluginDir, projectName)}.csproj -c Release -o {publishDir}"
    }
};

if (glimpseVersion != null)
    process.StartInfo.Arguments += $" -p:GlimpseVersion=\"{glimpseVersion}\"";

process.Start();
process.WaitForExit();

if (process.ExitCode != 0)
    return 1;

// A list of dependencies that should not be copied.
// These are dependencies that are already provided by Glimpse and copying them may cause conflicts.
// This is not a full list, as every dep that contains these strings will be ignored.
// May cause problems in the future.
ReadOnlySpan<string> dependencyBlacklist =
[
    "cimgui",
    "hexa",
    "glimpse.api",
    "microsoft.win32.systemevents"
];

List<string> filesToPackage = [];

// Parse the deps file that dotnet publish outputs in order to read and parse the various dependencies.
string depsFile = Path.Combine(publishDir, $"{projectName}.deps.json");
JsonNode? node = JsonNode.Parse(File.ReadAllText(depsFile));
JsonNode? targets = node?["targets"];

if (targets == null)
    goto SKIP_PACKAGING;

foreach ((_, JsonNode? target) in targets.AsObject())
{
    if (target == null)
        continue;
    
    foreach ((_, JsonNode? package) in target.AsObject())
    {
        if (package == null)
            continue;
        
        GetRuntimesFromList(package["runtime"], ref filesToPackage, in dependencyBlacklist, true);
        GetRuntimesFromList(package["runtimeTargets"], ref filesToPackage, in dependencyBlacklist, false);
    }
}

SKIP_PACKAGING: ;

JsonNode pluginJson = JsonNode.Parse(File.ReadAllText(pluginJsonFile))!;
if (glimpseVersion != null)
    pluginJson["Version"] = glimpseVersion;

// i can't see this ever being false but the script allows for it so gotta handle it...
if (filesToPackage.Count > 0)
{
    // add the entry point to the json
    pluginJson["EntryPoint"] = filesToPackage[0]; // the deps.json will always place the entry point dll first in the list. todo check this is actually true

    // plugins may not necessarily reference any other assemblies, so only output assemblies if needed
    if (filesToPackage.Count > 1)
    {
        // this is dumb and stupid and i hate it
        JsonArray assemblies = [];
        for (int i = 1; i < filesToPackage.Count; i++) // start at 1 as we don't need to package the entry point
            assemblies.Add<string>(filesToPackage[i]);
        pluginJson["Dependencies"] = assemblies;
    }
}

// Read the native dependencies if the Plugin.json file contains any.
// Usually this is not needed if using nuget packages etc, as the packager will automatically
// copy everything in the `runtimes` directory.
// However some plugins may rely on submodules etc that do not package native dependencies as the compiler expects,
// thus they do not show in the .deps.json file. Therefore a plugin can manually specify native dependencies for this.
JsonNode? nativeDependencies = pluginJson["NativeDependencies"];
if (nativeDependencies != null)
{
    foreach ((_, JsonNode? deps) in nativeDependencies.AsObject())
    {
        if (deps == null)
            continue;
        
        JsonArray depsList = deps.AsArray(); 
        foreach (JsonNode? dep in depsList)
            filesToPackage.Add(dep.ToString());
    }
}

Console.WriteLine(string.Join(',', filesToPackage));

Directory.CreateDirectory(outDir);
// save the plugin.json to the new location
using (Stream fileStream = File.OpenWrite(Path.Combine(outDir, "Plugin.json")))
{
    using Utf8JsonWriter writer = new Utf8JsonWriter(fileStream, new JsonWriterOptions
    {
        Indented = true
    });
    pluginJson.WriteTo(writer);
}

// Move everything else
foreach (string file in filesToPackage)
{
    // handle files that are in subdirectories
    string fileDir = Path.GetDirectoryName(file);
    string moveDir = Path.Combine(outDir, fileDir);
    Directory.CreateDirectory(moveDir);
    
    // we can just directly move the files instead of copying as the publish directory is deleted once we're done
    File.Move(Path.Combine(publishDir, file), Path.Combine(outDir, file));
}

if (pack)
{
    string packFileName = $"{projectName}.gplg";
    
    if (File.Exists(packFileName))
        File.Delete(packFileName);
    
    // mmmm i sure love renamed zip archives
    ZipFile.CreateFromDirectory(outDir, packFileName);
    Directory.Delete(outDir, true);
}
else
{
    // move the output directory to the cwd and rename it to the project name
    Directory.Move(outDir, projectName);
}

Directory.Delete(publishDir, true);

return 0;

void GetRuntimesFromList(JsonNode? node, ref List<string> targets, in ReadOnlySpan<string> dependencyBlacklist, bool stripRuntimeDir)
{
    if (node != null)
    {
        foreach ((string runtime, _) in node.AsObject())
        {
            foreach (string dep in dependencyBlacklist)
            {
                if (runtime.Contains(dep, StringComparison.CurrentCultureIgnoreCase))
                    goto SKIP;
            }

            string runtimeName = stripRuntimeDir ? Path.GetFileName(runtime) : runtime;
            
            targets.Add(runtimeName);
            
            SKIP: ;
        }
    }
}

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
                       USAGE: build-plugin [OPTIONS] <Plugin Dir>

                       Options:
                           --project <name>, -p <name>
                               Define the name of the project file to build.
                               If not provided, the directory name will be used.

                           --no-pack
                               Do not pack the output into a .gplg file..
                               This flag is primarily for the automated Glimpse build script,
                               but can be useful for debugging.
                       """);
}

void PrintError(string error)
{
    PrintHelp();
    Console.WriteLine();
    Console.WriteLine($"\e[31mERROR: {error}\e[0m");
}