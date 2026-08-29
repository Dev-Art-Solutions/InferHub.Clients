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
