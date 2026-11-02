using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IApplicationPreferencesService
{
    ApplicationPreferences Current { get; }
    bool IsSaving { get; }
    event EventHandler? Changed;
    void Apply(ApplicationPreferences preferences);
    Task SaveAsync(ApplicationPreferences preferences, CancellationToken cancellationToken);
    Task WaitForSaveAsync() => Task.CompletedTask;
}
