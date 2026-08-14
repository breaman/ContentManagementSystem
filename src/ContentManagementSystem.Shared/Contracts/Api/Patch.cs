using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContentManagementSystem.Shared.Contracts.Api;

/// <summary>
/// One member of a PATCH request body: a value that was supplied, or the fact that it was not.
/// </summary>
/// <typeparam name="T">Type of the value being patched.</typeparam>
/// <remarks>
/// A plain nullable member cannot express a partial update, because it collapses the two things a
/// PATCH has to tell apart: <c>{"ownerUserId": null}</c> means "nobody owns this page now", and
/// omitting the member entirely means "do not touch the owner". Binding both to <c>int?</c> makes a
/// client that sends only the field it changed silently clear every field it did not — which is how
/// an editor's SEO settings disappear when someone fixes a title.
/// <para>
/// The same distinction <c>ContentValueState</c> draws inside a payload, drawn at the request
/// boundary. <c>System.Text.Json</c> supplies the reading half for free: the converter runs only for
/// a member that is present, so an absent one leaves the struct at its default and
/// <see cref="IsSet"/> stays false. The writing half needs
/// <see cref="JsonIgnoreCondition.WhenWritingDefault"/> on the member, since a converter cannot
/// suppress a property name its parent has already written.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
/// public Patch&lt;int?&gt; OwnerUserId { get; init; }
///
/// page.OwnerUserId = request.OwnerUserId.Or(page.OwnerUserId);
/// </code>
/// </example>
[JsonConverter(typeof(PatchConverterFactory))]
public readonly struct Patch<T> : IEquatable<Patch<T>>
{
    /// <summary>Creates a member that was supplied.</summary>
    /// <param name="value">The supplied value, which may be null to clear it.</param>
    public Patch(T? value)
    {
        Value = value;
        IsSet = true;
    }

    /// <summary>Whether the request carried this member at all.</summary>
    public bool IsSet { get; }

    /// <summary>The supplied value. Meaningless unless <see cref="IsSet"/>.</summary>
    public T? Value { get; }

    /// <summary>Applies the patch to a current value.</summary>
    /// <param name="current">What is stored now.</param>
    /// <returns>The supplied value when the member was present, otherwise <paramref name="current"/>.</returns>
    public T? Or(T? current) => IsSet ? Value : current;

    /// <summary>Wraps a value as a supplied member, for callers building a request in code.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator Patch<T>(T value) => new(value);

    /// <inheritdoc />
    /// <remarks>
    /// Hand-written rather than left to <see cref="ValueType.Equals(object)"/>, whose reflective
    /// field walk is what <see cref="JsonIgnoreCondition.WhenWritingDefault"/> would otherwise call
    /// once per member per serialization.
    /// </remarks>
    public bool Equals(Patch<T> other) =>
        IsSet == other.IsSet && EqualityComparer<T?>.Default.Equals(Value, other.Value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Patch<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(IsSet, Value);

    /// <summary>Whether two patches carry the same state.</summary>
    /// <param name="left">The first patch.</param>
    /// <param name="right">The second patch.</param>
    public static bool operator ==(Patch<T> left, Patch<T> right) => left.Equals(right);

    /// <summary>Whether two patches carry different state.</summary>
    /// <param name="left">The first patch.</param>
    /// <param name="right">The second patch.</param>
    public static bool operator !=(Patch<T> left, Patch<T> right) => !left.Equals(right);
}

/// <summary>
/// Builds the converter for a closed <see cref="Patch{T}"/>.
/// </summary>
/// <remarks>
/// A factory rather than a converter, because the attribute on the open generic type has to produce
/// one converter per element type.
/// </remarks>
public sealed class PatchConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        return typeToConvert.IsGenericType &&
            typeToConvert.GetGenericTypeDefinition() == typeof(Patch<>);
    }

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        return (JsonConverter)Activator.CreateInstance(
            typeof(PatchConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
    }

    /// <summary>Reads and writes one element type's <see cref="Patch{T}"/>.</summary>
    /// <typeparam name="T">Type of the value being patched.</typeparam>
    internal sealed class PatchConverter<T> : JsonConverter<Patch<T>>
    {
        /// <summary>
        /// Always true, so an explicit <c>null</c> reaches <see cref="Read"/>.
        /// </summary>
        /// <remarks>
        /// Without this the converter is skipped for a null token and the member is left at its
        /// default — which is exactly <see cref="Patch{T}.IsSet"/> false, turning "clear this" back
        /// into "leave this alone" and losing the distinction the type exists for.
        /// </remarks>
        public override bool HandleNull => true;

        /// <inheritdoc />
        public override Patch<T> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            new(JsonSerializer.Deserialize<T>(ref reader, options));

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, Patch<T> value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
