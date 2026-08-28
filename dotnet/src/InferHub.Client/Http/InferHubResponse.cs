using System.Text.Json;
using InferHub.Client.Exceptions;

namespace InferHub.Client.Http;

/// <summary>
/// Shared response plumbing for <see cref="InferHubClient"/> and
/// <see cref="InferHubAdminClient"/>: non-success → typed <see cref="InferHubException"/>
/// with the coordinator's <c>{ "error": … }</c> message extracted.
/// </summary>
internal static class InferHubResponse
{
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = TryExtractErrorMessage(body) ?? $"InferHub request failed with status {(int)response.StatusCode} ({response.StatusCode}).";

        if (response.StatusCode == System.Net.HttpStatusCode.FailedDependency)
        {
            throw new InferHubRetrievalException(message, body);
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
}
