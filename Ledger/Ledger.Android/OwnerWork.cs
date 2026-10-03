using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Index.Domain.Household;

namespace Index.Android;

// The device-side half of PassphraseRestoreFlow (Index.Domain, where the order is tested): the vault
// restore and the workers it brings back to life. Registered as a singleton; VaultSync is scoped, so each
// restore takes its own scope, the way MainActivity's launch does.
public sealed class OwnerWork : IOwnerWorkScheduler
{
    private readonly IServiceScopeFactory scopes;

    public OwnerWork(IServiceScopeFactory scopes)
    {
        this.scopes = scopes;
    }

    public bool IsConfigured => SettingsStore.IsConfigured;

    // Off the calling (UI) thread: opening the vault derives a key from the passphrase, which is slow on
    // purpose.
    public Task RestoreAsync(CancellationToken cancellationToken) => Task.Run(async () =>
    {
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<VaultSync>().RestoreAsync(cancellationToken);
    }, cancellationToken);

    public void ScheduleRefresh() => RefreshWorker.Schedule(Platform.AppContext);

    public void CancelRefresh() => RefreshWorker.CancelAll(Platform.AppContext);

    public void RunRefreshNow() => RefreshWorker.RunNow(Platform.AppContext);

    public void ScheduleWorksheet() => WorksheetDeliveryWorker.Schedule(Platform.AppContext);

    public void CancelWorksheet() => WorksheetDeliveryWorker.CancelAll(Platform.AppContext);
}
