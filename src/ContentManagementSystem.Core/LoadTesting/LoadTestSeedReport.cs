namespace ContentManagementSystem.Core.LoadTesting;

/// <summary>
/// What a seeding run created, and where it left the manifest.
/// </summary>
/// <param name="RootPageId">Identity of the page every seeded page hangs below.</param>
/// <param name="RootUrl">URL of that page, which prefixes every seeded URL.</param>
/// <param name="Pages">Pages written.</param>
/// <param name="PublishedPages">Of those, the ones anonymous requests can reach.</param>
/// <param name="PageVersions">Version rows written, drafts and published together.</param>
/// <param name="MediaItems">Media rows written.</param>
/// <param name="DistinctImages">Distinct blobs behind them.</param>
/// <param name="Tags">Tags written.</param>
/// <param name="Redirects">Redirects written.</param>
/// <param name="SearchDocuments">Search rows written.</param>
/// <param name="Elapsed">How long the run took.</param>
/// <param name="ManifestPath">Where the manifest was written, or null if none was asked for.</param>
/// <param name="AlreadySeeded">
/// True when the run found a dataset already in place and did nothing. The counts then describe
/// what is there rather than what this run wrote.
/// </param>
public sealed record LoadTestSeedReport(
    int RootPageId,
    string RootUrl,
    int Pages,
    int PublishedPages,
    int PageVersions,
    int MediaItems,
    int DistinctImages,
    int Tags,
    int Redirects,
    int SearchDocuments,
    TimeSpan Elapsed,
    string? ManifestPath,
    bool AlreadySeeded);
