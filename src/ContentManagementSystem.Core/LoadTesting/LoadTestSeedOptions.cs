namespace ContentManagementSystem.Core.LoadTesting;

/// <summary>
/// The shape of the load-testing dataset (task P9-12, spec section 25 NFR-9).
/// </summary>
/// <remarks>
/// The defaults are the figures NFR-9 names — fifty thousand pages and a hundred thousand media
/// items — so a run with no arguments produces the dataset the requirement was written about. Every
/// count scales down, which is what lets the same code path be tested against a container in
/// seconds rather than only being exercised the one time somebody seeds a load-test environment.
/// <para>
/// Everything the seeder writes hangs below a single root page and a single media folder, both named
/// by <see cref="RootSlug"/>. That is what makes <see cref="Reset"/> safe to implement and safe to
/// run: the purge deletes a subtree it can identify, never "rows that look generated".
/// </para>
/// </remarks>
public sealed class LoadTestSeedOptions
{
    /// <summary>Total pages to create, including the root and the branch pages above the leaves.</summary>
    public int Pages { get; set; } = 50_000;

    /// <summary>Media items to create.</summary>
    public int MediaItems { get; set; } = 100_000;

    /// <summary>
    /// How many distinct images are actually written to the media store.
    /// </summary>
    /// <remarks>
    /// Every seeded media row points at one of these blobs, so all of them serve real bytes and
    /// generate real renditions, while the store holds megabytes rather than the hundreds of
    /// gigabytes a hundred thousand distinct photographs would. See the remarks on
    /// <see cref="ILoadTestSeeder"/> for what that costs.
    /// </remarks>
    public int DistinctImages { get; set; } = 24;

    /// <summary>Tags to create and spread across the published pages.</summary>
    public int Tags { get; set; } = 200;

    /// <summary>Redirects to leave behind, so the 301 path has something to serve.</summary>
    public int Redirects { get; set; } = 500;

    /// <summary>Share of leaf pages that carry a published version.</summary>
    public double PublishedShare { get; set; } = 0.90;

    /// <summary>Share of published pages whose draft has moved on since it was published.</summary>
    public double EditedShare { get; set; } = 0.15;

    /// <summary>Share of leaf pages that sit in the recycle bin.</summary>
    public double RecycledShare { get; set; } = 0.01;

    /// <summary>
    /// Share of leaf pages authored against the landing template rather than the article one.
    /// </summary>
    /// <remarks>
    /// Landing pages are the ones that carry the shared footer, so this is also the share of the
    /// site a single reusable-content publish has to invalidate. At the default size it is close to
    /// ten thousand pages, which is the figure risk <c>R8</c>'s trigger is stated against.
    /// </remarks>
    public double LandingShare { get; set; } = 0.20;

    /// <summary>
    /// Seed for the generator's randomness, so two runs of the same options produce the same site.
    /// </summary>
    /// <remarks>
    /// A load test whose dataset changes between runs cannot tell a regression from a different
    /// distribution of page sizes, so this is fixed rather than clock-derived.
    /// </remarks>
    public int RandomSeed { get; set; } = 20_260_819;

    /// <summary>Rows per bulk-copy batch.</summary>
    public int BatchSize { get; set; } = 10_000;

    /// <summary>Slug of the root page, and the name of the media folder, everything hangs under.</summary>
    public string RootSlug { get; set; } = "load-test";

    /// <summary>Where to write the manifest k6 reads, or null to skip writing one.</summary>
    public string? ManifestPath { get; set; }

    /// <summary>How many URLs of each kind the manifest carries.</summary>
    public int ManifestSampleSize { get; set; } = 2_000;

    /// <summary>Delete a dataset already under <see cref="RootSlug"/> and seed it again.</summary>
    public bool Reset { get; set; }

    /// <summary>
    /// Throws when the options describe a dataset that cannot be built.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A count is negative or too small to shape.</exception>
    public void Validate()
    {
        // Eight is the smallest tree with a root, sections, and leaves below them — below that the
        // shape the generator produces stops resembling a site at all, and a test asking for three
        // pages would be testing arithmetic rather than the seeder.
        ArgumentOutOfRangeException.ThrowIfLessThan(Pages, 8);
        ArgumentOutOfRangeException.ThrowIfLessThan(MediaItems, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(DistinctImages, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(Tags);
        ArgumentOutOfRangeException.ThrowIfNegative(Redirects);
        ArgumentOutOfRangeException.ThrowIfLessThan(BatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(ManifestSampleSize);
        ArgumentException.ThrowIfNullOrWhiteSpace(RootSlug);

        foreach (var (name, share) in new[]
        {
            (nameof(PublishedShare), PublishedShare),
            (nameof(EditedShare), EditedShare),
            (nameof(RecycledShare), RecycledShare),
            (nameof(LandingShare), LandingShare),
        })
        {
            if (share is < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(name, share, "A share is a fraction between 0 and 1.");
            }
        }
    }
}
