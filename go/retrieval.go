package inferhub

import (
	"net/http"
	"strconv"
)

// RetrievalOptions is a call-scoped concern, never a field on ChatRequest/GenerateRequest (D1 in
// plans/phase-23-go-retrieval.md — the variadic-trailing-parameter shape that keeps every v0.1.0
// call site compiling unchanged). Passing one to Chat/ChatStream/Generate/GenerateStream builds
// the five X-InferHub-Retrieve*/X-InferHub-Rerank headers for that call only.
type RetrievalOptions struct {
	Collection string
	K          *int
	Model      string
	Mode       string
	Rerank     *bool
}

// firstRetrievalOptions returns the zero value and false when the variadic tail is empty, or the
// first (and only meaningful) element otherwise — every Chat/Generate variant uses this to read
// its trailing "...RetrievalOptions" parameter.
func firstRetrievalOptions(opts []RetrievalOptions) (RetrievalOptions, bool) {
	if len(opts) == 0 {
		return RetrievalOptions{}, false
	}
	return opts[0], true
}

// buildRetrievalHeaders mirrors python's _build_retrieval_headers / js's buildRetrievalHeaders.
func buildRetrievalHeaders(opts []RetrievalOptions, h http.Header) {
	options, ok := firstRetrievalOptions(opts)
	if !ok {
		return
	}
	h.Set("X-InferHub-Retrieve", options.Collection)
	if options.K != nil {
		h.Set("X-InferHub-Retrieve-K", strconv.Itoa(*options.K))
	}
	if options.Model != "" {
		h.Set("X-InferHub-Retrieve-Model", options.Model)
	}
	if options.Mode != "" {
		h.Set("X-InferHub-Retrieve-Mode", options.Mode)
	}
	if options.Rerank != nil {
		h.Set("X-InferHub-Rerank", strconv.FormatBool(*options.Rerank))
	}
}
