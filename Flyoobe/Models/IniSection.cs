namespace Flyoobe3.Models;

//a tiny neutral ini section used by every data catalog
internal sealed class IniSection
{
    public string Name { get; set; } = "";
    public Dictionary<string, string> Values { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string Get(string key, string fallback = "") =>
        Values.TryGetValue(key, out var value) ? value : fallback;
}
