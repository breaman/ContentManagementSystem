using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ContentManagementSystem.Core.Tests.Content;

/// <summary>
/// Compares JSON against a checked-in snapshot file.
/// </summary>
/// <remarks>
/// Hand-rolled rather than pulled from a snapshot-testing package, because what it has to do is
/// small and what it must not do is large: a snapshot that silently rewrites itself when the output
/// changes pins nothing at all. The expected files are reviewed in the diff like any other source,
/// and are regenerated only by an explicit opt-in:
/// <code>
/// CMS_UPDATE_SNAPSHOTS=1 dotnet test tests/ContentManagementSystem.Core.Tests
/// </code>
/// Both sides are canonicalised — parsed and rewritten indented — so that the comparison is about
/// the document and not about how it was formatted.
/// </remarks>
internal static class JsonSnapshot
{
    private const string UpdateVariable = "CMS_UPDATE_SNAPSHOTS";

    /// <summary>
    /// Asserts that JSON matches its snapshot, writing the snapshot when asked to.
    /// </summary>
    /// <param name="actualJson">The JSON produced by the code under test.</param>
    /// <param name="snapshotName">File name under <c>Snapshots/</c>.</param>
    /// <param name="callerFilePath">Supplied by the compiler; locates the snapshot directory.</param>
    public static void Match(
        string actualJson,
        string snapshotName,
        [CallerFilePath] string callerFilePath = "")
    {
        var directory = Path.Combine(Path.GetDirectoryName(callerFilePath)!, "Snapshots");
        var path = Path.Combine(directory, snapshotName);
        var actual = Canonicalize(actualJson);

        if (Environment.GetEnvironmentVariable(UpdateVariable) is "1")
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, actual + Environment.NewLine);

            return;
        }

        File.Exists(path).Should().BeTrue(
            $"the snapshot '{snapshotName}' should be checked in; regenerate it by running the " +
            $"suite with {UpdateVariable}=1 and reviewing the result");

        var expected = Canonicalize(File.ReadAllText(path));

        actual.Should().Be(
            expected,
            $"the payload envelope format is a storage contract (spec section 6.2) — every row ever " +
            $"written reads through it, so a change here is a migration, not an edit. Review the " +
            $"difference, then regenerate with {UpdateVariable}=1 if it is intended.");
    }

    private static string Canonicalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            document.RootElement.WriteTo(writer);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n");
    }
}
