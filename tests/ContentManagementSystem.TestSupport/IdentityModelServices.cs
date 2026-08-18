using ContentManagementSystem.Data.Common;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.TestSupport;

/// <summary>
/// The minimal service provider a test-owned <c>ApplicationDbContext</c> needs in order to build
/// the same model the migrations were generated from.
/// </summary>
/// <remarks>
/// <c>IdentityDbContext</c> reads the store schema version out of the application service provider
/// while building its model. A context built without one gets an older Identity schema, and EF then
/// reports the model as having pending changes. Worse, the two shapes share a
/// <c>DbContext</c> type: once both have been built in a single process, migration-time validation
/// sees the model differ between builds and fails with
/// <c>PendingModelChangesWarning — "the model … changes each time it is built"</c>. The suite that
/// fails is whichever one migrates, not the one that built the odd model, which makes the failure
/// read as a missing migration.
/// <para>
/// So this lives in one place and every test-owned context passes it to
/// <c>UseApplicationServiceProvider</c> — including the ones that never open a connection. The web
/// host configures the same value in <c>AddIdentityCore</c>; both read it from
/// <see cref="IdentitySchema"/> so they cannot drift.
/// </para>
/// </remarks>
public static class IdentityModelServices
{
    /// <summary>Gets the provider to hand to <c>UseApplicationServiceProvider</c>.</summary>
    public static IServiceProvider Instance { get; } = new ServiceCollection()
        .Configure<IdentityOptions>(options => options.Stores.SchemaVersion = IdentitySchema.Version)
        .BuildServiceProvider();
}
