package inferhub

import "fmt"

// Error model — mirrors python _exceptions.py's and js errors.ts's shape (itself a port of the C#
// client's). Root CLAUDE.md rule 9: which envelope arrived decides the exception type, never which
// method was called. /api/* answers {"error":"..."} (a plain string); /v1/* and routes that reuse
// its shape answer {"error":{"message":...,"type":...,"param":...,"code":...}}. A 424 is always
// Kind == KindRetrieval, in either dialect.
//
// D4 (plans/phase-22-go-core.md) — one flat *Error with a Kind discriminator, not three types in an
// embedding "hierarchy". python/js express "RetrievalError extends InferHubError" with `extends`;
// the direct Go translation is an anonymous-embedded *Error inside a RetrievalError/OpenAIError
// struct. That translation has a real trap: naming the base type Error (the idiomatic Go choice —
// callers write inferhub.Error, not the stuttering inferhub.InferHubError) means an embedded *Error
// field's implicit name is also "Error", which collides with the *Error.Error() method promoted
// from the same embedding at one level deeper. Go resolves the shallower selector (the field) and
// the promoted method is shadowed — so *RetrievalError would silently fail to implement the `error`
// interface, a compile error at every call site that returns one as `error`. That bug is exactly
// the kind this package cannot catch by compiling (no local Go toolchain — see the phase's
// verification section), so the type-hierarchy translation is rejected in favor of the flatter,
// lower-risk shape: one struct, one Error() method, a Kind field instead of a type switch. A caller
// asks "is this a retrieval error" with `err.Kind == inferhub.KindRetrieval` instead of a type
// assertion — different idiom, same information, and it is arguably more idiomatic Go besides: the
// standard library itself prefers a sentinel/kind check (os.IsNotExist, errors.Is) over a type
// hierarchy for exactly this class of "same error, different reason" case.
// Considered and rejected: renaming the base type to avoid the collision (e.g. APIError) and
// keeping three embedded types — works, but changes the public name for no reader-facing benefit
// over the Kind field, and does not remove the same footgun for any future subclass this package
// grows in 0.2.0/1.0.0.

// ErrorKind discriminates which envelope produced an *Error.
type ErrorKind int

const (
	// KindPlain is the Ollama dialect's {"error":"..."} envelope, or a body that was not JSON at
	// all (the raw text is used as Message).
	KindPlain ErrorKind = iota
	// KindRetrieval is HTTP 424 — retrieval was asked for (X-InferHub-Retrieve) and is unavailable.
	// The chat/generate call itself could have succeeded; only the retrieval step it depended on
	// could not.
	KindRetrieval
	// KindOpenAI is the {"error":{"message":...,"type":...,"param":...,"code":...}} envelope used
	// by /v1/* and routes that reuse its shape. Code/Param/Type are populated only for this kind.
	KindOpenAI
)

func (k ErrorKind) String() string {
	switch k {
	case KindRetrieval:
		return "retrieval"
	case KindOpenAI:
		return "openai"
	default:
		return "plain"
	}
}

// Error is returned when the coordinator (or a solo node) answers a non-success HTTP status, or
// when an NDJSON stream carries a mid-stream terminal error chunk. It is always this one type;
// Kind says which envelope produced it (see ErrorKind).
type Error struct {
	// StatusCode is the raw HTTP status — 404 (model missing), 401/403 (auth), 503 (temporary, see
	// RetryAfter), etc. A mid-stream NDJSON terminal error carries 200 (the HTTP response itself
	// succeeded; the failure is inside the stream).
	StatusCode int
	// Message is the human-readable error text extracted from the response body.
	Message string
	// ResponseBody is the raw response body, for a caller who wants more than Message.
	ResponseBody string
	// RetryAfter is seconds to wait before retrying, from a Retry-After header. nil when the hub
	// did not send one (an HTTP-date form exists but the hub always writes delta-seconds).
	RetryAfter *float64
	// Kind says which envelope produced this error.
	Kind ErrorKind
	// Code, Param, Type are populated only when Kind == KindOpenAI — the OpenAI dialect's
	// machine-readable error code (e.g. "capability_unavailable", retryable via RetryAfter), the
	// request field it names, and its error type string.
	Code  string
	Param string
	Type  string
}

func (e *Error) Error() string {
	return fmt.Sprintf("inferhub: status %d: %s", e.StatusCode, e.Message)
}

// IsRetrieval reports whether this is a 424 retrieval-unavailable error (Kind == KindRetrieval). A
// nil-safe convenience for the common `errors.As` + Kind check.
func (e *Error) IsRetrieval() bool { return e != nil && e.Kind == KindRetrieval }

// IsOpenAI reports whether this came from the {"error":{...}} envelope (Kind == KindOpenAI).
func (e *Error) IsOpenAI() bool { return e != nil && e.Kind == KindOpenAI }

func newError(statusCode int, message, body string, retryAfter *float64) *Error {
	return &Error{StatusCode: statusCode, Message: message, ResponseBody: body, RetryAfter: retryAfter, Kind: KindPlain}
}

func newRetrievalError(statusCode int, message, body string, retryAfter *float64) *Error {
	return &Error{StatusCode: statusCode, Message: message, ResponseBody: body, RetryAfter: retryAfter, Kind: KindRetrieval}
}

func newOpenAIError(statusCode int, message, body string, retryAfter *float64, code, param, errType string) *Error {
	return &Error{
		StatusCode:   statusCode,
		Message:      message,
		ResponseBody: body,
		RetryAfter:   retryAfter,
		Kind:         KindOpenAI,
		Code:         code,
		Param:        param,
		Type:         errType,
	}
}
