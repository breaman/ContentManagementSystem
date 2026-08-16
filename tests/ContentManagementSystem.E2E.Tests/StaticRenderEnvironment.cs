using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// The two hosting services a pre-render supplies that a bare service collection does not
/// (task P5-22).
/// </summary>
/// <remarks>
/// Both exist because the media screens reach for things the earlier admin screens never did: the
/// uploader is an <c>InputFile</c>, which resolves <see cref="IJSRuntime"/> when it is constructed,
/// and the item screen navigates away after a permanent delete. Neither is <em>used</em> while
/// rendering statically — the framework injects them before it knows that — so the substitutes are
/// deliberately inert rather than clever.
/// <para>
/// Throwing rather than quietly returning would be wrong here, and quietly returning rather than
/// throwing would be wrong in a test that actually exercised interaction. The line these draw is
/// the honest one for a pre-render gate: constructing a component is allowed, and reaching the
/// browser is a defect the gate should not hide.
/// </para>
/// </remarks>
internal static class StaticRenderEnvironment
{
    /// <summary>The base address a pre-render is rooted at.</summary>
    public const string BaseUri = "https://localhost/";
}

/// <summary>
/// A navigation manager rooted at a fixed address, which records rather than navigates.
/// </summary>
/// <remarks>
/// A static render has nowhere to navigate to, and a component that tried would be asking the gate
/// to check a page it is not rendering. Recording the request keeps that visible if it ever matters,
/// without turning a construction-time injection into a failure.
/// </remarks>
internal sealed class StaticNavigationManager : NavigationManager
{
    public StaticNavigationManager() =>
        Initialize(StaticRenderEnvironment.BaseUri, StaticRenderEnvironment.BaseUri);

    /// <summary>Where a component asked to go, or null when none did.</summary>
    public string? RequestedUri { get; private set; }

    /// <inheritdoc />
    protected override void NavigateToCore(string uri, bool forceLoad) => RequestedUri = uri;
}

/// <summary>
/// A JavaScript runtime that refuses every call.
/// </summary>
/// <remarks>
/// Injected into <c>InputFile</c>, which resolves it on construction and calls it only once a user
/// picks a file — something no static render does. A call reaching here means a component ran
/// browser code during pre-rendering, which is a defect worth an exception rather than a silent
/// default.
/// </remarks>
internal sealed class UnavailableJSRuntime : IJSRuntime
{
    /// <inheritdoc />
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        throw new InvalidOperationException(
            $"'{identifier}' was called during a static render, which has no browser to call it in.");

    /// <inheritdoc />
    public ValueTask<TValue> InvokeAsync<TValue>(
        string identifier,
        CancellationToken cancellationToken,
        object?[]? args) =>
        InvokeAsync<TValue>(identifier, args);
}
