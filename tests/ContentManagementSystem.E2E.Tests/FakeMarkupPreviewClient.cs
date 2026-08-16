using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// An <see cref="IMarkupPreviewClient"/> that renders without a server (task P6-09).
/// </summary>
/// <remarks>
/// The accessibility gate renders these screens statically, with no HTTP in front of them, and what
/// it is auditing is the markup around the preview rather than the fidelity of the preview itself —
/// that is acceptance criterion P6 #2's, and it is asserted where the real pipeline is in scope.
/// <para>
/// It answers a removal for markup carrying a <c>&lt;script&gt;</c>, so the HTML editor's warning
/// banner is drawn and audited rather than skipped for want of anything to warn about.
/// </para>
/// </remarks>
public sealed class FakeMarkupPreviewClient : IMarkupPreviewClient
{
    /// <inheritdoc />
    public Task<MarkupPreviewResult?> RenderAsync(
        MarkupPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = request.Source ?? string.Empty;

        var removals = source.Contains("<script", StringComparison.OrdinalIgnoreCase)
            ? new[] { new SanitizationRemoval(SanitizationRemovalKind.Tag, "script") }
            : [];

        return Task.FromResult<MarkupPreviewResult?>(
            new MarkupPreviewResult($"<p>{source}</p>", removals));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SanitizationProfileDescriptor>> GetProfilesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SanitizationProfileDescriptor>>(
        [
            new SanitizationProfileDescriptor(
                nameof(SanitizationProfile.Developer),
                ["a", "em", "iframe", "p", "strong"]),
        ]);
}
