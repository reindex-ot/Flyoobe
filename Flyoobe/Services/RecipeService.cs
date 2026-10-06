using Flyoobe3.Models;
using System.Text;

namespace Flyoobe3.Services;

//reads and writes the portable Flyoobe recipe format, but never changes Windows itself
//I treat recipes as shopping lists: only ids known by the loaded catalogs become actions, never commands
internal static class RecipeService
{
    private const string CurrentFormat = "2";

    public static void Export(string path, SetupCatalog catalog, RecipeExportPlan plan)
    {
        var text = new StringBuilder();
        text.AppendLine("; Flyoobe setup recipe");
        text.AppendLine("; portable choices only - no commands, scripts or local file paths");
        WriteRecipeHeader(text);
        PersonalizationService.WriteRecipe(text, plan.Preferences);
        WriteRules(text, catalog.Rules.Where(item => item.IsApplied || item.IsDefault));
        //technical ids stay valid if a visible app name or the ui language changes
        WriteIds(text, "Apps", plan.AppIds);
        WriteIds(text, "Bloatware", plan.BloatwareIds);
        WriteChoices(text, "Actions", plan.ActionChoices);
        File.WriteAllText(path, text.ToString(), Encoding.UTF8);
    }

    public static bool Import(string path, SetupCatalog catalog)
    {
        var sections = IniFile.Read(path);
        var values = sections.ToDictionary(section => section.Name,
            section => section.Values,
            StringComparer.OrdinalIgnoreCase);
        ValidateFormat(values);
        SelectActions(catalog, values);

        //an import replaces the old staged selection, it never quietly merges both
        SelectRules(catalog.Rules, values);
        SelectIds(catalog.Apps, "Apps", item => item.WingetId, (item, value) => item.Selected = value, values);
        SelectIds(catalog.Bloatware, "Bloatware", item => item.PackageName, (item, value) => item.Selected = value, values);

        values.TryGetValue("Preferences", out var preferenceValues);
        catalog.RecipePreferences = PersonalizationService.ReadRecipeChanges(preferenceValues);
        PrepareBrowser(catalog);

        //the overview only needs to know whether review mode has something to show
        return catalog.Rules.Any(item => item.Selected || item.RestoreWithRecipe) ||
               catalog.Apps.Any(item => item.Selected) ||
               catalog.Bloatware.Any(item => item.Selected) ||
               catalog.Actions.Any(item => item.Selected) ||
               catalog.RecipePreferences?.Count > 0;
    }

    private static void WriteRecipeHeader(StringBuilder text)
    {
        text.AppendLine();
        text.AppendLine("[Recipe]");
        text.AppendLine("Format=" + CurrentFormat);
        text.AppendLine("CreatedWith=" + AppInfo.DisplayVersion);
    }

    private static void WriteRules(StringBuilder text, IEnumerable<SetupRule> rules)
    {
        text.AppendLine();
        text.AppendLine("[Tweaks]");
        //true applies the recommendation, false restores the Windows default
        foreach (var rule in rules.OrderBy(item => item.Name))
            text.AppendLine(rule.Name + "=" + (rule.IsApplied ? "true" : "false"));
    }

    private static void WriteIds(StringBuilder text, string name, IEnumerable<string> ids)
    {
        text.AppendLine();
        text.AppendLine("[" + name + "]");
        foreach (var id in ids.OrderBy(value => value)) text.AppendLine(id + "=true");
    }

    private static void WriteChoices(StringBuilder text, string name,
        IEnumerable<KeyValuePair<string, string>> choices)
    {
        text.AppendLine();
        text.AppendLine("[" + name + "]");
        foreach (var choice in choices.OrderBy(item => item.Key))
            text.AppendLine(choice.Key + "=" + choice.Value);
    }

