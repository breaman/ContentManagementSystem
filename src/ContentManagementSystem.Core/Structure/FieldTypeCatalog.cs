using System.Text.Json;

using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Fields.Configuration;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Core.Structure;

/// <summary>
/// Describes the registered field types for the read-only introspection endpoint (task P1-24).
/// </summary>
/// <remarks>
/// Built once and cached. The registry is a singleton whose contents cannot change without a
/// restart, and generating a dozen JSON Schema documents per request to describe something that is
/// constant for the lifetime of the process would be pure waste on a screen a developer refreshes
/// while iterating on a content model.
/// </remarks>
public interface IFieldTypeCatalog
{
    /// <summary>Every registered field type, ordered by key.</summary>
    IReadOnlyList<FieldTypeDescriptor> All { get; }

    /// <summary>Finds one field type by key.</summary>
    /// <param name="key">The field type key.</param>
    /// <returns>The descriptor, or null when nothing is registered under that key.</returns>
    FieldTypeDescriptor? Find(string key);
}

/// <inheritdoc />
public sealed class FieldTypeCatalog : IFieldTypeCatalog
{
    private readonly Dictionary<string, FieldTypeDescriptor> _byKey;

    /// <summary>
    /// Describes every field type in the registry.
    /// </summary>
    /// <param name="registry">The registry a real deployment builds.</param>
    public FieldTypeCatalog(IFieldTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        All = registry.All.Select(Describe).ToList();
        _byKey = All.ToDictionary(descriptor => descriptor.Key, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public IReadOnlyList<FieldTypeDescriptor> All { get; }

    /// <inheritdoc />
    public FieldTypeDescriptor? Find(string key) => _byKey.GetValueOrDefault(key);

    private static FieldTypeDescriptor Describe(IFieldType fieldType)
    {
        using var schema = JsonDocument.Parse(FieldConfigurationSchemaWriter.Write(fieldType));

        return new FieldTypeDescriptor(
            fieldType.Key,
            fieldType.DisplayName,
            Names(fieldType.Capabilities),
            fieldType.Capabilities.HasFlag(FieldTypeCapabilities.Container),
            fieldType.Capabilities.HasFlag(FieldTypeCapabilities.DeveloperOnly),
            schema.RootElement.Clone());
    }

    /// <summary>Projects the capability flags onto their names.</summary>
    /// <remarks>
    /// <see cref="FieldTypeCapabilities.None"/> is skipped rather than reported as a name: it is the
    /// absence of every flag, and a list containing "None" beside nothing else reads as a capability
    /// a client might branch on.
    /// </remarks>
    private static List<string> Names(FieldTypeCapabilities capabilities) =>
        Enum.GetValues<FieldTypeCapabilities>()
            .Where(flag => flag is not FieldTypeCapabilities.None && capabilities.HasFlag(flag))
            .Select(flag => flag.ToString())
            .ToList();
}
