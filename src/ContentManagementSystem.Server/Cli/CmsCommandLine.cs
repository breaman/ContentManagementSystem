using ContentManagementSystem.Core.Structure;

using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.Cli;

/// <summary>
/// The <c>cms</c> verbs the server exposes when it is run as a command rather than as a site
/// (task P1-28, spec section 27.1).
/// </summary>
/// <remarks>
/// <code>
/// dotnet run -- cms schema export   # write the database's structure out as files
/// dotnet run -- cms schema diff     # what the files would change; non-zero if anything would
/// dotnet run -- cms schema apply    # apply them
/// </code>
/// <para>
/// <b><c>diff</c> exits non-zero when the files and the database disagree</b>, which is the whole
/// reason it exists: it is the drift check a CI job runs, and a check that always succeeds is not a
/// check. Exit code 2 means "there is pending work", distinct from 1, which means the command
/// itself failed.
/// </para>
/// <para>
/// The verbs run inside the fully built application, so they use exactly the services the site uses.
/// A promotion tool that reimplemented the sync would be a second definition of what "apply" means.
/// </para>
/// </remarks>
public static class CmsCommandLine
{
    /// <summary>First argument that hands control to this class instead of the web host.</summary>
    public const string Verb = "cms";

    /// <summary>Exit code for a command that completed and found nothing to do.</summary>
    public const int Success = 0;

    /// <summary>Exit code for a command that could not run.</summary>
    public const int Failed = 1;

    /// <summary>Exit code for a <c>diff</c> that found pending work.</summary>
    public const int Drift = 2;

    /// <summary>Whether these arguments are for the CLI rather than for the web host.</summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>True when the first argument is <c>cms</c>.</returns>
    public static bool Handles(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Verb, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs a <c>cms</c> command against a built application.
    /// </summary>
    /// <param name="app">The application, built but not started.</param>
    /// <param name="args">The process arguments, beginning with <c>cms</c>.</param>
    /// <param name="cancellationToken">Token observed while working.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> RunAsync(
        WebApplication app,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length < 3 || !string.Equals(args[1], "schema", StringComparison.OrdinalIgnoreCase))
        {
            return Usage();
        }

        using var scope = app.Services.CreateScope();

        var sync = scope.ServiceProvider.GetRequiredService<ISchemaSyncService>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<SchemaSyncOptions>>().Value;
        var directory = Directory(app, args, options);

        return args[2].ToLowerInvariant() switch
        {
            "export" => await ExportAsync(sync, directory, cancellationToken),
            "diff" => await DiffAsync(sync, directory, cancellationToken),
            "apply" => await ApplyAsync(sync, directory, cancellationToken),
            _ => Usage(),
        };
    }

    private static async Task<int> ExportAsync(
        ISchemaSyncService sync,
        string directory,
        CancellationToken cancellationToken)
    {
        var written = await sync.ExportAsync(directory, cancellationToken);

        Console.WriteLine($"Exported {written.Count} file(s) to {directory}");

        foreach (var path in written)
        {
            Console.WriteLine($"  {Path.GetFileName(path)}");
        }

        return Success;
    }

    private static async Task<int> DiffAsync(
        ISchemaSyncService sync,
        string directory,
        CancellationToken cancellationToken)
    {
        var report = await sync.DiffAsync(directory, cancellationToken);

        Print(report, directory);

        if (report.Errors.Count > 0) return Failed;

        // A refusal is drift too. The file asks for something that will never be applied, so leaving
        // it in the repository means every future run reports it — better to fail the build now.
        return report.HasPendingWork || report.HasProblems ? Drift : Success;
    }

    private static async Task<int> ApplyAsync(
        ISchemaSyncService sync,
        string directory,
        CancellationToken cancellationToken)
    {
        var report = await sync.ApplyAsync(directory, cancellationToken);

        Print(report, directory);

        return report.Errors.Count > 0 ? Failed : Success;
    }

    private static void Print(SchemaSyncReport report, string directory)
    {
        Console.WriteLine($"{report.FilesRead} file(s) read from {directory}");

        foreach (var error in report.Errors)
        {
            Console.Error.WriteLine($"  error: {error}");
        }

        foreach (var change in report.Changes)
        {
            var marker = change.Change switch
            {
                SchemaChangeKind.Created => "+",
                SchemaChangeKind.SlotAdded => "+",
                SchemaChangeKind.SlotUpdated => "~",
                SchemaChangeKind.Refused => "!",
                _ => " ",
            };

            Console.WriteLine($"  {marker} {change.Kind} {change.Key}: {change.Detail}");
        }

        if (!report.HasPendingWork && !report.HasProblems)
        {
            Console.WriteLine("  database matches the files");
        }
    }

    /// <summary>
    /// Works out which directory to use.
    /// </summary>
    /// <remarks>
    /// A trailing path argument wins, so a developer can export somewhere scratch before overwriting
    /// what is committed; otherwise the configured directory under the content root, which is the
    /// same one the startup pass reads.
    /// </remarks>
    private static string Directory(WebApplication app, string[] args, SchemaSyncOptions options) =>
        args.Length > 3 && !string.IsNullOrWhiteSpace(args[3])
            ? Path.GetFullPath(args[3])
            : Path.Combine(app.Environment.ContentRootPath, options.Directory);

    private static int Usage()
    {
        Console.Error.WriteLine("usage: cms schema export|diff|apply [directory]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  export  write the database's templates, block types, and");
        Console.Error.WriteLine("          compositions out as JSON files");
        Console.Error.WriteLine("  diff    report what the files would change; exit code 2 if");
        Console.Error.WriteLine("          anything would, for use as a CI drift check");
        Console.Error.WriteLine("  apply   apply the files, additively and non-destructively");

        return Failed;
    }
}
