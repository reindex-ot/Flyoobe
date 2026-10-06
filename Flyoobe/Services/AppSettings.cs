using System.Text.Json;

namespace Flyoobe3.Services;

//loads and saves the few settings that belong to Flyoobe itself
//a settings.json beside the exe means portable mode, otherwise I use the user's roaming folder
internal sealed class AppSettings
{
    private static readonly string PortablePath = Path.Combine(AppContext.BaseDirectory, "settings.json");
    private static readonly string RoamingPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Flyoobe", "settings.json");
    private static readonly string SettingsFile = File.Exists(PortablePath) ? PortablePath : RoamingPath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static AppSettings Instance { get; } = Load();

    //static, so it is never part of the saved json
    public static string SettingsPath => SettingsFile;

    public int WindowWidth { get; set; } = 984;
    public int WindowHeight { get; set; } = 641;
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowState { get; set; }
    public string UiLanguage { get; set; } = "";
    public string AiProvider { get; set; } = "Groq";
    public string? GroqApiKey { get; set; }
    public string? OpenAiApiKey { get; set; }
    public string? AnthropicApiKey { get; set; }
    public string? OpenAiCompatibleApiKey { get; set; }
    public string OpenAiCompatibleEndpoint { get; set; } = AppLinks.OpenRouterChat;
    public string OpenAiCompatibleModel { get; set; } = "";
    public bool SetupActionsEnabled { get; set; }

    //off by default, so Flyoobe never writes a file about this pc unless the user asked for it
    public bool ChangeLogEnabled { get; set; }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { }
    }

    private static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), JsonOptions)
                   ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }
}
