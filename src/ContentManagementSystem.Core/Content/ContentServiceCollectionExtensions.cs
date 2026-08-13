using ContentManagementSystem.Core.Content.Markdown;

using ContentManagementSystem.Shared.Contracts.Content;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Registration helpers for the payload engine.
/// </summary>
public static class ContentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the payload validator, the reference indexer, and the markdown pipeline.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// The first two depend on <see cref="Fields.IFieldTypeRegistry"/>, so call this alongside
    /// <c>AddCmsFieldTypes()</c>; the markdown renderer depends on an
    /// <see cref="Shared.Contracts.Security.IContentSanitizer"/>, so call
    /// <c>AddCmsSanitization()</c> as well. The validator also needs an
    /// <see cref="Schema.IContentSchemaCatalog"/>, which is deliberately <em>not</em> registered
    /// here: what serves captured template revisions is a database-backed, cached implementation
    /// that belongs to the hosting layer, and defaulting to an empty one would let a deployment
    /// start up validating every payload against nothing.
    /// </remarks>
    public static IServiceCollection AddCmsContent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IContentSchemaValidator, ContentSchemaValidator>();
        services.TryAddSingleton<IReferenceIndexer, ReferenceIndexer>();
        // One registration, so the editor preview and delivery cannot end up holding two pipelines
        // that agree today (acceptance criterion P1 #7).
        services.TryAddSingleton<IMarkdownRenderer, MarkdownRenderer>();

        return services;
    }
}
