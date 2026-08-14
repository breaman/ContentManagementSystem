using ContentManagementSystem.Shared.Contracts.Api;

namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// What a zone create or update produced.
/// </summary>
/// <param name="Zone">The zone as it now stands.</param>
/// <param name="CurrentRevision">
/// The template's revision number after the write. A structural change cuts a new revision; an
/// edit to a label does not (spec section 8.5), so this is how a client knows which happened
/// without diffing what it sent against what it got back.
/// </param>
/// <param name="Warnings">
/// Non-blocking diagnostics about what was stored. Empty for an uneventful save. This is where a
/// configuration setting whose phase has not shipped is reported: the value is accepted and
/// persisted, and saying nothing would leave a developer to discover months later that a setting
/// they configured never did anything (spec section 7.2).
/// </param>
public sealed record ZoneSaveResult(
    ZoneDefinition Zone,
    int CurrentRevision,
    IReadOnlyList<ApiDiagnostic> Warnings);

/// <summary>
/// What removing a zone produced.
/// </summary>
/// <param name="Key">Key of the removed zone, which stored payloads still carry.</param>
/// <param name="CurrentRevision">The template's revision number after the removal.</param>
/// <remarks>
/// Removing a zone removes the <em>definition</em> only. Every payload that already holds a value
/// under this key keeps it, unreachable by the editor's normal controls and reported as orphaned
/// content until someone explicitly discards it (spec section 8.5). Returning the key rather than an
/// empty 204 is what lets the admin screen say which key went, and say it in the same words the
/// orphaned-content panel will use.
/// </remarks>
public sealed record ZoneRemovalResult(string Key, int CurrentRevision);
