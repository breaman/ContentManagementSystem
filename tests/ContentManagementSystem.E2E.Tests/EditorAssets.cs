using Microsoft.Playwright;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// Serves the built editor bundles to a Playwright page from a synthetic origin (task P6-31a).
/// </summary>
/// <remarks>
/// The bundles are ES modules that import one another, and a module graph cannot be loaded from
/// <c>file://</c> — the browser refuses the cross-origin import and the failure looks like an editor
/// that simply never appears. Routing a synthetic <c>https://</c> origin at the build output gives
/// the page a real origin, real module resolution, and somewhere to attach a
/// <c>Content-Security-Policy</c> header, which is the point of the exercise: a missing style nonce
/// fails <em>silently</em>, so the only way to catch it is to be strict on purpose and look.
/// </remarks>
internal static class EditorAssets
{
    /// <summary>The origin the test page is served from.</summary>
    public const string Origin = "https://cms.localhost";

    /// <summary>The per-request nonce the host page issues, as <c>App.razor</c> issues one.</summary>
    public const string Nonce = "dGVzdC1ub25jZS0xMjM0NTY3OA==";

    /// <summary>Where <c>npm run editors</c> writes the bundles.</summary>
    /// <exception cref="InvalidOperationException">The bundles have not been built.</exception>
    public static string BundleDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                directory = directory.Parent;
            }

            if (directory is null)
            {
                throw new InvalidOperationException("The repository root could not be located.");
            }

            var bundles = Path.Combine(
                directory.FullName,
                "src",
                "ContentManagementSystem.Server",
                "wwwroot",
                "js");

            if (!File.Exists(Path.Combine(bundles, "cms-source-editor.js")))
            {
                throw new InvalidOperationException(
                    $"The editor bundles are missing from '{bundles}'. They are built by the " +
                    "BuildEditorBundles target — build the server project first.");
            }

            return bundles;
        }
    }

    /// <summary>
    /// Points a page's requests at the built bundles, and serves it a host page.
    /// </summary>
    /// <param name="page">The page to route.</param>
    /// <param name="body">Markup for the document body, including the module script.</param>
    /// <param name="strictStyles">
    /// Whether to send the strict <c>style-src</c> of spec section 20.5. True is the interesting
    /// case: CodeMirror injects its theme as a <c>style</c> element at runtime, so a policy without
    /// the nonce leaves the editor working and unstyled — which is what D13 calls the load-bearing
    /// finding and what this harness exists to be able to see.
    /// </param>
    /// <param name="withNonceMeta">
    /// Whether the host page carries the <c>csp-nonce</c> meta tag. False reproduces the deployment
    /// mistake D13 warns about — the policy on, the bundle fine, and nobody having wired the tag.
    /// </param>
    public static async Task ServeAsync(
        IPage page,
        string body,
        bool strictStyles = true,
        bool withNonceMeta = true)
    {
        ArgumentNullException.ThrowIfNull(page);

        var bundles = BundleDirectory;

        await page.RouteAsync($"{Origin}/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;

            if (path is "/" or "/index.html")
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    ContentType = "text/html; charset=utf-8",
                    Headers = strictStyles
                        ? new Dictionary<string, string>
                        {
                            // Exactly the shape D13 describes: inline styles are refused unless they
                            // carry this request's nonce.
                            ["Content-Security-Policy"] =
                                $"default-src 'self'; script-src 'self'; style-src 'self' 'nonce-{Nonce}'",
                        }
                        : null,
                    Body = Document(body, withNonceMeta),
                });

                return;
            }

            // The stylesheet as well as the scripts: Quill's module adds its own same-origin
            // <link> on first mount (task P6-08), and a 404 there would leave the toolbar unstyled
            // for reasons that have nothing to do with the nonce this test is about.
            var isStylesheet = path.EndsWith(".css", StringComparison.OrdinalIgnoreCase);

            var file = Path.Combine(
                isStylesheet ? Path.Combine(Path.GetDirectoryName(bundles)!, "css") : bundles,
                Path.GetFileName(path));

            if (!File.Exists(file))
            {
                await route.FulfillAsync(new RouteFulfillOptions { Status = 404 });

                return;
            }

            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = isStylesheet ? "text/css; charset=utf-8" : "text/javascript; charset=utf-8",
                Body = await File.ReadAllTextAsync(file),
            });
        });
    }

    /// <summary>The host page, carrying the nonce the way the backoffice's own host page carries it.</summary>
    private static string Document(string body, bool withNonceMeta) =>
        $"""
         <!doctype html>
         <html lang="en">
         <head>
           <meta charset="utf-8">
           {(withNonceMeta ? $"""<meta name="csp-nonce" content="{Nonce}">""" : string.Empty)}
           <title>Editor teardown</title>
         </head>
         <body>{body}</body>
         </html>
         """;
}
