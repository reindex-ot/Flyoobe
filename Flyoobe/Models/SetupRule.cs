namespace Flyoobe3.Models;

//describes one registry value belonging to a setup rule
internal sealed class RegistryEntry
{
    public string Hive { get; set; } = "";
    public string Key { get; set; } = "";
    public string ValueName { get; set; } = "";
    public string ValueType { get; set; } = "DWORD";
    public string RecommendedValue { get; set; } = "";
    public string DefaultValue { get; set; } = "";
}

//one rule loaded from Flyoobe.ini, including its live state
internal sealed class SetupRule
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Catalog_Windows";
    public string Description { get; set; } = "";
    public bool Selected { get; set; }
    public bool RestoreWithRecipe { get; set; }
    public bool RequiresAdmin { get; set; }
    public string Restart { get; set; } = "";
    public bool IsApplied { get; set; }
    public bool IsDefault { get; set; }
    public string CurrentValue { get; set; } = "(not set)";
    public List<RegistryEntry> Entries { get; } = new List<RegistryEntry>();
}
