using Flyoobe3.Models;
using Flyoobe3.Features.SetupActions;

namespace Flyoobe3.Services;

//loads the loose rule, app and bloatware databases into the small models used by the rest of Flyoobe
//it only builds and merges the catalog - checking or changing this PC belongs to the other services
//I reused the built-in + extra database idea from my CrapFixer because it already works well:
//https://github.com/builtbybel/CrapFixer
//I did not want to reinvent the wheel here, dropping another ini into Data should be enough
internal sealed class SetupCatalog
{
    public List<SetupRule> Rules { get; } = new List<SetupRule>();
    public List<AppItem> Apps { get; } = new List<AppItem>();
    public List<BloatwareItem> Bloatware { get; } = new List<BloatwareItem>();
    public List<SetupAction> Actions { get; } = new List<SetupAction>();
    public PersonalizationChoices? RecipePreferences { get; set; }

    public void Load()
    {
        Rules.Clear();
        Apps.Clear();
        Bloatware.Clear();
        Actions.Clear();
        RecipePreferences = null;

        //built-in first, extras afterwards; the last same-named section wins just like in CrapFixer
        foreach (var path in AppPaths.RuleDatabases())
        {
            foreach (var section in IniFile.Read(path))
            {
                if (!MatchesThisWindows(section.Get("OsVersion"))) continue;
                var rule = ReadRule(section);
                if (rule.Entries.Count == 0) continue;

                var oldIndex = Rules.FindIndex(item =>
                    item.Name.Equals(rule.Name, StringComparison.OrdinalIgnoreCase));
                if (oldIndex < 0) Rules.Add(rule);
                else Rules[oldIndex] = rule;
            }
        }

        //installable apps from Apps.ini
        foreach (var section in IniFile.Read(AppPaths.Apps))
        {
            foreach (var pair in section.Values)
            {
                if (pair.Value.Equals("na", StringComparison.OrdinalIgnoreCase) || pair.Value.Length == 0) continue;
                Apps.Add(new AppItem
                {
                    Name = pair.Key,
                    Category = section.Name,
                    WingetId = pair.Value,
                    Description = Loc.Format("Apps_Description", pair.Key)
                });
            }
        }

        //optional inbox apps, the installed state is checked later
        foreach (var section in IniFile.Read(AppPaths.Bloatware))
        {
            var item = new BloatwareItem
            {
                Name = section.Name,
                Category = section.Get("Category", Loc.Get("Catalog_WindowsApps")),
                PackageName = section.Get("PackageName"),
                Description = section.Get("Description")
            };
            if (item.PackageName.Length > 0) Bloatware.Add(item);
        }

        ReloadActions();
    }

    public void ReloadActions()
    {
        var selected = Actions.Where(item => item.Selected)
            .Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Actions.Clear();

        //The service returns nothing while the optional feature is off, so no action code leaks into normal startup.
        foreach (var action in SetupActionService.Load())
        {
            action.Selected = selected.Contains(action.Id);
            Actions.Add(action);
        }
    }

    private static SetupRule ReadRule(IniSection section)
    {
        var rule = new SetupRule
        {
            Name = section.Name,
            Category = section.Get("Category", Loc.Get("Catalog_Windows")),
            Description = section.Get("Description"),
            Selected = IsTrue(section.Get("DefaultSelected")),
            RequiresAdmin = IsTrue(section.Get("RequiresAdmin")),
            Restart = section.Get("Restart")
        };

        AddEntry(rule, section, "");
        for (var number = 2; ; number++)
        {
            var suffix = number.ToString();
            if (section.Get("Path" + suffix).Length == 0) break;
            AddEntry(rule, section, suffix);
        }

        if (!rule.RequiresAdmin)
            rule.RequiresAdmin = rule.Entries.Any(IsProtected);
        return rule;
    }

    private static void AddEntry(SetupRule rule, IniSection section, string suffix)
    {
        var path = section.Get("Path" + suffix);
        var slash = path.IndexOf('\\');
        if (slash < 1) return;

        rule.Entries.Add(new RegistryEntry
        {
            Hive = path.Substring(0, slash).TrimEnd(':'),
            Key = path.Substring(slash + 1),
            ValueName = section.Get("ValueName" + suffix),
            ValueType = section.Get("ValueType" + suffix, "DWORD"),
            RecommendedValue = section.Get("RecommendedValue" + suffix),
            DefaultValue = section.Get("DefaultValue" + suffix)
        });
    }

    private static bool IsProtected(RegistryEntry entry) =>
        entry.Hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase) ||
        entry.Hive.Equals("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) ||
        entry.Hive.Equals("HKCR", StringComparison.OrdinalIgnoreCase) ||
        entry.Hive.Equals("HKEY_CLASSES_ROOT", StringComparison.OrdinalIgnoreCase) ||
        entry.Key.IndexOf("\\Policies\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
        entry.Key.StartsWith("Policies\\", StringComparison.OrdinalIgnoreCase);

    //OsVersion is empty for rules that fit every Windows, otherwise it names the single one they
    //belong to. A rule that names the other version is left out of the catalog entirely, so it is
    //never scanned, shown or applied on a system it was not written for.
    //The manifest carries the Windows 10 compatibility GUID, so the build number here is the real one.
    private static bool MatchesThisWindows(string value) =>
        value.Length == 0 || value == (Environment.OSVersion.Version.Build >= 22000 ? "11" : "10");

    private static bool IsTrue(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
}
