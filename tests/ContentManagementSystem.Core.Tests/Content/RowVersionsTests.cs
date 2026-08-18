using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ContentManagementSystem.Core.Tests.Content;

/// <summary>
/// How a draft save is guarded against a concurrent one (task P2-24, spec section 11.8).
/// </summary>
/// <remarks>
/// The context here is never connected to anything. Attaching an entity and reading its change
/// tracker entry needs a model, not a database, and what is under test is precisely the decision
/// made <em>before</em> any statement is sent: whether the caller's token becomes the predicate of
/// the <c>UPDATE</c>, is rejected, or is absent.
/// <para>
/// The race itself — two saves, one winner, a 409 carrying both payloads — is asserted against real
/// SQL Server by <c>PageSchemaTests</c> and <c>PageApiTests</c>. It has to be: only the database can
/// arbitrate it. What those suites cannot show is that a <em>malformed</em> token is refused rather
/// than quietly treated as no precondition at all, because both spellings of that bug produce a
/// successful save on an uncontended row.
/// </para>
/// </remarks>
public class RowVersionsTests
{
    private static readonly byte[] Stored = [1, 2, 3, 4, 5, 6, 7, 8];

    [Test]
    public void ATokenTheCallerSuppliedBecomesTheOriginalValueTheUpdateIsJudgedAgainst()
    {
        using var context = Context();
        var entry = Attach(context, out var draft);
        var held = new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 };

        RowVersions.TryApply(entry, Convert.ToBase64String(held)).Should().BeTrue();

        // Set as the original value and never compared in code. A comparison would check the token
        // against what this request has just read, leaving the window between that read and the
        // write — the window two editors saving at once occupy — completely unguarded.
        entry.Property(version => version.RowVersion).OriginalValue.Should().Equal(held);
        entry.Property(version => version.RowVersion).CurrentValue.Should().Equal(Stored);
        draft.RowVersion.Should().Equal(Stored, "the entity itself is not rewritten");
    }

    [Test]
    public void NoTokenLeavesTheWriteUnconditional()
    {
        using var context = Context();
        var entry = Attach(context, out _);

        // Null rather than false: absent and unreadable are different answers, and the endpoint
        // layer decides which of them is acceptable. The draft save insists (428); the metadata
        // patch does not, because two patches naming different members merge rather than collide.
        RowVersions.TryApply(entry, null).Should().BeNull();
        RowVersions.TryApply(entry, string.Empty).Should().BeNull();
        RowVersions.TryApply(entry, "   ").Should().BeNull();

        entry.Property(version => version.RowVersion).OriginalValue.Should().Equal(Stored);
    }

    [Test]
    [Arguments("not base64 at all")]
    [Arguments("!!!!")]
    [Arguments("AQIDBA")]
    [Arguments("AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=")]
    public void ATokenThisServerCouldNotHaveIssuedIsRefusedAndChangesNothing(string supplied)
    {
        using var context = Context();
        var entry = Attach(context, out _);

        // The last case is the one worth spelling out: a token longer than a rowversion is refused
        // rather than truncated to its first eight bytes, which would have matched.
        RowVersions.TryApply(entry, supplied).Should().BeFalse();

        // And nothing was applied on the way to refusing. Treating garbage as "no precondition"
        // would turn a client's encoding bug into the unguarded overwrite the token exists to stop.
        entry.Property(version => version.RowVersion).OriginalValue.Should().Equal(Stored);
    }

    [Test]
    public void TheTokenTheApiHandsOutIsOneItWillAcceptBack()
    {
        using var context = Context();
        var entry = Attach(context, out var draft);

        // The round trip an editor actually makes: read the draft, hold its ETag, save with it.
        // Both halves are Convert.To/FromBase64String, and pinning that here is what stops one of
        // them moving to Base64Url for tidiness and refusing every token in flight.
        RowVersions.TryApply(entry, Convert.ToBase64String(draft.RowVersion)).Should().BeTrue();

        entry.Property(version => version.RowVersion).OriginalValue.Should().Equal(Stored);
    }

    [Test]
    public void AVersionWithNoRowVersionYetStillTakesAToken()
    {
        using var context = Context();
        var draft = new PageVersion { Id = 7, PageId = 3, VersionNumber = 1, RowVersion = null! };
        var entry = context.Attach(draft);
        var held = new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 };

        // An unsaved or hand-built row has no token of its own. The caller's still has to be applied,
        // or the guard would silently switch off for exactly the row nobody has looked at.
        RowVersions.TryApply(entry, Convert.ToBase64String(held)).Should().BeTrue();

        entry.Property(version => version.RowVersion).OriginalValue.Should().Equal(held);
    }

    /// <summary>A context with a built model and no connection behind it.</summary>
    private static ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=none;Database=none;Trusted_Connection=False")
            .Options);

    /// <summary>Attaches a draft carrying <see cref="Stored"/> as the row version it was read with.</summary>
    private static EntityEntry<PageVersion> Attach(
        ApplicationDbContext context,
        out PageVersion draft)
    {
        draft = new PageVersion
        {
            Id = 7,
            PageId = 3,
            VersionNumber = 1,
            Status = PageVersionStatus.Draft,
            RowVersion = [.. Stored],
        };

        return context.Attach(draft);
    }
}
