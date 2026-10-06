namespace Flyoobe3.Models;

//contains the optional choices picked for one recipe export
internal sealed class RecipeExportPlan
{
    public PersonalizationChoices Preferences { get; } = new PersonalizationChoices();
    public List<string> AppIds { get; } = new List<string>();
    public List<string> BloatwareIds { get; } = new List<string>();
    public Dictionary<string, string> ActionChoices { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
