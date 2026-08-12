namespace ContentManagementSystem.ServiceDefaults;

public static class Constants
{
    public const string HealthEndpointPath = "/health";
    public const string AlivenessEndpointPath = "/alive";
    public const string DatabaseConnectionString = "contentmanagementsystemdb";

    /// <summary>
    /// Connection name for the blob store holding media originals and generated renditions.
    /// Azurite in development, an Azure Storage account in production.
    /// </summary>
    public const string MediaBlobConnectionString = "media";

    /// <summary>
    /// Connection name for the Redis instance backing the distributed output cache. Only present
    /// when <c>Cms:UseRedisOutputCache</c> is enabled.
    /// </summary>
    public const string OutputCacheConnectionString = "outputcache";

    /// <summary>Root configuration section for all CMS settings.</summary>
    public const string CmsConfigurationSection = "Cms";
}
