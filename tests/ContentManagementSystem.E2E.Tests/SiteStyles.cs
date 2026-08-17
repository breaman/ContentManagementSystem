using Microsoft.Playwright;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// Serves a rendered screen to a Playwright page with the site's own stylesheet (task P6-38).
/// </summary>
/// <remarks>
/// The zoom pass is a measurement of CSS, so it has to be the real CSS: <c>site.css</c> as
/// <c>npm run sass-prod</c> builds it, including Bootstrap's grid and the backoffice layer task
/// P6-40 added. A document with inline styles standing in for it would measure a mock-up.
/// <para>
/// Routed from an origin rather than pushed in with <c>SetContentAsync</c>, because a stylesheet
/// reference has to resolve to something — and because the same route can then serve the fonts and
/// icons the stylesheet asks for without them failing in a way that changes the layout.
/// </para>
/// </remarks>
internal static class SiteStyles
{
    /// <summary>The origin the page is served from.</summary>
    public const string Origin = "https://backoffice.localhost";

    /// <summary>Where <c>npm run sass-prod</c> writes the stylesheet.</summary>
    /// <exception cref="InvalidOperationException">The stylesheet has not been built.</exception>
    public static string StylesheetPath
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

            var stylesheet = Path.Combine(
                directory.FullName,
                "src",
                "ContentManagementSystem.Server",
                "wwwroot",
                "css",
                "site.css");

            if (!File.Exists(stylesheet))
            {
                throw new InvalidOperationException(
                    $"'{stylesheet}' is missing. It is built by `npm run sass-prod` in the server " +
                    "project — a zoom pass without the site's stylesheet would be measuring nothing.");
            }

            return stylesheet;
        }
    }

    /// <summary>Serves a document containing one screen, styled as the site styles it.</summary>
    /// <param name="page">The page to route.</param>
    /// <param name="html">The rendered screen.</param>
    public static async Task ServeAsync(IPage page, string html)
    {
        ArgumentNullException.ThrowIfNull(page);

        var stylesheet = StylesheetPath;

        await page.RouteAsync($"{Origin}/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;

            if (path is "/" or "/index.html")
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    ContentType = "text/html; charset=utf-8",
                    Body = Document(html),
                });

                return;
            }

            if (path.EndsWith("site.css", StringComparison.Ordinal))
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    ContentType = "text/css; charset=utf-8",
                    Body = await File.ReadAllTextAsync(stylesheet),
                });

                return;
            }

            // Fonts and icon files. Answered empty rather than 404: a missing icon font changes
            // nothing about reflow, and a failed request would put noise in the console for a test
            // that is about layout.
            await route.FulfillAsync(new RouteFulfillOptions { Status = 200, Body = string.Empty });
        });
    }

    /// <summary>Wraps a screen the way the backoffice's own layout wraps it.</summary>
    private static string Document(string html) =>
        $"""
         <!doctype html>
         <html lang="en">
         <head>
           <meta charset="utf-8">
           <meta name="viewport" content="width=device-width, initial-scale=1">
           <link rel="stylesheet" href="/css/site.css">
           <title>Zoom pass</title>
         </head>
         <body>
           <div class="container-fluid"><main>{html}</main></div>
         </body>
         </html>
         """;
}
