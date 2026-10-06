using System.Globalization;
using System.Text.Json;

namespace Flyoobe3.Services;

//loads the English fallback plus the selected loose json translation
//it also resolves translated rule names and categories without changing their stable database ids
internal static class Loc
{
    private const string FallbackLocale = "en";
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Localization");
    private static Dictionary<string, string> _fallback = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string> _current = new(StringComparer.OrdinalIgnoreCase);

    public static string CurrentLocale { get; private set; } = FallbackLocale;
    public static string LocalizationFolder => Folder;

    public static void Init(string? locale)
    {
        _fallback = ReadLanguage(FallbackLocale);
        var requested = string.IsNullOrWhiteSpace(locale) ? CultureInfo.CurrentUICulture.Name : locale!.Trim();
        CurrentLocale = FindLocale(requested);
        _current = CurrentLocale.Equals(FallbackLocale, StringComparison.OrdinalIgnoreCase)
            ? _fallback : ReadLanguage(CurrentLocale);
        try
        {
            var culture = CultureInfo.GetCultureInfo(CurrentLocale);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch { }
    }

    public static string Get(string key)
    {
        if (_current.TryGetValue(key, out var value) && value.Length > 0) return value;
        if (_fallback.TryGetValue(key, out value) && value.Length > 0) return value;
        return key;
    }

    //a translation is a loose file anyone can drop in, so a wrong placeholder in one
    //must never take the app down. the key itself is ugly, but it is still running.
    public static string Format(string key, params object[] args)
    {
        try { return string.Format(CultureInfo.CurrentCulture, Get(key), args); }
        catch (FormatException) { return key; }
    }

    //file metadata of the active language only. no english fallback on purpose:
    //falling back here would credit the english file's translator for someone else's work.
    public static string Meta(string key) => _current.TryGetValue(key, out var value) ? value : "";

    public static string RuleName(string name) => GetOrOriginal("Tweak_Name_" + name, name);
    public static string RuleDescription(string name, string description) =>
        GetOrOriginal("Tweak_Description_" + name, description);
    public static string RuleCategory(string category) =>
        GetOrOriginal("Tweak_Category_" + category, category);

    public static IReadOnlyList<LanguageInfo> GetAvailableLanguages()
    {
        var result = new List<LanguageInfo>();
        foreach (var locale in AvailableLocales())
        {
            var values = ReadLanguage(locale);
            var name = values.TryGetValue("_Meta_LanguageDisplayName", out var display) && display.Length > 0
                ? display : locale;
            result.Add(new LanguageInfo(locale, name));
        }
        return result;
    }

    //the file names alone, so nothing is parsed where only the locale matters
    private static List<string> AvailableLocales() =>
        Directory.Exists(Folder)
            ? Directory.GetFiles(Folder, "*.json").OrderBy(path => path)
                .Select(Path.GetFileNameWithoutExtension).ToList()
            : new List<string>();

    private static string GetOrOriginal(string key, string original)
    {
        if (_current.TryGetValue(key, out var value) && value.Length > 0) return value;
        if (_fallback.TryGetValue(key, out value) && value.Length > 0) return value;
        return original;
    }

    private static string FindLocale(string requested)
    {
        var locales = AvailableLocales();
        var exact = locales.FirstOrDefault(item => item.Equals(requested, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;
        var dash = requested.IndexOf('-');
        var neutral = dash > 0 ? requested.Substring(0, dash) : requested;
        return locales.FirstOrDefault(item => item.Equals(neutral, StringComparison.OrdinalIgnoreCase))
               ?? FallbackLocale;
    }

    private static Dictionary<string, string> ReadLanguage(string locale) =>
        ReadFile(Path.Combine(Folder, locale + ".json"));

    private static Dictionary<string, string> ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            return values == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }
}

//one language file as it appears in the settings combo box
internal sealed class LanguageInfo
{
    public LanguageInfo(string locale, string displayName) { Locale = locale; DisplayName = displayName; }
    public string Locale { get; }
    public string DisplayName { get; }
}
