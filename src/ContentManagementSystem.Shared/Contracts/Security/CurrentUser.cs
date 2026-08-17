namespace ContentManagementSystem.Shared.Contracts.Security;

/// <summary>
/// Who is signed in, as <c>GET /api/cms/v1/me</c> reports it.
/// </summary>
/// <param name="UserId">
/// The editor's own database identity — the value that goes into <c>OwnerUserId</c> when they take
/// ownership of a page.
/// </param>
/// <param name="DisplayName">What to call them on screen.</param>
/// <remarks>
/// The backoffice cannot work this out for itself. Blazor's authentication state is serialized into
/// the WebAssembly client with the name and role claims only, so the signed-in editor's <em>id</em>
/// never crosses that boundary — and every screen that has to write it into a record needs one
/// (the properties panel's owner field in task P6-17, the dashboard's "my work" tile in P6-24).
/// <para>
/// Deliberately not a user <em>directory</em>. This answers "who am I", not "who else is there";
/// assigning a page to another editor needs the user management Phase 7 builds, and inventing a
/// lookup here would be a permission surface designed by a screen rather than by section 21.
/// </para>
/// </remarks>
public sealed record CurrentUser(int UserId, string DisplayName);
