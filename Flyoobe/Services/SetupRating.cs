namespace Flyoobe3.Services;

//one line of the rating: an area, how much of it already matches, and that share as a mark
internal sealed class RatingRow
{
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";        //section key, so the page can open the area itself
    public string Measured { get; set; } = "";   //the plain numbers the mark was made from
    public double Score { get; set; }
}

//turns a finished scan into the marks shown on the rating page
//it only reads the catalog, so it can be deleted without touching anything else
internal static class SetupRating
{
    //shares, never counts: a growing database must not move anyone's mark
    //and 9.9 is the ceiling on purpose, the old Windows index never gave a round top mark either
    private const double Ceiling = 9.9;

    public static List<RatingRow> Rows(SetupCatalog catalog)
    {
        var rows = new List<RatingRow>();
        foreach (var group in catalog.Rules.GroupBy(rule => rule.Category).OrderBy(group => group.Key))
        {
            var applied = group.Count(rule => rule.IsApplied);
            rows.Add(Row(Loc.RuleCategory(group.Key), "rules:" + group.Key, applied, group.Count(),
                Loc.Format("Rules_Match", applied, group.Count())));
        }

        //here an absent app is the good end, so the share counts the ones that are not installed
        var installed = catalog.Bloatware.Count(item => item.IsInstalled);
        if (catalog.Bloatware.Count > 0)
            rows.Add(Row(Loc.Get("Bloatware_Title"), "bloatware",
                catalog.Bloatware.Count - installed, catalog.Bloatware.Count,
                Loc.Format("Overview_RemovalChoices", installed)));
        return rows;
    }

    //the weakest area, not the average: a setup is only as done as the part that was left out
    public static RatingRow? Weakest(List<RatingRow> rows) => rows.OrderBy(row => row.Score).FirstOrDefault();

    //short way in for the overview row, which wants the number and nothing else
    public static double BaseScore(SetupCatalog catalog) => Weakest(Rows(catalog))?.Score ?? 0;

    private static RatingRow Row(string name, string key, int good, int total, string measured) => new RatingRow
    {
        Name = name,
        Key = key,
        Measured = measured,
        Score = total == 0 ? Ceiling : Math.Round(good * Ceiling / total, 1)
    };
}
