using ContentManagementSystem.Shared.Contracts.Api;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Common;

/// <summary>
/// Everything the server refused about one field, drawn beside it (task P6-21).
/// </summary>
/// <remarks>
/// The counterpart to <c>DiagnosticList</c>, which reports what a whole write refused. A form of
/// twenty boxes needs both: a list at the top says how many problems there are, and this says which
/// box each one is about — and a message an editor has to match to a field by reading a property
/// name is a message they will match to the wrong one.
/// </remarks>
public partial class FieldMessages : ComponentBase
{
    /// <summary>What was said about this field, or null when nothing was.</summary>
    [Parameter]
    public IReadOnlyList<ApiDiagnostic>? Diagnostics { get; set; }
}
