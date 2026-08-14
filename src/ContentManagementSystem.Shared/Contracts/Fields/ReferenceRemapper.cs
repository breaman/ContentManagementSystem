namespace ContentManagementSystem.Shared.Contracts.Fields;

/// <summary>
/// Maps a referenced entity to the copy that replaces it.
/// </summary>
/// <param name="targetType">Kind of entity referenced.</param>
/// <param name="targetId">Identity as stored.</param>
/// <returns>
/// The replacement identity, or <paramref name="targetId"/> unchanged when this target is not being
/// replaced.
/// </returns>
/// <remarks>
/// Written as a delegate rather than a dictionary so the decision stays with the caller. Duplicating
/// a subtree rewrites links to pages <em>inside</em> the copied set and deliberately leaves links out
/// of it pointing at the originals; media is never copied at all, so it is never remapped
/// (spec section 14.12). A field type given a map would have to be told all of that.
/// </remarks>
public delegate int ReferenceRemapper(ContentReferenceTargetType targetType, int targetId);
