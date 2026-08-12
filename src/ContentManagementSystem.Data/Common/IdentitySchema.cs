using Microsoft.AspNetCore.Identity;

namespace ContentManagementSystem.Data.Common;

/// <summary>
/// The ASP.NET Core Identity store schema this solution's EF model is built against.
/// </summary>
/// <remarks>
/// <see cref="IdentityOptions.Stores"/>'s schema version changes the shape of the generated model,
/// so it is part of the database contract rather than a runtime preference. It is defined here
/// once because anything that builds an <c>ApplicationDbContext</c> — the web host, design-time
/// migration tooling, and the integration-test fixtures — must agree on it. When they disagree,
/// EF reports the model as having pending changes even though the migrations are current.
/// </remarks>
public static class IdentitySchema
{
    /// <summary>The store schema version the migrations were generated from.</summary>
    public static Version Version => IdentitySchemaVersions.Version3;
}
