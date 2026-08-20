using System.Globalization;

using ContentManagementSystem.Core.LoadTesting;

namespace ContentManagementSystem.Server.Cli;

/// <summary>
/// The <c>cms seed</c> verbs, which build and remove the load-testing dataset (task P9-12).
/// </summary>
/// <remarks>
/// <code>
/// dotnet run -- cms seed load                    # the NFR-9 dataset: 50,000 pages, 100,000 media
/// dotnet run -- cms seed load --pages 500        # a small one, for trying the tooling out
/// dotnet run -- cms seed load --reset            # rebuild it from scratch
/// dotnet run -- cms seed purge                   # take it away again
/// </code>
/// <para>
/// The command refuses to run in the Production environment unless <c>--force</c> is given. Seeding
/// writes half a million rows and reseeds identity counters, which is safe against a database
/// nothing else is writing to and is not safe against one taking traffic — and "the load-test
/// environment is configured as Production" is exactly the situation in which somebody types this
/// against the wrong connection string.
/// </para>
/// </remarks>
public static class CmsSeedCommand
{
    /// <summary>Second argument that selects these verbs.</summary>
    public const string Verb = "seed";

    /// <summary>Runs a <c>cms seed</c> command.</summary>
    /// <param name="app">The application, built but not started.</param>
    /// <param name="args">The process arguments, beginning with <c>cms seed</c>.</param>
    /// <param name="cancellationToken">Token observed while working.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> RunAsync(
        WebApplication app,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(args);

        var action = args[2].ToLowerInvariant();

        if (action is not ("load" or "purge"))
        {
            PrintUsage();

            return CmsCommandLine.Failed;
        }

        var flags = args[3..];

        if (app.Environment.IsProduction() && !Has(flags, "--force"))
        {
            Console.Error.WriteLine(
                "Refusing to seed: the application is running in the Production environment. " +
                "This writes hundreds of thousands of generated rows and reseeds identity " +
                "counters. Pass --force if this really is a load-test environment.");

            return CmsCommandLine.Failed;
        }

        LoadTestSeedOptions options;

        try
        {
            options = Parse(app, flags);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"  error: {exception.Message}");
            PrintUsage();

            return CmsCommandLine.Failed;
        }

        using var scope = app.Services.CreateScope();

        var seeder = scope.ServiceProvider.GetRequiredService<ILoadTestSeeder>();
        var progress = new Progress<string>(message => Console.WriteLine($"  {message}"));

        if (action is "purge")
        {
            var removed = await seeder.PurgeAsync(options, progress, cancellationToken);

            Console.WriteLine(removed
                ? $"Removed the dataset under '{options.RootSlug}'."
                : $"There was no dataset under '{options.RootSlug}' to remove.");

            return CmsCommandLine.Success;
        }

        var report = await seeder.SeedAsync(options, progress, cancellationToken);

        Print(report);

        return CmsCommandLine.Success;
    }

    /// <summary>Writes the verbs and their flags to standard error.</summary>
    public static void PrintUsage()
    {
        Console.Error.WriteLine("  seed load   build the load-testing dataset (task P9-12)");
        Console.Error.WriteLine("  seed purge  delete it again");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  --pages N      pages to create (default 50000)");
        Console.Error.WriteLine("  --media N      media items to create (default 100000)");
        Console.Error.WriteLine("  --images N     distinct images actually written (default 24)");
        Console.Error.WriteLine("  --tags N       tags to create (default 200)");
        Console.Error.WriteLine("  --redirects N  redirects to leave behind (default 500)");
        Console.Error.WriteLine("  --random N     seed for the generator, so runs repeat");
        Console.Error.WriteLine("  --batch N      rows per bulk-copy batch (default 10000)");
        Console.Error.WriteLine("  --root SLUG    root page slug and media folder name");
        Console.Error.WriteLine("  --manifest P   where to write the k6 manifest");
        Console.Error.WriteLine("  --manifest-sample N  URLs of each kind in it (default 2000)");
        Console.Error.WriteLine("  --reset        delete an existing dataset first");
        Console.Error.WriteLine("  --force        allow the run in the Production environment");
    }

    private static LoadTestSeedOptions Parse(WebApplication app, string[] flags)
    {
        var options = new LoadTestSeedOptions
        {
            Reset = Has(flags, "--reset"),
        };

        if (Value(flags, "--pages") is { } pages) options.Pages = Number(pages, "--pages");
        if (Value(flags, "--media") is { } media) options.MediaItems = Number(media, "--media");
        if (Value(flags, "--images") is { } images) options.DistinctImages = Number(images, "--images");
        if (Value(flags, "--tags") is { } tags) options.Tags = Number(tags, "--tags");
        if (Value(flags, "--redirects") is { } redirects) options.Redirects = Number(redirects, "--redirects");
        if (Value(flags, "--random") is { } random) options.RandomSeed = Number(random, "--random");
        if (Value(flags, "--batch") is { } batch) options.BatchSize = Number(batch, "--batch");
        if (Value(flags, "--root") is { Length: > 0 } root) options.RootSlug = root;

        if (Value(flags, "--manifest-sample") is { } sample)
        {
            options.ManifestSampleSize = Number(sample, "--manifest-sample");
        }

        // Always written, because the scripts read it and a run that forgot to ask for one is a run
        // whose URLs nobody has. The default sits beside the media the same environment writes.
        options.ManifestPath = Value(flags, "--manifest") is { Length: > 0 } manifest
            ? Path.GetFullPath(manifest)
            : Path.Combine(app.Environment.ContentRootPath, "App_Data", "load-test", "manifest.json");

        return options;
    }

    private static bool Has(string[] flags, string name) =>
        Array.Exists(flags, flag => string.Equals(flag, name, StringComparison.OrdinalIgnoreCase));

    private static string? Value(string[] flags, string name)
    {
        var index = Array.FindIndex(
            flags,
            flag => string.Equals(flag, name, StringComparison.OrdinalIgnoreCase));

        if (index < 0) return null;

        if (index + 1 >= flags.Length)
        {
            throw new ArgumentException($"{name} needs a value.");
        }

        return flags[index + 1];
    }

    private static int Number(string value, string name) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new ArgumentException($"{name} takes a whole number, not '{value}'.");

    private static void Print(LoadTestSeedReport report)
    {
        if (report.AlreadySeeded)
        {
            Console.WriteLine(
                $"A dataset is already present under {report.RootUrl}: {report.Pages:N0} pages " +
                $"({report.PublishedPages:N0} published). Pass --reset to rebuild it.");

            return;
        }

        Console.WriteLine($"Seeded in {report.Elapsed.TotalSeconds:N1}s under {report.RootUrl}:");
        Console.WriteLine($"  {report.Pages,10:N0} pages ({report.PublishedPages:N0} published)");
        Console.WriteLine($"  {report.PageVersions,10:N0} page versions");
        Console.WriteLine($"  {report.MediaItems,10:N0} media items over {report.DistinctImages} images");
        Console.WriteLine($"  {report.Tags,10:N0} tags");
        Console.WriteLine($"  {report.Redirects,10:N0} redirects");
        Console.WriteLine($"  {report.SearchDocuments,10:N0} search documents");

        if (report.ManifestPath is { Length: > 0 } manifest)
        {
            Console.WriteLine($"  manifest: {manifest}");
        }
    }
}
