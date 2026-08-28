using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace InferHub.Client.Models.Vector;

/// <summary>Helpers for reading the opaque <c>payload</c> carried by records and matches.</summary>
public static class VectorPayloadExtensions
{
    private static readonly JsonSerializerOptions DefaultOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Deserialize a nullable payload into <typeparamref name="T"/>. Returns <c>default</c>
    /// when the payload is absent or JSON <c>null</c>.
    /// </summary>
    /// <remarks>
    /// This overload uses reflection-based serialization for <typeparamref name="T"/>. Under
    /// trimming or Native AOT, pass the
    /// <see cref="As{T}(JsonElement?, JsonTypeInfo{T})"/> overload with a source-generated
    /// <see cref="JsonTypeInfo{T}"/> to stay warning-free.
    /// </remarks>
    /// <typeparam name="T">Target type.</typeparam>
    /// <param name="payload">The payload element (e.g. <c>record.Payload</c>).</param>
    /// <param name="options">Serializer options; web defaults are used when null.</param>
    [RequiresUnreferencedCode("JSON deserialization of the caller's payload type may require types that cannot be statically analysed. Use the JsonTypeInfo<T> overload for trimming/AOT.")]
    [RequiresDynamicCode("JSON deserialization of the caller's payload type may require runtime code generation. Use the JsonTypeInfo<T> overload for AOT.")]
    public static T? As<T>(this JsonElement? payload, JsonSerializerOptions? options = null)
    {
        if (payload is not { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } element)
        {
            return default;
        }

        return element.Deserialize<T>(options ?? DefaultOptions);
    }

    /// <summary>
    /// Deserialize a nullable payload into <typeparamref name="T"/> using a source-generated
    /// <see cref="JsonTypeInfo{T}"/> — the trimming- and AOT-safe path. Returns <c>default</c>
    /// when the payload is absent or JSON <c>null</c>.
    /// </summary>
    /// <typeparam name="T">Target type.</typeparam>
    /// <param name="payload">The payload element (e.g. <c>record.Payload</c>).</param>
    /// <param name="jsonTypeInfo">Type metadata from your own <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>.</param>
    public static T? As<T>(this JsonElement? payload, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        if (payload is not { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } element)
        {
            return default;
        }

        return element.Deserialize(jsonTypeInfo);
    }
}