    private static void ValidateFormat(Dictionary<string, Dictionary<string, string>> sections)
    {
        //old drafts are rejected on purpose, guessing their meaning would be worse
        if (!sections.TryGetValue("Recipe", out var recipe) ||
            !recipe.TryGetValue("Format", out var format) || format != CurrentFormat)
            throw new InvalidDataException(Loc.Format("Recipe_WrongFormat", CurrentFormat));
    }

    private static void PrepareBrowser(SetupCatalog catalog)
    {
        //Windows needs the user to confirm a default browser, but Flyoobe can install it first
        var id = catalog.RecipePreferences?.DefaultBrowserId;
        if (id == null || id.Length == 0) return;
        var browser = catalog.Apps.FirstOrDefault(item =>
            item.Category.Equals("Browsers", StringComparison.OrdinalIgnoreCase) &&
            item.WingetId.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (browser == null)
            throw new InvalidDataException(Loc.Format("Recipe_UnsupportedBrowser", id));
        catalog.RecipePreferences!.DefaultBrowserId = browser.WingetId;

        //no Windows confirmation is needed if this browser is already the default
        var currentId = BrowserService.ReadDefaultBrowserId(catalog.Apps);
        if (browser.WingetId.Equals(currentId, StringComparison.OrdinalIgnoreCase))
        {
            catalog.RecipePreferences.DefaultBrowserId = "";
            if (catalog.RecipePreferences.Count == 0) catalog.RecipePreferences = null;
            return;
        }
        if (!BrowserService.IsInstalled(browser.WingetId, catalog.Apps)) browser.Selected = true;
    }

    private static void SelectActions(SetupCatalog catalog,
        Dictionary<string, Dictionary<string, string>> sections)
    {
        foreach (var action in catalog.Actions) action.Selected = false;
        if (!sections.TryGetValue("Actions", out var values) || values.Count == 0) return;
        if (!AppSettings.Instance.SetupActionsEnabled)
            throw new InvalidDataException(Loc.Get("Recipe_ActionsDisabled"));

        var allowed = catalog.Actions.Where(item => item.RecipeAllowed)
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var missing = values.Keys.Where(id => !allowed.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new InvalidDataException(Loc.Format("Recipe_ActionsMissing", string.Join(", ", missing)));

        foreach (var pair in values)
        {
            var action = allowed[pair.Key];
            if (action.Options.Count == 0)
            {
                action.Selected = ReadAction(pair.Value);
                continue;
            }

            var option = action.Options.FirstOrDefault(item =>
                item.Equals(pair.Value, StringComparison.OrdinalIgnoreCase));
            if (option == null)
                throw new InvalidDataException(Loc.Format("Recipe_ActionOptionInvalid", pair.Value, action.Name));
            action.SelectedOption = option;
            action.Selected = true;
        }
    }

    private static void SelectIds<T>(IEnumerable<T> items, string section, Func<T, string> id,
        Action<T, bool> set, Dictionary<string, Dictionary<string, string>> sections)
    {
        sections.TryGetValue(section, out var values);
        foreach (var item in items)
        {
            var value = values != null && values.TryGetValue(id(item), out var raw) && ReadAction(raw);
            set(item, value);
        }
    }

    private static void SelectRules(IEnumerable<SetupRule> rules,
        Dictionary<string, Dictionary<string, string>> sections)
    {
        sections.TryGetValue("Tweaks", out var values);
        foreach (var rule in rules)
        {
            rule.Selected = false;
            rule.RestoreWithRecipe = false;
            if (values == null || !values.TryGetValue(rule.Name, out var raw)) continue;

            //false is still an action here: restore this exact rule to its default
            rule.Selected = ReadAction(raw);
            rule.RestoreWithRecipe = !rule.Selected;
        }
    }

    private static bool ReadAction(string value)
    {
        //do not treat typos as true, a recipe should never make a fuzzy decision
        if (IsTrue(value)) return true;
        if (IsFalse(value)) return false;
        throw new InvalidDataException(Loc.Format("Recipe_InvalidValue", value));
    }

    private static bool IsTrue(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";

    private static bool IsFalse(string value) =>
        value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0";
}
