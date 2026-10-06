using Flyoobe3.Models;
using Microsoft.Win32;

namespace Flyoobe3.Services;

//reads the browsers registered in Windows and matches them to the stable winget ids from Apps.ini
//it can open the correct Default Apps page, but the final choice deliberately stays with Windows
internal static class BrowserService
{
    private const string RegisteredAppsKey = @"SOFTWARE\RegisteredApplications";
    private const string HttpsChoiceKey = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice";

    public static List<RegisteredBrowser> FindInstalled(IEnumerable<AppItem> apps)
    {
        var browsers = apps.Where(IsBrowser).ToList();
        var result = new List<RegisteredBrowser>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ReadRegistered(Registry.CurrentUser, true, browsers, result, seen);
        ReadRegistered(Registry.LocalMachine, false, browsers, result, seen);
        return result.OrderBy(item => item.Name).ToList();
    }

    public static string ReadDefaultBrowserId(IEnumerable<AppItem> apps)
    {
        using var choice = Registry.CurrentUser.OpenSubKey(HttpsChoiceKey);
        var progId = choice?.GetValue("ProgId")?.ToString();
        if (progId == null || progId.Length == 0) return "";

        var current = FindInstalled(apps)
            .FirstOrDefault(item => progId.Equals(item.HttpsProgId, StringComparison.OrdinalIgnoreCase));
        return current?.App.WingetId ?? "";
    }

    public static bool IsInstalled(string wingetId, IEnumerable<AppItem> apps) =>
        FindInstalled(apps).Any(item => item.App.WingetId.Equals(wingetId, StringComparison.OrdinalIgnoreCase));

    public static string? OpenDefaultApps(string wingetId, IEnumerable<AppItem> apps)
    {
        try
        {
            var browser = FindInstalled(apps).FirstOrDefault(item =>
                item.App.WingetId.Equals(wingetId, StringComparison.OrdinalIgnoreCase));
            WindowsActions.OpenDefaultApps(browser?.RegisteredName, browser?.IsUserRegistration == true);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    private static void ReadRegistered(RegistryKey root, bool isUserRegistration, List<AppItem> browsers,
        List<RegisteredBrowser> result, HashSet<string> seen)
    {
        using var registered = root.OpenSubKey(RegisteredAppsKey);
        if (registered == null) return;

        foreach (var registeredName in registered.GetValueNames())
        {
            var app = browsers.FirstOrDefault(item => LooksLikeBrowser(registeredName, item.Name));
            if (app == null || !seen.Add(registeredName)) continue;

            var capabilitiesPath = registered.GetValue(registeredName)?.ToString();
            using var urls = string.IsNullOrWhiteSpace(capabilitiesPath)
                ? null : root.OpenSubKey(capabilitiesPath + @"\URLAssociations");
            result.Add(new RegisteredBrowser(app, registeredName, isUserRegistration,
                urls?.GetValue("https")?.ToString() ?? ""));
        }
    }

    private static bool IsBrowser(AppItem item) =>
        item.Category.Equals("Browsers", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeBrowser(string registeredName, string catalogName)
    {
        var words = catalogName.Replace(" Browser", "")
            .Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Any(word => word.Length >= 4 &&
            registeredName.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}

//keeps one friendly Apps.ini item together with the technical registration Windows reported
internal sealed class RegisteredBrowser
{
    public AppItem App { get; }
    public string Name => App.Name;
    public string RegisteredName { get; }
    public bool IsUserRegistration { get; }
    public string HttpsProgId { get; }

    public RegisteredBrowser(AppItem app, string registeredName, bool isUserRegistration, string httpsProgId)
    {
        App = app;
        RegisteredName = registeredName;
        IsUserRegistration = isUserRegistration;
        HttpsProgId = httpsProgId;
    }

    public override string ToString() => Name;
}
