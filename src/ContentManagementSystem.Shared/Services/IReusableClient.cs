using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Shared.Services;

/// <summary>
/// What the reusable-content admin screens need from the server (task P4-11).
/// </summary>
/// <remarks>
/// Implemented twice, exactly as <see cref="IPageClient"/> is: over <c>HttpClient</c> in the
/// WebAssembly backoffice, and directly over the services on the server so a screen pre-renders with
/// real content instead of a spinner.
/// <para>
/// Reads return bare values and writes return <see cref="StructureClientResult{T}"/>, following the
/// asymmetry the other two clients set out. Publishing is the case that makes it matter here: the
/// refusal an unacknowledged publish comes back with <em>is</em> the confirmation dialog's content,
/// so a client that reduced it to "422" would have nothing to put in the dialog.
/// </para>
/// </remarks>
public interface IReusableClient
{
    /// <summary>Lists the library.</summary>
    /// <param name="folderId">Restrict to one folder, or null for every folder.</param>
    /// <param name="search">Fragment matched against key and name, or null.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<IReadOnlyList<ReusableContentSummary>> ListAsync(
        int? folderId = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one item and its draft payload.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<ReusableContentDetail?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Reads the property definitions of the revision a draft was authored against.</summary>
    /// <param name="blockTypeId">Identity of the block type shaping the item.</param>
    /// <param name="revision">The captured revision (spec section 8.5).</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <remarks>
    /// The captured revision, never the block type's current one — an item authored before a
    /// property was added has no value under that key and must not be shown a control for it as
    /// though it did.
    /// </remarks>
    Task<IReadOnlyList<CapturedSlot>> GetPropertiesAsync(
        int blockTypeId,
        int revision,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the block types a new item can be shaped by.</summary>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<IReadOnlyList<BlockTypeSummary>> GetBlockTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates an item from a block type.</summary>
    /// <param name="request">The item to create.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<ReusableContentDetail>> CreateAsync(
        CreateReusableContentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Applies a partial update to an item's name, description, and folder.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="request">The members to change.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<ReusableContentDetail>> PatchAsync(
        int id,
        PatchReusableContentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Saves the draft payload.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="request">The payload and the row version the editor last saw.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<ReusableDraftSaveResult>> SaveDraftAsync(
        int id,
        SaveDraftRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Lists an item's version history, newest first.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<IReadOnlyList<ReusableVersionSummary>> GetVersionsAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>Runs the publish checks and reports the impact, without publishing.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    Task<StructureClientResult<ReusablePublishValidation>> ValidateAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>Publishes the current draft.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="acknowledgeWarnings">Whether the editor has seen the impact and still wants to.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<ReusablePublishResult>> PublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default);

    /// <summary>Retires the item, so every page placing it renders nothing in its place.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="acknowledgeWarnings">Whether the editor has seen what will stop rendering.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<ReusableUnpublishResult>> UnpublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default);

    /// <summary>Moves the item to the recycle bin.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<ReusableDeleteResult>> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>Answers where an item is used.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<ReferenceImpact> WhereUsedAsync(int id, CancellationToken cancellationToken = default);
}
