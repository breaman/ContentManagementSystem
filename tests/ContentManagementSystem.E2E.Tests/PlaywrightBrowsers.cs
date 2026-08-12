using Microsoft.Playwright;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// Installs the browser binaries Playwright needs, once per test run.
/// </summary>
/// <remarks>
/// The usual route is the generated <c>playwright.ps1</c> script, which requires PowerShell to be
/// installed. Driving the same CLI through <see cref="Program.Main"/> keeps the suite runnable on
/// a bare developer machine and on a CI agent without adding a PowerShell dependency to either.
/// The install is idempotent — Playwright skips browsers that are already present.
/// </remarks>
public static class PlaywrightBrowsers
{
    private static readonly Lock InstallLock = new();
    private static bool _installed;

    /// <summary>
    /// Ensures Chromium is available, installing it on first call.
    /// </summary>
    /// <exception cref="InvalidOperationException">The Playwright CLI reported a failure.</exception>
    public static void EnsureInstalled()
    {
        lock (InstallLock)
        {
            if (_installed)
            {
                return;
            }

            var exitCode = Program.Main(["install", "chromium", "--with-deps"]);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Playwright browser installation failed with exit code {exitCode}.");
            }

            _installed = true;
        }
    }
}
