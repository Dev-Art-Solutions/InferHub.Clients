using System.Globalization;
using System.Text.Json;

namespace InferHub.Client.Http;

/// <summary>
/// The <c>X-InferHub-*</c> header contract, in one place because both dialects share it: the
/// retrieval, conversation and provider headers are read by <c>/api/chat</c> and
/// <c>/v1/chat/completions</c> alike, and the answer comes back with the same two response headers
/// either way.
/// </summary>
internal static class InferHubHeaders
{
    public const string Conversation = "X-InferHub-Conversation";
    public const string Retrieve = "X-InferHub-Retrieve";
    public const string RetrieveK = "X-InferHub-Retrieve-K";
    public const string RetrieveModel = "X-InferHub-Retrieve-Model";
    public const string Provider = "X-InferHub-Provider";
    public const string ServedBy = "X-InferHub-Served-By";
    public const string Sources = "X-InferHub-Sources";

    /// <summary>The rate the speech worker measured off its own first samples. Streamed answers only.</summary>
    public const string AudioSampleRate = "X-InferHub-Audio-Sample-Rate";

    /// <summary>What a synthesis was metered in: input characters, not tokens. Streamed answers only.</summary>
    public const string SpeechCharacters = "X-InferHub-Speech-Characters";

    /// <summary>The value of <see cref="Provider"/> that means "the fleet, and no vendor, for this request".</summary>
    public const string FleetOnlyProvider = "node";

    public static void Apply(HttpRequestMessage request, InferHubCallOptions? options)
    {
        if (options is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.ConversationId))
        {
            request.Headers.TryAddWithoutValidation(Conversation, options.ConversationId);
        }

        // Both spellings of the steer set one header, so a caller who set both is asking for two
        // different things at once. Throwing beats picking a winner: the losing intent here is
        // "keep this prompt off somebody else's servers".
        if (options.FleetOnly && !string.IsNullOrWhiteSpace(options.Provider))
        {
            throw new ArgumentException(
                $"{nameof(InferHubCallOptions.FleetOnly)} and {nameof(InferHubCallOptions.Provider)} both set the "
                + $"{Provider} header and cannot be combined.",
                nameof(options));
        }

        if (options.FleetOnly)
        {
            request.Headers.TryAddWithoutValidation(Provider, FleetOnlyProvider);
        }
        else if (!string.IsNullOrWhiteSpace(options.Provider))
        {
            request.Headers.TryAddWithoutValidation(Provider, options.Provider);
        }

        var retrieval = options.Retrieval;
        if (retrieval is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(retrieval.Collection))
        {
            throw new ArgumentException("RetrievalOptions.Collection is required.", nameof(options));
        }

        request.Headers.TryAddWithoutValidation(Retrieve, retrieval.Collection);

        if (retrieval.K is int k)
        {
            request.Headers.TryAddWithoutValidation(RetrieveK, k.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(retrieval.Model))
        {
            request.Headers.TryAddWithoutValidation(RetrieveModel, retrieval.Model);
        }
    }

    /// <summary>
    /// Which node or provider answered — a node id, or <c>provider:&lt;id&gt;</c>. <c>null</c> when
    /// the header is absent, which is a real answer for the endpoints that do not set it (embeddings,
    /// the model list). Never substituted with a placeholder, and never acted on.
    /// </summary>
    public static string? ReadServedBy(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(ServedBy, out var values))
        {
            return null;
        }

        var raw = string.Concat(values).Trim();
        return raw.Length == 0 ? null : raw;
    }

    /// <summary>
    /// A header the hub writes as a plain integer, or <c>null</c> when it is absent or unreadable.
    /// Absent is a real answer: the audio headers ride on streamed responses only, and a zero
    /// invented to fill the field would be a measurement nobody took.
    /// </summary>
    public static long? ReadInt64(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        return long.TryParse(
            string.Concat(values).Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// A header the hub writes as a decimal number, parsed invariantly. The hub formats it
    /// invariantly for the same reason: a decimal comma is a bug that only appears on a Bulgarian or
    /// German host, and a header is parsed by somebody else's client.
    /// </summary>
    public static double? ReadDouble(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        return double.TryParse(
            string.Concat(values).Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>A header read verbatim, or <c>null</c> when it is absent or blank.</summary>
    public static string? ReadString(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        var raw = string.Concat(values).Trim();
        return raw.Length == 0 ? null : raw;
    }

    public static IReadOnlyList<string>? ParseSourceIds(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(Sources, out var values))
        {
            return null;
        }

        var raw = string.Concat(values).Trim();
        if (raw.Length == 0)
        {
            return Array.Empty<string>();
        }

        // The coordinator echoes a JSON array: X-InferHub-Sources: ["id", "id2"].
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var ids = new List<string>(doc.RootElement.GetArrayLength());
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var id = element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
                    if (!string.IsNullOrEmpty(id))
                    {
                        ids.Add(id);
                    }
                }

                return ids;
            }
        }
        catch (JsonException)
        {
            // Not a JSON array — fall back to a comma-separated list.
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
