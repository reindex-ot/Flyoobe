using Flyoobe3.Models;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Flyoobe3.Services;

//wraps every winget and Appx process used for installing, detecting and removing apps
//it updates the catalog models and returns plain error text; dialogs and selections stay in the views
internal sealed class PackageService
{
    //winget refuses an install when the app is already there; both codes mean exactly that
    private const int AlreadyInstalled = unchecked((int)0x8A150061);
    private const int NoNewerVersion = unchecked((int)0x8A15002B);

    //winget owns the install, Flyoobe only passes checked ids
    public async Task<string?> InstallAsync(IEnumerable<AppItem> items, IProgress<string>? progress = null)
    {
        foreach (var item in items.Where(item => !item.IsInstalled))
        {
            progress?.Report(Loc.Format("Common_Installing", item.Name));
            //no disable-interactivity here: it would also suppress the uac prompt an installer needs
            var result = await RunCaptureAsync("winget.exe",
                $"install --id \"{item.WingetId}\" -e --silent " +
                "--accept-package-agreements --accept-source-agreements");
            //an app that is already there is a result, not a failure, so the row is just corrected
            if (result.ExitCode == AlreadyInstalled || result.ExitCode == NoNewerVersion)
            {
                item.IsInstalled = true;
                item.Selected = false;
                continue;
            }
            if (result.ExitCode != 0) return item.Name + ": " + Explain(result);
            //winget reported success, so this is the point where the pc has really changed
            ChangeLog.Add("App", item.WingetId, "installed");
            item.IsInstalled = true;
        }
        return null;
    }

    //one winget call is enough to mark the matching catalog entries
    public async Task<string?> ScanInstalledAppsAsync(IEnumerable<AppItem> items)
    {
        var result = await RunCaptureAsync("winget.exe",
            "list --accept-source-agreements --disable-interactivity");
        if (result.ExitCode != 0) return Explain(result);

        foreach (var item in items)
        {
            item.IsInstalled = HasWingetId(result.Output, item.WingetId)
                               || HasAppName(result.Output, item.Name);
            if (item.IsInstalled) item.Selected = false;
        }
        return null;
    }

    private static bool HasWingetId(string output, string id) =>
        Regex.IsMatch(output,
            @"(?<![A-Za-z0-9._-])" + Regex.Escape(id) + @"(?![A-Za-z0-9._-])",
            RegexOptions.IgnoreCase);

    //apps installed outside winget carry no catalog id, so the name column is the only other signal
    //it has to start the line and end on a space, otherwise "Notepad" would match "Notepad++"
    private static bool HasAppName(string output, string name) =>
        Regex.IsMatch(output, "^" + Regex.Escape(name) + @"(?=\s|$)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

    //read only appx scan, nothing is removed here
    public async Task<string?> ScanBloatwareAsync(IEnumerable<BloatwareItem> items)
    {
        var result = await RunPowerShellAsync("Get-AppxPackage | ForEach-Object { $_.Name + '|' + $_.PackageFullName }");
        if (result.Error.Length > 0 && result.Output.Length == 0) return result.Error;

        var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var pipe = raw.IndexOf('|');
            if (pipe > 0) installed[raw.Substring(0, pipe)] = raw.Substring(pipe + 1);
        }

        //PackageName in Bloatware.ini is the exact Name from Get-AppxPackage,
        //so this looks the entry up instead of searching for it inside other package names
        foreach (var item in items)
            item.FullPackageName = installed.TryGetValue(item.PackageName, out var fullName) ? fullName : null;
        return null;
    }

    //called only after the user confirmed the checked matches
    public async Task<string?> RemoveAsync(IEnumerable<BloatwareItem> items, IProgress<string>? progress = null)
    {
        foreach (var item in items.Where(item => item.IsInstalled))
        {
            progress?.Report(Loc.Format("Service_Removing", item.Name));
            var package = item.FullPackageName!.Replace("'", "''");
            var result = await RunPowerShellAsync($"Remove-AppxPackage -Package '{package}'");
            if (result.Error.Length > 0) return item.Name + ": " + result.Error;
            //the full name is the exact package that was removed, so it is logged before it is cleared
            ChangeLog.Add("Bloatware", item.PackageName, "removed " + item.FullPackageName);
            item.FullPackageName = null;
        }
        return null;
    }

    //winget explains itself on stdout, so an empty stderr must never become an empty message
    private static string Explain((string Output, string Error, int ExitCode) result)
    {
        if (result.Error.Length > 0) return result.Error;
        var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
            if (lines[i].Trim().Length > 0) return lines[i].Trim();
        //winget documents its codes in hex, so the number stays searchable
        return Loc.Format("Service_ExitCode", "winget", "0x" + result.ExitCode.ToString("X"));
    }

    private static async Task<(string Output, string Error, int ExitCode)> RunCaptureAsync(string fileName, string arguments)
    {
        try
        {
            var info = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                //winget writes utf8, without this the console codepage turns umlauts into mojibake
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using var process = Process.Start(info);
            if (process == null) return ("", Loc.Format("Service_CouldNotStart", fileName), -1);
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(output, error);
            process.WaitForExit();
            return (output.Result, error.Result.Trim(), process.ExitCode);
        }
        catch (Exception ex) { return ("", ex.Message, -1); }
    }

    private static async Task<(string Output, string Error)> RunPowerShellAsync(string command)
    {
        var info = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(info);
        if (process == null) return ("", Loc.Format("Service_CouldNotStart", "PowerShell"));
        process.StandardInput.WriteLine(command);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(output, error);
        process.WaitForExit();
        return (output.Result, error.Result.Trim());
    }
}
