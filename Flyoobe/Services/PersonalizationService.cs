using Flyoobe3.Models;
using Microsoft.Win32;
using System.Globalization;
using System.Text;

namespace Flyoobe3.Services;

//owns the small set of personal choices: theme, transparency and taskbar alignment
//the same read/apply code is shared by the Personalization page and portable recipes
internal static class PersonalizationService
{
    private const string ThemeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ExplorerKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public static PersonalizationChoices Read()
    {
        var result = new PersonalizationChoices();
        using (var key = Registry.CurrentUser.OpenSubKey(ThemeKey))
        {
            result.DarkApps = ReadInt(key, "AppsUseLightTheme", 1) == 0;
            result.DarkWindows = ReadInt(key, "SystemUsesLightTheme", 1) == 0;
            result.Transparency = ReadInt(key, "EnableTransparency", 1) == 1;
        }

        using (var key = Registry.CurrentUser.OpenSubKey(ExplorerKey))
            result.TaskbarLeft = ReadInt(key, "TaskbarAl", 1) == 0;
        return result;
    }

    public static string? Apply(PersonalizationChoices choices)
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(ThemeKey, true))
            {
                WriteBool(key, "AppsUseLightTheme", choices.DarkApps, false);
                WriteBool(key, "SystemUsesLightTheme", choices.DarkWindows, false);
                WriteBool(key, "EnableTransparency", choices.Transparency, true);
            }

            using (var key = Registry.CurrentUser.CreateSubKey(ExplorerKey, true))
                WriteBool(key, "TaskbarAl", choices.TaskbarLeft, false);

            WindowsActions.NotifyShellSettingsChanged();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    //keeps the tiny Preferences block beside the values it represents
    public static void WriteRecipe(StringBuilder text, PersonalizationChoices choices)
    {
        if (choices.Count == 0) return;
        text.AppendLine();
        text.AppendLine("[Preferences]");
        if (choices.DarkApps.HasValue)
            text.AppendLine("AppsTheme=" + (choices.DarkApps == true ? "Dark" : "Light"));
        if (choices.DarkWindows.HasValue)
            text.AppendLine("WindowsTheme=" + (choices.DarkWindows == true ? "Dark" : "Light"));
        if (choices.TaskbarLeft.HasValue)
            text.AppendLine("TaskbarAlignment=" + (choices.TaskbarLeft == true ? "Left" : "Center"));
        if (choices.Transparency.HasValue)
            text.AppendLine("Transparency=" + (choices.Transparency == true ? "true" : "false"));
        if (choices.DefaultBrowserId.Length > 0)
            text.AppendLine("DefaultBrowser=" + choices.DefaultBrowserId);
    }

    public static PersonalizationChoices? ReadRecipeChanges(Dictionary<string, string>? values)
    {
        if (values == null) return null;
        var choices = new PersonalizationChoices
        {
            DarkApps = ReadChoice(values, "AppsTheme", "Dark", "Light"),
            DarkWindows = ReadChoice(values, "WindowsTheme", "Dark", "Light"),
            TaskbarLeft = ReadChoice(values, "TaskbarAlignment", "Left", "Center"),
            Transparency = ReadBoolean(values, "Transparency"),
            DefaultBrowserId = values.TryGetValue("DefaultBrowser", out var browserId)
                ? browserId : ""
        };

        //only the preferences chosen during export are compared with this pc
        var current = Read();
        if (choices.DarkApps == current.DarkApps) choices.DarkApps = null;
        if (choices.DarkWindows == current.DarkWindows) choices.DarkWindows = null;
        if (choices.TaskbarLeft == current.TaskbarLeft) choices.TaskbarLeft = null;
        if (choices.Transparency == current.Transparency) choices.Transparency = null;
        return choices.Count == 0 ? null : choices;
    }

    private static bool? ReadChoice(Dictionary<string, string> values, string key,
        string trueValue, string falseValue)
    {
        if (!values.TryGetValue(key, out var value)) return null;
        if (value.Equals(trueValue, StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals(falseValue, StringComparison.OrdinalIgnoreCase)) return false;
        throw InvalidRecipeValue(key, value);
    }

    private static bool? ReadBoolean(Dictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value)) return null;
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1") return true;
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0") return false;
        throw InvalidRecipeValue(key, value);
    }

    private static InvalidDataException InvalidRecipeValue(string key, string value) =>
        new InvalidDataException(Loc.Format("Recipe_InvalidPreference", key, value));

    //all four preferences go through here, so this is the one place the log has to cover
    private static void WriteBool(RegistryKey key, string name, bool? value, bool trueValue)
    {
        if (!value.HasValue) return;
        var number = (value.Value == trueValue ? 1 : 0).ToString(CultureInfo.InvariantCulture);
        var before = key.GetValue(name)?.ToString();
        key.SetValue(name, value.Value == trueValue ? 1 : 0, RegistryValueKind.DWord);
        ChangeLog.Add("Preference", name, ChangeLog.Change(before, number));
    }

    private static int ReadInt(RegistryKey? key, string name, int fallback)
    {
        try { return Convert.ToInt32(key?.GetValue(name, fallback)); }
        catch { return fallback; }
    }
}
