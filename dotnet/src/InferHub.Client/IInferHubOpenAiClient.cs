using InferHub.Client.Models.OpenAi;

namespace InferHub.Client;

/// <summary>
/// Client for the hub's <b>OpenAI-compatible</b> surface — <c>/v1/chat/completions</c>,
/// <c>/v1/completions</c>, <c>/v1/embeddings</c> and <c>/v1/models</c>. Same hub, same base
/// address, same client API key as <see cref="IInferHubClient"/>: this is a second dialect for
/// the same fleet, not a second server.
/// </summary>
/// <remarks>
/// <para>
/// It is a separate interface rather than more methods on <see cref="IInferHubClient"/> because
/// that one is published at 1.0 and adding a member to it would break every caller holding a test
/// double or a decorator.
/// </para>
/// <para>
/// The per-call <see cref="InferHubCallOptions"/> are the same object both dialects use, so
/// retrieval, sticky conversations and the provider steer work here exactly as they do on
/// <c>/api/chat</c>. Failures arrive in the OpenAI error envelope and surface as
/// <see cref="Exceptions.InferHubOpenAiException"/>, which carries <c>type</c>, <c>code</c> and
/// <c>param</c> alongside the message.
/// </para>
/// </remarks>
public interface IInferHubOpenAiClient
{
    /// <summary>
    /// Blocking chat completion — <c>POST /v1/chat/completions</c> with <c>stream:false</c>.
    /// </summary>
    /// <param name="request">Request body. <see cref="ChatCompletionRequest.Stream"/> is forced to <c>false</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ChatCompletionResponse> CreateChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Blocking chat completion with per-call options — retrieval, sticky conversation, and the
    /// provider steer (<c>X-InferHub-Provider</c>). The answer carries
    /// <see cref="ChatCompletionResponse.ServedBy"/> and, when retrieval was asked for,
    /// <see cref="ChatCompletionResponse.SourceIds"/>.
    /// </summary>
    /// <param name="request">Request body. <see cref="ChatCompletionRequest.Stream"/> is forced to <c>false</c>.</param>
    /// <param name="options">Per-call options; <c>null</c> for a plain call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ChatCompletionResponse> CreateChatCompletionAsync(ChatCompletionRequest request, InferHubCallOptions? options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streaming chat completion — <c>POST /v1/chat/completions</c> with <c>stream:true</c>, read as
    /// server-sent events. Yields one <see cref="ChatCompletionChunk"/> per frame and stops at the
    /// <c>data: [DONE]</c> sentinel. A frame with an empty <c>choices</c> list is the usage frame
    /// requested by <c>stream_options.include_usage</c> and is yielded like any other.
    /// </summary>
    /// <param name="request">Request body. <see cref="ChatCompletionRequest.Stream"/> is forced to <c>true</c>.</param>
    /// <param name="cancellationToken">Cancels the read loop.</param>
    IAsyncEnumerable<ChatCompletionChunk> StreamChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streaming chat completion with per-call options. Every yielded chunk carries the same
    /// <see cref="ChatCompletionChunk.ServedBy"/> and <see cref="ChatCompletionChunk.SourceIds"/>,
    /// read once from the response headers before the first frame.
    /// </summary>
    /// <param name="request">Request body. <see cref="ChatCompletionRequest.Stream"/> is forced to <c>true</c>.</param>
    /// <param name="options">Per-call options; <c>null</c> for a plain call.</param>
    /// <param name="cancellationToken">Cancels the read loop.</param>
    IAsyncEnumerable<ChatCompletionChunk> StreamChatCompletionAsync(ChatCompletionRequest request, InferHubCallOptions? options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Blocking legacy completion — <c>POST /v1/completions</c> with <c>stream:false</c>. Maps to
    /// the fleet's <c>generate</c> work, so it takes a prompt rather than messages.
    /// </summary>
    /// <param name="request">Request body. <see cref="CompletionRequest.Stream"/> is forced to <c>false</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CompletionResponse> CreateCompletionAsync(CompletionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Blocking legacy completion with per-call options. <c>/v1/completions</c> honours the
    /// provider steer; it does <b>not</b> take retrieval — the hub grounds chat, not raw
    /// completions — so a <see cref="RetrievalOptions"/> here reaches a hub that ignores it.
    /// </summary>
    /// <param name="request">Request body. <see cref="CompletionRequest.Stream"/> is forced to <c>false</c>.</param>
    /// <param name="options">Per-call options; <c>null</c> for a plain call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CompletionResponse> CreateCompletionAsync(CompletionRequest request, InferHubCallOptions? options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streaming legacy completion — <c>POST /v1/completions</c> with <c>stream:true</c>. Legacy
    /// completions use one shape for both modes, so this yields <see cref="CompletionResponse"/>
    /// once per frame, each carrying the increment in <see cref="CompletionChoice.Text"/>.
    /// </summary>
    /// <param name="request">Request body. <see cref="CompletionRequest.Stream"/> is forced to <c>true</c>.</param>
    /// <param name="cancellationToken">Cancels the read loop.</param>
    IAsyncEnumerable<CompletionResponse> StreamCompletionAsync(CompletionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Streaming legacy completion with per-call options.</summary>
    /// <param name="request">Request body. <see cref="CompletionRequest.Stream"/> is forced to <c>true</c>.</param>
    /// <param name="options">Per-call options; <c>null</c> for a plain call.</param>
    /// <param name="cancellationToken">Cancels the read loop.</param>
    IAsyncEnumerable<CompletionResponse> StreamCompletionAsync(CompletionRequest request, InferHubCallOptions? options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Embeddings in the OpenAI dialect — <c>POST /v1/embeddings</c>. Ask for <c>float</c> or
    /// <c>base64</c> with <see cref="OpenAiEmbeddingsRequest.EncodingFormat"/>;
    /// <see cref="OpenAiEmbedding.AsFloats"/> decodes either. There is no options overload: this
    /// endpoint dispatches to an embedding node and reads neither the steer nor the retrieval
    /// headers.
    /// </summary>
    /// <param name="request">Request body. <see cref="OpenAiEmbeddingsRequest.Model"/> is required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OpenAiEmbeddingsResponse> CreateEmbeddingsAsync(OpenAiEmbeddingsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// List models in the OpenAI dialect — <c>GET /v1/models</c>. Each entry carries the hub's
    /// <see cref="OpenAiModel.Capabilities"/> extension: what the fleet will actually do with that
    /// model, rather than only that it exists.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<OpenAiModel>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch one model — <c>GET /v1/models/{id}</c>. Returns <c>null</c> when the hub does not
    /// serve it, because "no such model" is an answer rather than a failure.
    /// </summary>
    /// <param name="id">Model name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OpenAiModel?> GetModelAsync(string id, CancellationToken cancellationToken = default);
}
