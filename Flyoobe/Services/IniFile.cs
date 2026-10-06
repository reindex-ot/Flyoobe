using Flyoobe3.Models;

namespace Flyoobe3.Services;

//turns a plain ini file into neutral sections and key/value pairs
//it deliberately knows nothing about tweaks, apps or recipes - those formats are interpreted elsewhere
internal static class IniFile
{
    public static List<IniSection> Read(string path)
    {
        var result = new List<IniSection>();
        if (!File.Exists(path)) return result;

        IniSection? current = null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                current = new IniSection { Name = line.Substring(1, line.Length - 2).Trim() };
                result.Add(current);
                continue;
            }

            if (current == null) continue;
            var equals = line.IndexOf('=');
            if (equals < 1) continue;

            var key = line.Substring(0, equals).Trim();
            var value = StripComment(line.Substring(equals + 1).Trim());
            current.Values[key] = value;
        }
        return result;
    }

    //a whole line may start with ; or #, so a trailing comment accepts both as well
    private static string StripComment(string value)
    {
        var semicolon = value.IndexOf(" ;", StringComparison.Ordinal);
        var hash = value.IndexOf(" #", StringComparison.Ordinal);
        var pos = semicolon < 0 || (hash >= 0 && hash < semicolon) ? hash : semicolon;
        return pos < 0 ? value : value.Substring(0, pos).Trim();
    }
}
