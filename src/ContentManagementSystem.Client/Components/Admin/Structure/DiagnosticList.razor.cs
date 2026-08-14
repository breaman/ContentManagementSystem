using ContentManagementSystem.Shared.Contracts.Api;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Structure;

/// <summary>
/// Shows the diagnostics a structure write returned.
/// </summary>
/// <remarks>
/// One component for errors and warnings both, because they differ only in whether the write
/// happened. Showing the stable code beside each message is deliberate: these screens are a
/// developer's tool, and the code is what they will search the source or the spec for.
/// </remarks>
public partial class DiagnosticList : ComponentBase
{
    /// <summary>What to show. Nothing renders when this is null or empty.</summary>
    [Parameter]
    public IReadOnlyList<ApiDiagnostic>? Diagnostics { get; set; }

    /// <summary>Whether these are blocking errors or non-blocking warnings.</summary>
    [Parameter]
    public bool IsError { get; set; } = true;

    /// <summary>Heading above the list.</summary>
    [Parameter]
    public string? Heading { get; set; }

    /// <summary>Bootstrap alert variant for the current severity.</summary>
    private string AlertClass => IsError ? "alert-danger" : "alert-warning";

    /// <summary>
    /// ARIA role for the current severity.
    /// </summary>
    /// <remarks>
    /// An error is an <c>alert</c>, which a screen reader announces immediately, because the save
    /// the developer just asked for did not happen. A warning is a <c>status</c>: the change was
    /// saved, so interrupting them mid-sentence would be wrong.
    /// </remarks>
    private string Role => IsError ? "alert" : "status";
}
