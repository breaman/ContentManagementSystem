using System.Text.Json.Serialization;

namespace ContentManagementSystem.Server.Api.Cms;

/// <summary>
/// One entry in a problem response's <c>errors</c> or <c>warnings</c> array (spec section 22.2).
/// </summary>
/// <param name="Code">
/// Stable machine-readable discriminator, such as <c>structure.key-immutable</c>. This is the part
/// clients switch on.
/// </param>
/// <param name="Message">Human-readable explanation, phrased for whoever made the request.</param>
/// <param name="Property">
/// Member of the request the diagnostic concerns, when one is to blame. Null for a problem with the
/// request as a whole.
/// </param>
/// <remarks>
/// Content validation adds <c>zoneKey</c>, <c>blockId</c>, and <c>property</c> to this shape in
/// Phase 2; the members that are meaningless for a structural write are omitted rather than sent as
/// null, so a client can tell "not applicable" from "not identified".
/// </remarks>
public sealed record ApiDiagnostic(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Property = null);
