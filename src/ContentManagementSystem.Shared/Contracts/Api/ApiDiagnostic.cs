using System.Text.Json.Serialization;

namespace ContentManagementSystem.Shared.Contracts.Api;

/// <summary>
/// One entry in a management API response's <c>errors</c> or <c>warnings</c> array
/// (spec section 22.2).
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
/// <para>
/// It lives in <c>Shared</c> rather than beside the problem-details mapper because it is not only a
/// failure shape. A write that succeeds can still have something to say — a zone configured with a
/// setting whose phase has not shipped is stored <em>and</em> warned about (spec section 7.2) — and a
/// success body carrying a different diagnostic shape from a failure body would make every client
/// read diagnostics twice. The backoffice runs in WebAssembly and reads both from here.
/// </para>
/// </remarks>
public sealed record ApiDiagnostic(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Property = null);
