using ContentManagementSystem.Core.Content.Markdown;
using ContentManagementSystem.Core.Content.Schema;

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
    /// <see cref="Schema.IContentSchemaCatalog"/>, served here by
    /// <see cref="Schema.DatabaseContentSchemaCatalog"/> — revision snapshots read from the database
    /// and cached for the life of the process. That implementation is what task <c>P1-30</c> was
    /// waiting for: registering an empty catalog instead would have let a deployment start up
    /// validating every payload against nothing and reporting success.
    /// <para>
    /// The catalog and therefore the validator are <em>scoped</em>, since resolving a revision reads
    /// through a database context. The indexer and the markdown renderer hold no state and stay
    /// singleton.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCmsContent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ContentSchemaCache>();
        services.TryAddScoped<IContentSchemaCatalog, DatabaseContentSchemaCatalog>();
        services.TryAddScoped<IContentSchemaValidator, ContentSchemaValidator>();
        services.TryAddSingleton<IReferenceIndexer, ReferenceIndexer>();
        services.TryAddSingleton<IContentPayloadRemapper, ContentPayloadRemapper>();
        // One registration, so the editor preview and delivery cannot end up holding two pipelines
        // that agree today (acceptance criterion P1 #7).
        services.TryAddSingleton<IMarkdownRenderer, MarkdownRenderer>();
        // Singleton: it parses the markup it is handed and asks the field type registry which values
        // carry any. Neither touches a database (task P9-10).
        services.TryAddSingleton<IAuthoredAccessibilityValidator, AuthoredAccessibilityValidator>();

        return services;
    }
}
