using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContentManagementSystem.Shared.Content;

/// <summary>
/// Carries a <see cref="ContentPayload"/> through <c>System.Text.Json</c> unchanged.
/// </summary>
/// <remarks>
/// A payload is an API payload as well as a stored one: the draft-save request body and the version
/// responses in spec section 22.1 all carry one. Without this converter the serializer would reflect
/// over the accessors and emit <c>{ "root": …, "isObject": true }</c>, and a round trip through the
/// API would not produce the document that went in.
/// <para>
/// Reading is a document parse and writing is a verbatim copy, so absent-versus-null, member order,
/// and members this build does not recognise all survive the trip — the same guarantees
/// <see cref="ContentPayload.ToJson"/> gives, for the same reason.
/// </para>
/// </remarks>
public sealed class ContentPayloadJsonConverter : JsonConverter<ContentPayload>
{
    /// <inheritdoc />
    public override ContentPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);

        return ContentPayload.FromElement(document.RootElement);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ContentPayload value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        value.WriteTo(writer);
    }
}
