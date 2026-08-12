using System.Diagnostics;

using ContentManagementSystem.AppHost;
using ContentManagementSystem.ServiceDefaults;

var osArch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;

var builder = DistributedApplication.CreateBuilder(args);

var dbPassword = builder.AddParameter("sql-password", "P@ssw0rd!")
    .InitiallyHidden();

var sqlServer = builder.AddSqlServer("sqlserver", dbPassword)
    .WithContainerName("contentmanagementsystem-sqlserver");

if (osArch == System.Runtime.InteropServices.Architecture.Arm64
    && System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
{
    sqlServer.WithImage("azure-sql-edge");
}

var db = sqlServer.WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase(Constants.DatabaseConnectionString);

var server = builder.AddProject<Projects.ContentManagementSystem_Server>("server", "https")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck(Constants.HealthEndpointPath)
    .WithReference(db);

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