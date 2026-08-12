using System.Security.Cryptography;

namespace S3.EditorInterop.Server;

/// <summary>
/// The backoffice Content Security Policy from spec §20.5, in two variants so the spike can measure
/// what the editors actually need rather than guessing.
/// </summary>
public static class Csp
{
    /// <summary>Everything except <c>style-src</c>, which is what the two variants differ on.</summary>
    private const string Common =
        "default-src 'self'; " +
        "script-src 'self' 'wasm-unsafe-eval'; " +   // Blazor WebAssembly needs wasm-unsafe-eval
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'self'";

    public static string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    /// <summary>Strict policy that hands the editors a nonce for their injected stylesheets.</summary>
    public static string WithNonce(string nonce) => $"{Common}; style-src 'self' 'nonce-{nonce}'";

    /// <summary>Strict policy with no nonce at all — the control case.</summary>
    public static string WithoutNonce() => $"{Common}; style-src 'self'";
}
