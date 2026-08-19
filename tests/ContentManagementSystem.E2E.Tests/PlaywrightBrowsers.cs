namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// Installs the browser binaries Playwright needs, once per test run.
/// </summary>
/// <remarks>
/// The usual route is the generated <c>playwright.ps1</c> script, which requires PowerShell to be
/// installed. Driving the same CLI through <c>Microsoft.Playwright.Program.Main</c> keeps the suite
/// runnable on a bare developer machine and on a CI agent without adding a PowerShell dependency to
/// either. The install is idempotent — Playwright skips browsers that are already present.
/// </remarks>
public static class PlaywrightBrowsers
{
    /// <summary>The engine every suite but the browser matrix uses.</summary>
    public const string Chromium = "chromium";

    private static readonly Lock InstallLock = new();
    private static readonly HashSet<string> Installed = new(StringComparer.Ordinal);

    /// <summary>
    /// Ensures a browser is available, installing it on first call.
    /// </summary>
    /// <param name="browser">
    /// Which engine — <c>chromium</c>, <c>firefox</c>, or <c>webkit</c>. Chromium by default, since
    /// it is what every suite except the NFR-13 matrix runs against.
    /// </param>
    /// <exception cref="InvalidOperationException">The Playwright CLI reported a failure.</exception>
    /// <remarks>
    /// Tracked per engine rather than with one flag. Firefox and WebKit are a few hundred megabytes
    /// each and only the browser matrix needs them, so a developer running the accessibility gate
    /// should not be made to download all three.
    /// </remarks>
    public static void EnsureInstalled(string browser = Chromium)
    {
        lock (InstallLock)
        {
            if (!Installed.Add(browser))
            {
                return;
            }

            // Fully qualified since P9-07: the server project declares a public Program in the
            // global namespace so WebApplicationFactory can boot it, and referencing that project
            // for the public accessibility gate brings the name into scope here.
            var exitCode = Microsoft.Playwright.Program.Main(["install", browser, "--with-deps"]);

            if (exitCode != 0)
            {
                Installed.Remove(browser);

                throw new InvalidOperationException(
                    $"Playwright installation of '{browser}' failed with exit code {exitCode}.");
            }
        }
    }
}
