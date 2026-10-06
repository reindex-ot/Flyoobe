namespace Flyoobe3.Models;

//holds the portable personal choices carried by a recipe
internal sealed class PersonalizationChoices
{
    public bool? DarkApps { get; set; }
    public bool? DarkWindows { get; set; }
    public bool? TaskbarLeft { get; set; }
    public bool? Transparency { get; set; }
    public string DefaultBrowserId { get; set; } = "";

    public int PreferenceCount
    {
        get
        {
            //app and Windows mode are one theme choice in the ui
            var count = DarkApps.HasValue || DarkWindows.HasValue ? 1 : 0;
            if (TaskbarLeft.HasValue) count++;
            if (Transparency.HasValue) count++;
            return count;
        }
    }

    public int ConfirmationCount => DefaultBrowserId.Length == 0 ? 0 : 1;
    public int Count => PreferenceCount + ConfirmationCount;

    public PersonalizationChoices Clone() => new PersonalizationChoices
    {
        DarkApps = DarkApps,
        DarkWindows = DarkWindows,
        TaskbarLeft = TaskbarLeft,
        Transparency = Transparency,
        DefaultBrowserId = DefaultBrowserId
    };
}
