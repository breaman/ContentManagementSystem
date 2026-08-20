using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContentManagementSystem.Core.LoadTesting;

/// <summary>How much of each thing the seeded dataset holds.</summary>
/// <param name="Pages">Pages written.</param>
/// <param name="PublishedPages">Of those, the ones anonymous requests can reach.</param>
/// <param name="MediaItems">Media rows written.</param>
/// <param name="DistinctImages">Distinct blobs behind them.</param>
/// <param name="Tags">Tags written.</param>
/// <param name="Redirects">Redirects written.</param>
/// <param name="SearchDocuments">Search rows written.</param>
public sealed record LoadTestManifestCounts(
    int Pages,
    int PublishedPages,
    int MediaItems,
    int DistinctImages,
    int Tags,
    int Redirects,
    int SearchDocuments);

/// <summary>
/// What a load-test script needs to know about the dataset it is pointed at.
/// </summary>
/// <param name="GeneratedOn">When the dataset was seeded.</param>
/// <param name="RandomSeed">The seed that produced it, so a run can be reproduced.</param>
/// <param name="RootUrl">The URL every seeded URL sits below.</param>
/// <param name="Counts">Sizes, for a script to assert it is pointed at the dataset it expects.</param>
/// <param name="PublishedUrls">Published pages, sampled across the tree.</param>
/// <param name="LandingUrls">
/// The published pages that carry the shared footer, which are the ones a reusable-content publish
/// has to invalidate.
/// </param>
/// <param name="DeepUrls">The one branch that runs to depth ten.</param>
/// <param name="RedirectUrls">URLs that answer 301.</param>
/// <param name="NotFoundUrls">URLs that answer 404, for measuring the miss path.</param>
/// <param name="TagSlugs">Tag slugs, for the tag listing pages.</param>
/// <param name="FirstMediaId">Identity of the first media row.</param>
/// <param name="MediaCount">How many media rows follow it.</param>
/// <remarks>
/// Image URLs are deliberately absent. They are signed and may be given a lifetime, so a URL
/// written here could expire before the run that reads it — a script gets them the way a browser
/// does, out of the HTML of the page it just fetched.
/// </remarks>
public sealed record LoadTestManifest(
    DateTimeOffset GeneratedOn,
    int RandomSeed,
    string RootUrl,
    LoadTestManifestCounts Counts,
    IReadOnlyList<string> PublishedUrls,
    IReadOnlyList<string> LandingUrls,
    IReadOnlyList<string> DeepUrls,
    IReadOnlyList<string> RedirectUrls,
    IReadOnlyList<string> NotFoundUrls,
    IReadOnlyList<string> TagSlugs,
    int FirstMediaId,
    int MediaCount)
{
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Serializes a manifest.</summary>
    /// <param name="manifest">What to write.</param>
    /// <returns>Indented JSON, as k6 reads it with <c>JSON.parse(open(...))</c>.</returns>
    public static string Write(LoadTestManifest manifest) => JsonSerializer.Serialize(manifest, Format);

    /// <summary>Reads a manifest back.</summary>
    /// <param name="json">What was written.</param>
    /// <returns>The manifest, or null when the text is not one.</returns>
    public static LoadTestManifest? Read(string json) =>
        JsonSerializer.Deserialize<LoadTestManifest>(json, Format);
}
