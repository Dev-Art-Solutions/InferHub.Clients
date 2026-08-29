using System.Text.Json;
using InferHub.Client.Exceptions;

namespace InferHub.Client.Http;

/// <summary>
/// Shared response plumbing for <see cref="InferHubClient"/>, <see cref="InferHubAdminClient"/>
/// and <see cref="InferHubOpenAiClient"/>: non-success → typed <see cref="InferHubException"/>
/// with the hub's error message extracted from whichever envelope it arrived in.
/// </summary>
/// <remarks>
/// The hub speaks two dialects and they fail differently. The Ollama surface answers
/// <c>{"error":"…"}</c>; <c>/v1/*</c> answers <c>{"error":{"message":…,"type":…}}</c>, because an
/// OpenAI SDK reads <c>error.message</c> to build the exception it raises. Which envelope arrived
/// decides the exception type — not which method was called — so a hub that starts answering a
/// path in the other dialect still produces a usable message rather than a wall of JSON.
/// </remarks>
internal static class InferHubResponse
{
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var openAi = TryReadOpenAiError(body);
        var message = openAi?.Message
            ?? TryExtractErrorMessage(body)
            ?? $"InferHub request failed with status {(int)response.StatusCode} ({response.StatusCode}).";

        // 424 first, in both dialects: "retrieval was asked for and could not be satisfied" is one
        // condition, and a caller who catches it should not have to catch it twice because the
        // answer came back through /v1.
        if (response.StatusCode == System.Net.HttpStatusCode.FailedDependency)
        {
            throw new InferHubRetrievalException(message, body);
        }

        if (openAi is { } error)
        {
            throw new InferHubOpenAiException(
                response.StatusCode,
                message,
                body,
                error.Type,
                error.Code,
                error.Param);
        }

        throw new InferHubException(response.StatusCode, message, body);
    }

    public static string? TryExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var errorElement)
                && errorElement.ValueKind == JsonValueKind.String)
            {
                return errorElement.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON body — fall through to the raw string.
        }

        return body;
    }

    /// <summary>
    /// Reads the OpenAI error envelope, or returns <c>null</c> when the body is not one. The
    /// <c>code</c> field is accepted as a string <em>or</em> a number: this project writes strings,
    /// and a provider error the hub passed through can carry <c>429</c>.
    /// </summary>
    public static OpenAiError? TryReadOpenAiError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new OpenAiError(
                ReadString(error, "message"),
                ReadString(error, "type"),
                ReadScalar(error, "code"),
                ReadString(error, "param"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadScalar(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    /// <summary>The parts of an OpenAI error envelope a caller can act on.</summary>
    public readonly record struct OpenAiError(string? Message, string? Type, string? Code, string? Param);
}
