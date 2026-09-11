// Batch embeddings — POST /api/embed, a string or a []string.
//
//	INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... go run ./go/examples/embeddings
package main

import (
	"context"
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

	result, err := client.Embed(context.Background(), inferhub.EmbedRequest{
		Model: "nomic-embed-text:latest",
		Input: []string{"Payroll runs on the fifth working day.", "The office closes at six."},
	})
	if err != nil {
		log.Fatal(err)
	}

	dims := 0
	if len(result.Embeddings) > 0 {
		dims = len(result.Embeddings[0])
	}
	fmt.Printf("%d vectors, %d dims each\n", len(result.Embeddings), dims)
}
