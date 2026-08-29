using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.OpenAi;

/// <summary>Request body for <c>POST /v1/embeddings</c>.</summary>
public sealed class OpenAiEmbeddingsRequest
{
    /// <summary>Embedding model name. Required.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// Input text: a string or an array of strings. Token arrays are rejected by the hub — it has
    /// no tokenizer at the edge and guessing one would produce silently wrong vectors. Use
    /// <see cref="FromText"/> / <see cref="FromTexts"/>.
    /// </summary>
    [JsonPropertyName("input")]
    public JsonElement? Input { get; set; }

    /// <summary>
    /// <c>float</c> (default) or <c>base64</c>. Both come back in the same <c>embedding</c> field;
    /// <see cref="OpenAiEmbedding.AsFloats"/> decodes either.
    /// </summary>
    [JsonPropertyName("encoding_format")]
    public string? EncodingFormat { get; set; }

    /// <summary>Requested dimensionality, where the model supports it.</summary>
    [JsonPropertyName("dimensions")]
    public int? Dimensions { get; set; }

    /// <summary>Any other OpenAI-shaped field.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

    /// <summary>One model, one input string.</summary>
    /// <param name="model">Embedding model name.</param>
    /// <param name="input">Text to embed.</param>
    public static OpenAiEmbeddingsRequest FromText(string model, string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(input);
        return new OpenAiEmbeddingsRequest
        {
            Model = model,
            Input = JsonSerializer.SerializeToElement(input, Serialization.InferHubJsonContext.Default.String)
        };
    }

    /// <summary>One model, a batch of input strings.</summary>
    /// <param name="model">Embedding model name.</param>
    /// <param name="inputs">Texts to embed, in order; the answer keeps that order.</param>
    public static OpenAiEmbeddingsRequest FromTexts(string model, IEnumerable<string> inputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(inputs);
        return new OpenAiEmbeddingsRequest
        {
            Model = model,
            Input = JsonSerializer.SerializeToElement(inputs.ToArray(), Serialization.InferHubJsonContext.Default.StringArray)
        };
    }
}

/// <summary>Response for <c>POST /v1/embeddings</c>.</summary>
public sealed class OpenAiEmbeddingsResponse
{
    /// <summary>Always <c>list</c>.</summary>
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    /// <summary>Model that produced the vectors.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>One entry per input, in the order they were sent.</summary>
    [JsonPropertyName("data")]
    public IReadOnlyList<OpenAiEmbedding> Data { get; set; } = Array.Empty<OpenAiEmbedding>();

    /// <summary>Token counts for the embedded input.</summary>
    [JsonPropertyName("usage")]
    public OpenAiEmbeddingsUsage? Usage { get; set; }
}

/// <summary>One vector inside an <see cref="OpenAiEmbeddingsResponse"/>.</summary>
public sealed class OpenAiEmbedding
{
    /// <summary>Always <c>embedding</c>.</summary>
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    /// <summary>Position of the input this vector belongs to.</summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>
    /// The raw wire value: a JSON array of numbers under <c>encoding_format: float</c>, and a
    /// base64 string under <c>base64</c>. Call <see cref="AsFloats"/> rather than branching on it.
    /// </summary>
    [JsonPropertyName("embedding")]
    public JsonElement Embedding { get; set; }

    /// <summary>
    /// The vector, whichever encoding it arrived in. <c>base64</c> is little-endian float32 —
    /// the encoding the OpenAI Python SDK asks for by default, which makes it the common case
    /// rather than the exotic one.
    /// </summary>
    /// <exception cref="FormatException">The value is neither a number array nor a base64 float32 string.</exception>
    public float[] AsFloats() => Embedding.ValueKind switch
    {
        JsonValueKind.Array => FromArray(Embedding),
        JsonValueKind.String => FromBase64(Embedding.GetString() ?? string.Empty),
        _ => throw new FormatException(
            $"embedding arrived as {Embedding.ValueKind}, which is neither a float array nor a base64 string")
    };

    private static float[] FromArray(JsonElement array)
    {
        var values = new float[array.GetArrayLength()];
        var i = 0;

        foreach (var element in array.EnumerateArray())
        {
            values[i++] = element.GetSingle();
        }

        return values;
    }

    private static float[] FromBase64(string raw)
    {
        byte[] bytes;

        try
        {
            bytes = Convert.FromBase64String(raw);
        }
        catch (FormatException ex)
        {
            throw new FormatException("embedding was a string but not base64", ex);
        }

        if (bytes.Length % sizeof(float) != 0)
        {
            throw new FormatException(
                $"base64 embedding is {bytes.Length} bytes, which is not a whole number of float32 values");
        }

        var values = new float[bytes.Length / sizeof(float)];

        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float)));
        }

        return values;
    }
}

/// <summary>Token counts for an embeddings call.</summary>
public sealed class OpenAiEmbeddingsUsage
{
    /// <summary>Tokens in the input.</summary>
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    /// <summary>Total tokens billed — the same number, for this endpoint.</summary>
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
