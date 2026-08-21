using ContentManagementSystem.Shared.Contracts.Appearance;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// A site stylesheet with a history, for the gates that render its editor (task P10-11).
/// </summary>
/// <remarks>
/// Carries the states the screen has to draw rather than an empty one: a published copy, unpublished
/// changes on top of it, two revisions to revert between, and a diagnostic. A fixture with nothing
/// in it would leave the gate judging a screen consisting of an empty text box, which is the failure
/// mode every accessibility gate has when its fixture is too polite.
/// </remarks>
internal sealed class FakeSiteStylesheetClient : ISiteStylesheetClient
{
    /// <summary>The published stylesheet, as the fixture holds it.</summary>
    public const string PublishedCss = ".cms-page { --brand: #0a5; }";

    /// <summary>The draft, deliberately different so the screen shows unpublished changes.</summary>
    public const string DraftCss = ".cms-page { --brand: #0a5; padding-block: 2rem; }";

    /// <summary>The note on the newest revision, which the gate asserts has rendered.</summary>
    public const string RevisionNote = "Brand refresh";

    /// <inheritdoc />
    public Task<SiteStylesheetDetail?> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<SiteStylesheetDetail?>(new SiteStylesheetDetail(
            DraftCss,
            PublishedCss,
            HasUnpublishedChanges: true,
            DraftByteLength: DraftCss.Length,
            PublishedByteLength: PublishedCss.Length,
            MaxBytes: 262144,
            PublishedOn: new DateTimeOffset(2026, 8, 14, 9, 30, 0, TimeSpan.Zero),
            PublishedBy: "ada",
            Diagnostics: [],
            RowVersion: "AAAAAAAAB9E="));

    /// <inheritdoc />
    public Task<CssValidationReport?> ValidateAsync(
        string css,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CssValidationReport?>(new CssValidationReport(true, css.Length, 262144, []));

    /// <inheritdoc />
    public Task<StructureClientResult<SiteStylesheetDetail>> SaveDraftAsync(
        string css,
        string? rowVersion,
        CancellationToken cancellationToken = default) =>
        Unchanged();

    /// <inheritdoc />
    public Task<StructureClientResult<SiteStylesheetDetail>> PublishAsync(
        string? note,
        CancellationToken cancellationToken = default) =>
        Unchanged();

    /// <inheritdoc />
    public Task<StructureClientResult<SiteStylesheetDetail>> RevertAsync(
        int? revisionId,
        bool copyToDraft,
        CancellationToken cancellationToken = default) =>
        Unchanged();

    /// <inheritdoc />
    public Task<IReadOnlyList<SiteStylesheetRevisionSummary>> GetRevisionsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SiteStylesheetRevisionSummary>>(
        [
            new SiteStylesheetRevisionSummary(
                2,
                PublishedCss.Length,
                RevisionNote,
                new DateTimeOffset(2026, 8, 14, 9, 30, 0, TimeSpan.Zero),
                "ada",
                IsCurrent: true),
            new SiteStylesheetRevisionSummary(
                1,
                18,
                Note: null,
                new DateTimeOffset(2026, 7, 2, 16, 5, 0, TimeSpan.Zero),
                "grace",
                IsCurrent: false),
        ]);

    /// <inheritdoc />
    public Task<string?> GetRevisionCssAsync(
        int revisionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(PublishedCss);

    private async Task<StructureClientResult<SiteStylesheetDetail>> Unchanged() =>
        StructureClientResult<SiteStylesheetDetail>.Success((await GetAsync())!);
}
