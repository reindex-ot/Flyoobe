using Flyoobe3.Models;
using Microsoft.Win32;
using System.Globalization;

namespace Flyoobe3.Services;

//scans, applies and restores the registry entries prepared by SetupCatalog
//this is the only rule service that writes the registry; confirmation and selection stay in the views
internal sealed class RegistryService
{
    //read only, fills the live state shown by the views
    public Task ScanAsync(IEnumerable<SetupRule> rules) => Task.Run(() =>
    {
        foreach (var rule in rules) Scan(rule);
    });

    //writes only the recommended values from the loaded signature
    public Task<string?> ApplyAsync(IEnumerable<SetupRule> rules) => Task.Run(() =>
    {
        var items = rules.ToList();
        foreach (var rule in items)
        {
            foreach (var entry in rule.Entries)
            {
                //read first, so the log can show the value Flyoobe replaced
                var before = Read(entry);
                if (!Write(entry, entry.RecommendedValue))
                    return Loc.Format("Service_CouldNotWrite", $"{entry.Hive}\\{entry.Key}\\{entry.ValueName}");
                ChangeLog.Rule(rule.Name, entry, before, entry.RecommendedValue);
            }
            Scan(rule);
        }
        WindowsActions.NotifyShellSettingsChanged();
        if (NeedsExplorerRestart(items)) WindowsActions.RestartExplorer();
        return null;
    });

    //puts back exactly what the signature calls its Windows default
    public Task<string?> RestoreAsync(IEnumerable<SetupRule> rules) => Task.Run(() =>
    {
        var items = rules.ToList();
        foreach (var rule in items)
        {
            foreach (var entry in rule.Entries)
            {
                if (entry.DefaultValue.Length == 0) continue;
                //all three routes below replace the same value, so one read covers them
                var before = Read(entry);
                if (entry.DefaultValue.Equals("<delete>", StringComparison.OrdinalIgnoreCase))
                {
                    if (!DeleteKey(entry)) return Loc.Format("Service_CouldNotRemove", $"{entry.Hive}\\{entry.Key}");
                }
                else if (entry.DefaultValue.Equals("<deletevalue>", StringComparison.OrdinalIgnoreCase))
                {
                    if (!DeleteValue(entry)) return Loc.Format("Service_CouldNotRemove", entry.ValueName);
                }
                else if (!Write(entry, entry.DefaultValue))
                    return Loc.Format("Service_CouldNotRestore", $"{entry.Hive}\\{entry.Key}\\{entry.ValueName}");
                ChangeLog.Rule(rule.Name, entry, before, entry.DefaultValue);
            }
            Scan(rule);
        }
        WindowsActions.NotifyShellSettingsChanged();
        if (NeedsExplorerRestart(items)) WindowsActions.RestartExplorer();
        return null;
    });

    private static bool NeedsExplorerRestart(IEnumerable<SetupRule> rules) => rules.Any(rule =>
        rule.Restart.Equals("explorer", StringComparison.OrdinalIgnoreCase));

    private static void Scan(SetupRule rule)
    {
        rule.IsApplied = true;
        rule.IsDefault = true;
        for (var i = 0; i < rule.Entries.Count; i++)
        {
            var entry = rule.Entries[i];
            var current = Read(entry);
            if (i == 0) rule.CurrentValue = current ?? Loc.Get("Service_NotSet");
            if (!Matches(current, entry.RecommendedValue, entry.ValueType))
                rule.IsApplied = false;
            if (entry.DefaultValue.Length == 0 || !Matches(current, entry.DefaultValue, entry.ValueType))
                rule.IsDefault = false;
        }
    }

    //delete markers are real states too, not missing scan data
    private static bool Matches(string? current, string expected, string type)
    {
        if (expected.Equals("<delete>", StringComparison.OrdinalIgnoreCase) ||
            expected.Equals("<deletevalue>", StringComparison.OrdinalIgnoreCase))
            return current == null;
        return current != null && EqualValue(current, expected, type);
    }

    private static string? Read(RegistryEntry entry)
    {
        try
        {
            using var key = Root(entry.Hive)?.OpenSubKey(entry.Key);
            if (key == null) return null;
            var value = key.GetValue(Name(entry.ValueName));
            if (value == null) return null;
            if (value is int dword) return unchecked((uint)dword).ToString(CultureInfo.InvariantCulture);
            if (value is long qword) return unchecked((ulong)qword).ToString(CultureInfo.InvariantCulture);
            if (value is byte[] bytes) return BitConverter.ToString(bytes).Replace("-", "");
            return value.ToString();
        }
        catch { return null; }
    }

    private static bool Write(RegistryEntry entry, string value)
    {
        try
        {
            using var key = Root(entry.Hive)?.CreateSubKey(entry.Key, true);
            if (key == null) return false;
            var name = Name(entry.ValueName);

            switch (entry.ValueType.ToUpperInvariant())
            {
                case "DWORD":
                    if (!TryNumber(value, out var dword)) return false;
                    key.SetValue(name, unchecked((int)(uint)dword), RegistryValueKind.DWord);
                    break;
                case "QWORD":
                    if (!TryNumber(value, out var qword)) return false;
                    key.SetValue(name, unchecked((long)qword), RegistryValueKind.QWord);
                    break;
                case "EXPANDSTRING":
                    key.SetValue(name, value, RegistryValueKind.ExpandString);
                    break;
                case "BINARY":
                    key.SetValue(name, ParseBytes(value), RegistryValueKind.Binary);
                    break;
                default:
                    key.SetValue(name, value, RegistryValueKind.String);
                    break;
            }
            return true;
        }
        catch { return false; }
    }

    private static bool DeleteValue(RegistryEntry entry)
    {
        try
        {
            using var key = Root(entry.Hive)?.OpenSubKey(entry.Key, true);
            if (key == null) return true;
            key.DeleteValue(Name(entry.ValueName), false);
            return true;
        }
        catch { return false; }
    }

    private static bool DeleteKey(RegistryEntry entry)
    {
        try { Root(entry.Hive)?.DeleteSubKeyTree(entry.Key, false); return true; }
        catch { return false; }
    }

    private static bool EqualValue(string current, string expected, string type)
    {
        if ((type.Equals("DWORD", StringComparison.OrdinalIgnoreCase) ||
             type.Equals("QWORD", StringComparison.OrdinalIgnoreCase)) &&
            TryNumber(current, out var left) && TryNumber(expected, out var right))
            return left == right;
        return string.Equals(current, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNumber(string value, out ulong number)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out number);
        return ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    private static byte[] ParseBytes(string value)
    {
        var clean = value.Replace(" ", "").Replace(",", "").Replace("-", "");
        var bytes = new byte[clean.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(clean.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }

    private static string Name(string valueName) => valueName == "(Default)" ? "" : valueName;

    private static RegistryKey? Root(string hive)
    {
        switch (hive.ToUpperInvariant())
        {
            case "HKCU": case "HKEY_CURRENT_USER": return Registry.CurrentUser;
            case "HKLM": case "HKEY_LOCAL_MACHINE": return Registry.LocalMachine;
            case "HKCR": case "HKEY_CLASSES_ROOT": return Registry.ClassesRoot;
            case "HKU": case "HKEY_USERS": return Registry.Users;
            default: return null;
        }
    }
}
