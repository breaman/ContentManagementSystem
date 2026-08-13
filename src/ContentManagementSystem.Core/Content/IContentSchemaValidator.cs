using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Checks a content payload against the runtime-defined schema it was authored against.
/// </summary>
/// <remarks>
/// The gate every save and every publish passes through (spec sections 6.2 and 8.5). It runs on
/// write, never on render: a published version was validated when it was published, and re-checking
/// it on each request would put a schema lookup on the delivery path for content that cannot have
/// changed.
/// </remarks>
public interface IContentSchemaValidator
{
    /// <summary>
    /// Validates a payload against the template revision it captured.
    /// </summary>
    /// <param name="payload">The payload to check.</param>
    /// <param name="mode">Whether a draft is being saved or content is being published.</param>
    /// <param name="cancellationToken">Token observed while validating.</param>
    /// <returns>Everything found, errors and warnings together.</returns>
    /// <remarks>
    /// The normal path. Content is checked against the schema it was written against, which is the
    /// same schema it will render against.
    /// </remarks>
    ValueTask<ContentValidationReport> ValidateAsync(
        ContentPayload payload,
        ValidationMode mode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates a payload against a schema chosen by the caller.
    /// </summary>
    /// <param name="payload">The payload to check.</param>
    /// <param name="schema">The template revision to check against.</param>
    /// <param name="mode">Whether a draft is being saved or content is being published.</param>
    /// <param name="cancellationToken">Token observed while validating.</param>
    /// <returns>Everything found, errors and warnings together.</returns>
    /// <remarks>
    /// For the moment an editor adopts a newer template revision: the payload still names the old
    /// one, and what matters is whether it satisfies the new one — which zones have been removed
    /// beneath it, and which newly required zone it has never filled in (spec section 8.5).
    /// </remarks>
    ValueTask<ContentValidationReport> ValidateAsync(
        ContentPayload payload,
        ContentSchema schema,
        ValidationMode mode,
        CancellationToken cancellationToken);
}
