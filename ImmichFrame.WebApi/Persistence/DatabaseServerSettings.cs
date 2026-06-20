using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ImmichFrame.WebApi.Persistence;

/// <summary>
/// Database-backed <see cref="IServerSettings"/>. Holds an immutable in-memory snapshot of the
/// configuration that is (re)loaded from the database. For Phase 0 the snapshot is loaded once at
/// startup, preserving the legacy "load config once" semantics. The atomic <see cref="Load"/> swap
/// is the seam that later phases use to apply configuration changes without a restart.
/// </summary>
public class DatabaseServerSettings : IServerSettings
{
    private volatile Snapshot _snapshot = new(new GeneralSettingsEntity(), Array.Empty<AccountEntity>());

    public IEnumerable<IAccountSettings> Accounts => _snapshot.Accounts;
    public IGeneralSettings GeneralSettings => _snapshot.General;

    public void Validate()
    {
        GeneralSettings.Validate();
        foreach (var account in Accounts)
        {
            account.ValidateAndInitialize();
        }
    }

    /// <summary>Reloads the in-memory snapshot from the database and swaps it in atomically.</summary>
    public void Load(AppDbContext db)
    {
        var general = db.GeneralSettings.AsNoTracking().OrderBy(g => g.Id).FirstOrDefault() ?? new GeneralSettingsEntity();
        var accounts = db.Accounts.AsNoTracking().ToList();
        _snapshot = new Snapshot(general, accounts);
    }

    private sealed record Snapshot(GeneralSettingsEntity General, IReadOnlyList<AccountEntity> Accounts);
}
