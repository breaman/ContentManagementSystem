using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields;

/// <summary>
/// The set of field types this deployment knows about, keyed by <see cref="IFieldType.Key"/>.
/// </summary>
/// <remarks>
/// Lookups return null rather than throwing. A payload can legitimately name a field type that no
/// longer exists — content outlives the code that was deployed when it was written — and the
/// delivery path answers that by rendering nothing and logging a warning, never by failing a public
/// request (spec section 15.3).
/// </remarks>
public interface IFieldTypeRegistry
{
    /// <summary>Every registered field type, ordered by key.</summary>
    IReadOnlyList<IFieldType> All { get; }

    /// <summary>Finds a field type by key.</summary>
    /// <param name="key">The field type key stored in the payload, such as <c>richText</c>.</param>
    /// <returns>The field type, or null when nothing is registered under that key.</returns>
    IFieldType? Find(string key);

    /// <summary>Whether a field type is registered under the given key.</summary>
    /// <param name="key">The field type key.</param>
    bool Contains(string key);
}
