using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Media.Library;

/// <inheritdoc cref="IMediaFolderService" />
/// <param name="context">The application database context.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="users">Identity of the caller, stamped on a delete.</param>
/// <param name="clock">Source of the current time.</param>
/// <param name="logger">Log for every structural change to the tree.</param>
public sealed class MediaFolderService(
    ApplicationDbContext context,
    ICmsAuthorization authorization,
    IUserService users,
    TimeProvider clock,
    ILogger<MediaFolderService> logger) : IMediaFolderService
{
    /// <summary>
    /// How deep the folder tree may go.
    /// </summary>
    /// <remarks>
    /// A bound on the materialized path rather than a usability opinion: the column is 800
    /// characters, and a path is a slash-separated list of row ids, so an unbounded tree eventually
    /// writes a path that will not fit and truncates silently into a different branch.
    /// </remarks>
    public const int MaxDepth = 10;

    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<MediaFolderNode>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return Forbidden<IReadOnlyList<MediaFolderNode>>("Browsing the media library is not permitted.");
        }

        var folders = await context.MediaFolders
            .AsNoTracking()
            .OrderBy(folder => folder.SortOrder)
            .ThenBy(folder => folder.Name)
            .ToListAsync(cancellationToken);

        // One grouped count rather than a count per folder. A tree of two hundred folders would
        // otherwise be two hundred round trips to render a sidebar.
        var counts = await context.MediaItems
            .AsNoTracking()
            .Where(item => item.FolderId != null)
            .GroupBy(item => item.FolderId!.Value)
            .Select(group => new { FolderId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.FolderId, entry => entry.Count, cancellationToken);

        return CmsResult<IReadOnlyList<MediaFolderNode>>.Success(Build(folders, counts, parentId: null));
    }

    /// <inheritdoc />
    public async Task<CmsResult<MediaFolderNode>> CreateAsync(
        CreateMediaFolderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.MediaUpload))
        {
            return Forbidden<MediaFolderNode>("Managing media folders is not permitted.");
        }

        if (Validate(request.Name) is { } invalid) return invalid;

        MediaFolder? parent = null;

        if (request.ParentId is { } parentId)
        {
            parent = await context.MediaFolders.FirstOrDefaultAsync(
                folder => folder.Id == parentId, cancellationToken);

            if (parent is null)
            {
                return CmsResult<MediaFolderNode>.Invalid(
                    MediaCodes.FolderInvalidParent,
                    $"Folder {parentId} does not exist.",
                    nameof(CreateMediaFolderRequest.ParentId));
            }

            if (Depth(parent.Path) >= MaxDepth)
            {
                return CmsResult<MediaFolderNode>.Invalid(
                    MediaCodes.FolderInvalidParent,
                    $"Folders can be nested at most {MaxDepth} deep.",
                    nameof(CreateMediaFolderRequest.ParentId));
            }
        }

        var siblings = await context.MediaFolders
            .Where(folder => folder.ParentId == request.ParentId)
            .Select(folder => folder.SortOrder)
            .ToListAsync(cancellationToken);

        var folder = new MediaFolder
        {
            ParentId = request.ParentId,
            Name = request.Name.Trim(),
            SortOrder = siblings.Count == 0 ? 0 : siblings.Max() + 1,
            // Placeholder. The path contains the folder's own id, so it can only be built once the
            // row has one — the same two-step the page tree makes for the same reason.
            Path = string.Empty,
        };

        context.MediaFolders.Add(folder);

        await context.SaveChangesAsync(cancellationToken);

        folder.Path = BuildPath(parent?.Path, folder.Id);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Media folder {FolderId} ('{FolderName}') was created under {ParentId}.",
            folder.Id,
            folder.Name,
            request.ParentId);

        return CmsResult<MediaFolderNode>.Success(ToNode(folder, itemCount: 0, children: []));
    }

    /// <inheritdoc />
    public async Task<CmsResult<MediaFolderNode>> PatchAsync(
        int id,
        PatchMediaFolderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.MediaUpload))
        {
            return Forbidden<MediaFolderNode>("Managing media folders is not permitted.");
        }

        var folder = await context.MediaFolders.FirstOrDefaultAsync(
            candidate => candidate.Id == id, cancellationToken);

        if (folder is null) return NotFound<MediaFolderNode>(id);

        var name = request.Name.Or(folder.Name);

        if (Validate(name) is { } invalid) return invalid;

        folder.Name = name!.Trim();
        folder.SortOrder = request.SortOrder.Or(folder.SortOrder);

        if (request.ParentId.IsSet && request.ParentId.Value != folder.ParentId)
        {
            if (await MoveAsync(folder, request.ParentId.Value, cancellationToken) is { } refusal)
            {
                return refusal;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        var itemCount = await context.MediaItems.CountAsync(
            item => item.FolderId == id, cancellationToken);

        return CmsResult<MediaFolderNode>.Success(ToNode(folder, itemCount, children: []));
    }

    /// <inheritdoc />
    public async Task<CmsResult<int>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.MediaUpload))
        {
            return Forbidden<int>("Managing media folders is not permitted.");
        }

        var folder = await context.MediaFolders.FirstOrDefaultAsync(
            candidate => candidate.Id == id, cancellationToken);

        if (folder is null) return NotFound<int>(id);

        var items = await context.MediaItems.CountAsync(item => item.FolderId == id, cancellationToken);
        var children = await context.MediaFolders.CountAsync(
            child => child.ParentId == id, cancellationToken);

        if (items > 0 || children > 0)
        {
            return CmsResult<int>.Conflict(
                MediaCodes.FolderNotEmpty,
                $"This folder still holds {items} item(s) and {children} folder(s). Move them " +
                "somewhere else first.");
        }

        folder.IsDeleted = true;
        folder.DeletedOn = clock.GetUtcNow();
        folder.DeletedBy = users.UserId;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Media folder {FolderId} ('{FolderName}') was deleted.", id, folder.Name);

        return CmsResult<int>.Success(id);
    }

    /// <summary>
    /// Repoints a folder at a new parent and rewrites the paths beneath it.
    /// </summary>
    /// <param name="folder">The tracked folder being moved.</param>
    /// <param name="newParentId">Its new parent, or null for the root of the library.</param>
    /// <param name="cancellationToken">Token observed while loading the subtree.</param>
    /// <returns>The refusal, or null when the move was applied.</returns>
    private async Task<CmsResult<MediaFolderNode>?> MoveAsync(
        MediaFolder folder,
        int? newParentId,
        CancellationToken cancellationToken)
    {
        MediaFolder? parent = null;

        if (newParentId is { } parentId)
        {
            if (parentId == folder.Id) return InvalidParent("A folder cannot be its own parent.");

            parent = await context.MediaFolders.FirstOrDefaultAsync(
                candidate => candidate.Id == parentId, cancellationToken);

            if (parent is null) return InvalidParent($"Folder {parentId} does not exist.");

            // A prospective parent whose own path runs through this folder is a descendant of it.
            if (parent.Path.StartsWith(folder.Path, StringComparison.Ordinal))
            {
                return InvalidParent("A folder cannot be moved inside itself.");
            }
        }

        // Deleted descendants move too, so that restoring one later puts it back into a branch that
        // still exists.
        var descendants = await context.MediaFolders
            .IgnoreQueryFilters()
            .Where(candidate => candidate.Id != folder.Id && candidate.Path.StartsWith(folder.Path))
            .ToListAsync(cancellationToken);

        var oldPath = folder.Path;
        var newDepth = parent is null ? 1 : Depth(parent.Path) + 1;
        var deepest = descendants.Count == 0 ? Depth(oldPath) : descendants.Max(child => Depth(child.Path));

        if (newDepth + (deepest - Depth(oldPath)) > MaxDepth)
        {
            return InvalidParent($"That move would nest folders more than {MaxDepth} deep.");
        }

        folder.ParentId = parent?.Id;
        folder.Path = BuildPath(parent?.Path, folder.Id);

        foreach (var descendant in descendants)
        {
            // Only the ancestry prefix changes; the shape below this folder is untouched, which is
            // the whole reason the path is stored rather than recomputed from ParentId.
            descendant.Path = string.Concat(folder.Path, descendant.Path.AsSpan(oldPath.Length));
        }

        logger.LogInformation(
            "Media folder {FolderId} was moved under {ParentId}, rewriting {DescendantCount} path(s).",
            folder.Id,
            newParentId,
            descendants.Count);

        return null;

        static CmsResult<MediaFolderNode> InvalidParent(string message) =>
            CmsResult<MediaFolderNode>.Invalid(
                MediaCodes.FolderInvalidParent,
                message,
                nameof(PatchMediaFolderRequest.ParentId));
    }

    /// <summary>Assembles one level of the tree and, recursively, everything under it.</summary>
    private static List<MediaFolderNode> Build(
        List<MediaFolder> folders,
        Dictionary<int, int> counts,
        int? parentId) =>
        [.. folders
            .Where(folder => folder.ParentId == parentId)
            .Select(folder => ToNode(
                folder,
                counts.GetValueOrDefault(folder.Id),
                Build(folders, counts, folder.Id)))];

    private static MediaFolderNode ToNode(
        MediaFolder folder,
        int itemCount,
        IReadOnlyList<MediaFolderNode> children) =>
        new(folder.Id, folder.ParentId, folder.Name, folder.SortOrder, itemCount, children);

    /// <summary>Checks a folder name against the column that stores it.</summary>
    private static CmsResult<MediaFolderNode>? Validate(string? name) => name switch
    {
        null or "" => Refuse(MediaCodes.NameRequired, "A folder needs a name."),
        _ when string.IsNullOrWhiteSpace(name) => Refuse(MediaCodes.NameRequired, "A folder needs a name."),
        { Length: > FieldLengths.EntityName } => Refuse(MediaCodes.TooLong, "That folder name is too long."),
        _ => null,
    };

    private static CmsResult<MediaFolderNode> Refuse(string code, string message) =>
        CmsResult<MediaFolderNode>.Invalid(code, message, nameof(CreateMediaFolderRequest.Name));

    /// <summary>The path a folder has under a given parent, such as <c>/1/8/44/</c>.</summary>
    private static string BuildPath(string? parentPath, int folderId) =>
        string.IsNullOrEmpty(parentPath) ? $"/{folderId}/" : $"{parentPath}{folderId}/";

    /// <summary>How many folders a path names, which is the depth of the last one.</summary>
    private static int Depth(string path) => path.Count(character => character is '/') - 1;

    private static CmsResult<T> Forbidden<T>(string message) =>
        CmsResult<T>.Forbidden(message, MediaCodes.Forbidden);

    private static CmsResult<T> NotFound<T>(int id) =>
        CmsResult<T>.NotFound($"Media folder {id} does not exist.", MediaCodes.NotFound);
}
