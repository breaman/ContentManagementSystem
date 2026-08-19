using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Core.Tags;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.Search;

/// <summary>Registration for search and tags (tasks P8-18 to P8-20).</summary>
public static class SearchServiceCollectionExtensions
{
    /// <summary>
    /// Registers the index, the query side, the tag vocabulary, and the outbox handler that keeps
    /// the index current.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Call after <c>AddCmsCaching()</c>, which registers the runner this adds a handler to. Tags
    /// are registered here rather than beside content because their reason to exist is the search
    /// filter — the taxonomy and the index it narrows are one feature (spec section 17.1).
    /// </remarks>
    public static IServiceCollection AddCmsSearch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ISearchIndexQueue, SearchIndexQueue>();
        services.TryAddScoped<ISearchIndexer, SearchIndexer>();
        services.TryAddScoped<ISearchService, SearchService>();
        services.TryAddScoped<ITagService, TagService>();

        // A singleton because it remembers one fact about the database, established once per
        // process and unchanged until a migration or a restart.
        services.TryAddSingleton<SearchCapabilities>();

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IOutboxMessageHandler, SearchIndexHandler>());

        return services;
    }
}
