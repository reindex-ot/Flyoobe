using Flyoobe3.Models;
using System.Globalization;

namespace Flyoobe3.Services;

//writes one readable line for every change Flyoobe makes to this pc
//the file is meant as evidence, so it stores stable ids and never translated display names
//
//the log is written in the services only, never in a view. these are all the places:
//  RegistryService.ApplyAsync          rules that get the recommended value
//  RegistryService.RestoreAsync        rules that get the windows default back
//  PersonalizationService.WriteBool    the four personalization preferences
//  PackageService.InstallAsync         apps installed through winget
//  PackageService.RemoveAsync          preinstalled apps removed
//  SetupActionService.RunAsync         setup actions that finished without an error
//recipes need no own place, they run through the same services
internal static class ChangeLog
{
    //one generation is enough, this is a log and not an archive
    private const long MaxBytes = 1024 * 1024;
    private static readonly object FileLock = new object();

    //.txt instead of .log, so a double click always finds an editor
    public static string LogPath { get; } = System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(AppSettings.SettingsPath) ?? AppContext.BaseDirectory,
        "Flyoobe-changes.txt");

    public static bool Exists => File.Exists(LogPath);

    //one registry value of one rule, with the value it replaced
    public static void Rule(string ruleName, RegistryEntry entry, string? before, string after) =>
        Write("Rule", ruleName,
            entry.Hive + "\\" + entry.Key + "\\" + entry.ValueName + "  " + Change(before, after));

    public static void Add(string area, string item, string detail) => Write(area, item, detail);

    //shows what the value was and what Flyoobe made of it
    public static string Change(string? before, string after) =>
        (string.IsNullOrEmpty(before) ? "(not set)" : before) + " -> " + after;

    private static void Write(string area, string item, string detail)
    {
        //the log is off until the user turns it on, so nothing is written behind their back
        if (!AppSettings.Instance.ChangeLogEnabled) return;

        //a failing log must never stop a registry write, so this stays quiet on purpose
        try
        {
            lock (FileLock)
            {
                Rotate();
                var folder = System.IO.Path.GetDirectoryName(LogPath);
                if (folder != null) Directory.CreateDirectory(folder);
                File.AppendAllText(LogPath, string.Format(CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss}  {1,-11} {2,-34} {3}{4}",
                    DateTime.Now, area, item, detail, Environment.NewLine));
            }
        }
        catch { }
    }

    private static void Rotate()
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length < MaxBytes) return;
        var previous = LogPath + ".old";
        if (File.Exists(previous)) File.Delete(previous);
        File.Move(LogPath, previous);
    }
}
