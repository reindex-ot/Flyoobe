using Flyoobe3.Models;
using Flyoobe3.Features.SetupActions;

namespace Flyoobe3.Services;

//executes an already parsed recipe in one predictable order and stops at the first error
//it coordinates the other services but does not understand the recipe file format itself
internal sealed class RecipeRunner
{
    private readonly RegistryService _registry;
    private readonly PackageService _packages;

    public RecipeRunner(RegistryService registry, PackageService packages)
    {
        _registry = registry;
        _packages = packages;
    }

    public async Task<string?> ApplyAsync(SetupCatalog catalog, IProgress<string> progress)
    {
        //the order is deliberate: system choices, personal choices, apps, cleanup, Windows confirmation
        var rules = catalog.Rules.Where(item => item.Selected).ToList();
        var defaults = catalog.Rules.Where(item => !item.Selected && item.RestoreWithRecipe).ToList();
        var apps = catalog.Apps.Where(item => item.Selected).ToList();
        var bloatware = catalog.Bloatware.Where(item => item.Selected).ToList();
        var preferences = catalog.RecipePreferences;
        var actions = catalog.Actions.Where(item => item.Selected && item.RecipeAllowed).ToList();

        var actionError = await SetupActionService.RunAsync(actions, SetupActionPhase.Prepare, progress);
        if (actionError != null) return actionError;

        if (rules.Count > 0)
        {
            progress.Report(Loc.Format("Recipe_ApplyingSettings", rules.Count));
            var error = await _registry.ApplyAsync(rules);
            if (error != null) return error;
        }

        if (defaults.Count > 0)
        {
            progress.Report(Loc.Format("Recipe_RestoringDefaults", defaults.Count));
            var error = await _registry.RestoreAsync(defaults);
            if (error != null) return error;
        }

        if (preferences?.PreferenceCount > 0)
        {
            progress.Report(Loc.Format("Recipe_ApplyingPreferences", preferences.PreferenceCount));
            var error = PersonalizationService.Apply(preferences);
            if (error != null) return error;
        }

        if (apps.Count > 0)
        {
            var error = await _packages.InstallAsync(apps, progress);
            if (error != null) return error;
        }

        if (bloatware.Count > 0)
        {
            progress.Report(Loc.Get("Recipe_CheckingApps"));
            var error = await _packages.ScanBloatwareAsync(bloatware);
            if (error != null) return error;

            var installed = bloatware.Where(item => item.IsInstalled).ToList();
            error = await _packages.RemoveAsync(installed, progress);
            if (error != null) return error;
        }

        actionError = await SetupActionService.RunAsync(actions, SetupActionPhase.Finish, progress);
        if (actionError != null) return actionError;

        if (preferences?.ConfirmationCount > 0)
        {
            var browser = catalog.Apps.First(item => item.WingetId.Equals(
                preferences.DefaultBrowserId, StringComparison.OrdinalIgnoreCase));
            progress.Report(Loc.Format("Recipe_OpeningBrowserConfirmation", browser.Name));
            var error = BrowserService.OpenDefaultApps(preferences.DefaultBrowserId, catalog.Apps);
            if (error != null) return error;
        }

        return null;
    }
}
