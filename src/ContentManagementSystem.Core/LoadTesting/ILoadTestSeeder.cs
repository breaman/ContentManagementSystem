namespace ContentManagementSystem.Core.LoadTesting;

/// <summary>
/// Builds the dataset the load tests run against (task P9-12, spec section 25 NFR-9).
/// </summary>
/// <remarks>
/// The rows are written with <c>SqlBulkCopy</c> rather than through the content services, and that
/// is the whole design. Fifty thousand pages created one <c>CreatePageRequest</c> at a time is
/// upwards of a quarter of a million round trips, each one inside its own transaction with its own
/// URL rebuild and audit row; it would take hours and would measure the writer rather than produce
/// a dataset. The cost is that this code has to know what a published page looks like — a draft
/// version, a published version, two routes, a search document — instead of being told by the
/// service that owns it. <c>LoadTestSeederTests</c> is what keeps the two agreeing: it seeds a small
/// dataset and then asks the running application for the pages over HTTP.
/// <para>
/// <strong>The dataset is a scale fixture, not a corpus.</strong> A hundred thousand media rows sit
/// on a couple of dozen distinct blobs, so every row serves real bytes and generates real renditions
/// while the store stays small — but the deduplication the upload pipeline performs is measured
/// against a hash of the bytes, and these rows deliberately carry hashes that do not match theirs.
/// Nothing about deduplication, virus scanning, or upload throughput can be concluded from this
/// data. Everything about query cost, index size, render time, and cache behaviour can.
/// </para>
/// <para>
/// <strong>It is not for production.</strong> The seeder writes with the identity columns forced and
/// reseeds them afterwards, which is safe against a database nothing else is writing to and is not
/// safe against one taking traffic.
/// </para>
/// </remarks>
public interface ILoadTestSeeder
{
    /// <summary>
    /// Seeds the dataset, or reports the one that is already there.
    /// </summary>
    /// <param name="options">The shape to build.</param>
    /// <param name="progress">Told what the seeder is doing, for a command line to print.</param>
    /// <param name="cancellationToken">Token observed while writing.</param>
    /// <returns>What was created, or what was found.</returns>
    /// <remarks>
    /// Running twice does nothing the second time unless <see cref="LoadTestSeedOptions.Reset"/> is
    /// set, which deletes the previous dataset first. There is no top-up: a half-sized dataset that
    /// grew a second half would have two generations of content in it and no way to tell them apart.
    /// </remarks>
    Task<LoadTestSeedReport> SeedAsync(
        LoadTestSeedOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a seeded dataset and everything that hangs off it.
    /// </summary>
    /// <param name="options">Options naming the root to remove.</param>
    /// <param name="progress">Told what is being deleted.</param>
    /// <param name="cancellationToken">Token observed while deleting.</param>
    /// <returns>True when there was a dataset to remove.</returns>
    Task<bool> PurgeAsync(
        LoadTestSeedOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
