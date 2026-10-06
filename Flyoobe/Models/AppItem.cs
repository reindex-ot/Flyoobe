namespace Flyoobe3.Models;

//an installable app loaded from Apps.ini
internal sealed class AppItem
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Apps";
    public string WingetId { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Selected { get; set; }
    public bool IsInstalled { get; set; }
}

//a removable inbox app loaded from Bloatware.ini
internal sealed class BloatwareItem
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Windows apps";
    public string PackageName { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Selected { get; set; }
    public string? FullPackageName { get; set; }
    public bool IsInstalled => !string.IsNullOrWhiteSpace(FullPackageName);
}
