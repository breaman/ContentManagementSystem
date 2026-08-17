using System.Diagnostics;

using ContentManagementSystem.AppHost;
using ContentManagementSystem.ServiceDefaults;

using Microsoft.Extensions.Configuration;

var osArch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;

var builder = DistributedApplication.CreateBuilder(args);

var dbPassword = builder.AddParameter("sql-password", "P@ssw0rd!")
    .InitiallyHidden();

var sqlServer = builder.AddSqlServer("sqlserver", dbPassword, 54134)
    .WithContainerName("contentmanagementsystem-sqlserver");

if (osArch == System.Runtime.InteropServices.Architecture.Arm64
    && System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
{
    sqlServer.WithImage("azure-sql-edge");
}

var db = sqlServer.WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase(Constants.DatabaseConnectionString);

// Media originals and renditions are stored outside wwwroot and served through a signed endpoint,
// so development needs a real blob store rather than a folder. Azurite stands in for Azure Storage
// locally; production points the same connection name at a storage account.
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(azurite => azurite
        .WithContainerName("contentmanagementsystem-azurite")
        .WithLifetime(ContainerLifetime.Persistent));

var mediaBlobs = storage.AddBlobs(Constants.MediaBlobConnectionString);

var server = builder.AddProject<Projects.ContentManagementSystem_Server>("server", "https")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck(Constants.HealthEndpointPath)
    .WithReference(db)
    .WithReference(mediaBlobs)
    .WaitFor(mediaBlobs);

// Redis backs the distributed output cache once the site runs on more than one instance. It stays
// switched off by default so a single-instance developer loop does not pay for a container it will
// not read from; the cache itself is not wired up until Phase 8.
if (builder.Configuration.GetValue($"{Constants.CmsConfigurationSection}:UseRedisOutputCache", false))
{
    var redis = builder.AddRedis(Constants.OutputCacheConnectionString)
        .WithContainerName("contentmanagementsystem-redis")
        .WithLifetime(ContainerLifetime.Persistent);

    server.WithReference(redis).WaitFor(redis);
}

var migrations = server.AddEFMigrations("ef-migrations")
    .WithMigrationsProject<Projects.ContentManagementSystem_Data>()
    .RunDatabaseUpdateOnStart()
    .WithCommand("dotnet-tools", "Restore Tools", async (ExecuteCommandContext x) =>
    {
        var process = Process.Start(new ProcessStartInfo()
        {
            FileName = "dotnet",
            ArgumentList = { "tool", "restore" },
        });
        if (process is null) return CommandResults.Failure();
        await process.WaitForExitAsync(x.CancellationToken);
        return CommandResults.Success();
    }, new CommandOptions())
    .WaitFor(db);

server.WaitForCompletion(migrations);

builder.Build().Run();