using System.Globalization;
using System.Text;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// The payload path the walk is currently at, maintained as a stack.
/// </summary>
/// <remarks>
/// A stack rather than a string that is appended to. Composing
/// <c>"zones.hero.items[0].properties.headline"</c> at every property visited would allocate on the
/// happy path — the path is only ever needed when something is actually wrong, and almost nothing
/// ever is — and it dominated the cost in the S1 spike until it was pushed and popped instead.
/// <para>
/// Segments are stored in the form they appear in: <c>hero</c> for a member, <c>[0]</c> for an
/// index. Joining them is then a matter of deciding whether a separator is needed, which is what
/// keeps a prefixed path reading as one expression rather than as two conventions glued together.
/// </para>
/// </remarks>
internal sealed class ContentPath
{
    private readonly List<string> _segments = [];

    /// <summary>Enters a member, leaving it again when the scope is disposed.</summary>
    /// <param name="member">The member name.</param>
    /// <returns>A scope that pops the segment.</returns>
    public Scope Push(string member)
    {
        _segments.Add(member);

        return new Scope(this);
    }

    /// <summary>Enters an array element, leaving it again when the scope is disposed.</summary>
    /// <param name="index">Zero-based position in the array.</param>
    /// <returns>A scope that pops the segment.</returns>
    public Scope PushIndex(int index)
    {
        _segments.Add(string.Create(CultureInfo.InvariantCulture, $"[{index}]"));

        return new Scope(this);
    }

    /// <summary>Builds the absolute path of the position the walk is at.</summary>
    /// <returns>The path, such as <c>zones.hero.items[0].properties.headline</c>.</returns>
    public override string ToString()
    {
        if (_segments.Count == 0) return string.Empty;

        var builder = new StringBuilder();

        foreach (var segment in _segments)
        {
            Append(builder, segment);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds the absolute path of something a field type reported against its own value.
    /// </summary>
    /// <param name="relativePath">
    /// The path the field type reported, or null when it spoke about the value as a whole.
    /// </param>
    /// <returns>The absolute path.</returns>
    /// <remarks>
    /// The other half of the arrangement in <see cref="Shared.Contracts.Fields.ContentReference"/>
    /// and <see cref="Shared.Contracts.Fields.ValidationDiagnostic"/>: a field type reports where in
    /// <em>its</em> value the problem is, because it cannot know where in the document it sits, and
    /// the walk — which does know — supplies the rest.
    /// </remarks>
    public string Combine(string? relativePath)
    {
        var absolute = ToString();

        if (string.IsNullOrEmpty(relativePath)) return absolute;

        if (absolute.Length == 0) return relativePath;

        var builder = new StringBuilder(absolute);

        Append(builder, relativePath);

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string segment)
    {
        // An index segment continues the expression it indexes: 'items' then '[0]' is 'items[0]',
        // never 'items.[0]'.
        if (builder.Length > 0 && segment.Length > 0 && segment[0] is not '[')
        {
            builder.Append('.');
        }

        builder.Append(segment);
    }

    private void Pop() => _segments.RemoveAt(_segments.Count - 1);

    /// <summary>Pops the segment its <see cref="Push"/> pushed.</summary>
    /// <param name="path">The path to pop from.</param>
    internal readonly struct Scope(ContentPath path) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose() => path.Pop();
    }
}
