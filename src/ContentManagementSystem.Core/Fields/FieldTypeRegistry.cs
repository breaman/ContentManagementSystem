using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields;

/// <inheritdoc />
public sealed class FieldTypeRegistry : IFieldTypeRegistry
{
    private readonly Dictionary<string, IFieldType> _byKey;

    /// <summary>
    /// Builds the registry from every field type registered in the container.
    /// </summary>
    /// <param name="fieldTypes">The registered field types.</param>
    /// <exception cref="InvalidOperationException">
    /// Two field types claim the same key, or a key is blank.
    /// </exception>
    /// <remarks>
    /// Duplicate keys fail at startup rather than resolving to whichever registration happened to
    /// win. Two field types answering to <c>richText</c> is not a situation with a defensible
    /// default: one of them would silently validate and render content authored for the other.
    /// </remarks>
    public FieldTypeRegistry(IEnumerable<IFieldType> fieldTypes)
    {
        _byKey = new Dictionary<string, IFieldType>(StringComparer.Ordinal);

        foreach (var fieldType in fieldTypes)
        {
            if (string.IsNullOrWhiteSpace(fieldType.Key))
            {
                throw new InvalidOperationException(
                    $"Field type '{fieldType.GetType().FullName}' has no key. Keys are stored in " +
                    "content payloads and cannot be blank.");
            }

            if (!_byKey.TryAdd(fieldType.Key, fieldType))
            {
                throw new InvalidOperationException(
                    $"Two field types are registered under the key '{fieldType.Key}': " +
                    $"'{_byKey[fieldType.Key].GetType().FullName}' and " +
                    $"'{fieldType.GetType().FullName}'. Field type keys must be unique.");
            }
        }

        All = [.. _byKey.Values.OrderBy(f => f.Key, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public IReadOnlyList<IFieldType> All { get; }

    /// <inheritdoc />
    public IFieldType? Find(string key) => _byKey.GetValueOrDefault(key);

    /// <inheritdoc />
    public bool Contains(string key) => _byKey.ContainsKey(key);
}
