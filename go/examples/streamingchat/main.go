// Streaming chat — one ChatResponse per NDJSON line, printed as it arrives.
//
//	INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... go run ./go/examples/streamingchat
package main

import (
	"context"
	"errors"
	"fmt"
	"log"
	"os"

	inferhub "github.com/Dev-Art-Solutions/InferHub.Clients/go"
)

func main() {
	baseURL := os.Getenv("INFERHUB_BASE")
	if baseURL == "" {
		baseURL = inferhub.DefaultBaseURL
	}
	apiKey := os.Getenv("INFERHUB_API_KEY")

	client, err := inferhub.NewClient(inferhub.ClientOptions{BaseURL: baseURL, APIKey: apiKey})
	if err != nil {
		log.Fatal(err)
	}

	stream, err := client.ChatStream(context.Background(), inferhub.ChatRequest{
		Model:    "llama3",
		Messages: []inferhub.ChatMessage{{Role: "user", Content: "Count from one to five."}},
	})
	if err != nil {
		log.Fatal(err)
	}
	defer stream.Close()

	for stream.Next() {
		chunk := stream.Value()
		if chunk.Message != nil {
			fmt.Print(chunk.Message.Content)
		}
		if chunk.Done != nil && *chunk.Done {
			fmt.Printf("\n\n(served by: %s)\n", chunk.ServedBy)
		}
	}

	// A mid-stream terminal error ({"error": ..., "done": true}) surfaces here instead of the loop
	// hanging or ending quietly with a partial answer nobody was told about.
	var inferErr *inferhub.Error
	if err := stream.Err(); err != nil {
		if errors.As(err, &inferErr) {
			fmt.Printf("\nstream terminated: %s\n", inferErr.Message)
			return
		}
		log.Fatal(err)
	}
}
