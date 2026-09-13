// Ingest a couple of documents, search them, then ask a grounded question and print which
// documents answered it.
//
//	INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... go run ./go/examples/minirag
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
	collection := os.Getenv("INFERHUB_COLLECTION")
	if collection == "" {
		collection = "mini-rag-example"
	}

	client, err := inferhub.NewClient(inferhub.ClientOptions{BaseURL: baseURL, APIKey: apiKey})
	if err != nil {
		log.Fatal(err)
	}

	ctx := context.Background()

	if _, err := client.IngestText(ctx, collection, inferhub.TextDocument{
		ID: "payroll-policy", Text: "Payroll runs on the fifth working day.",
	}); err != nil {
		log.Fatal(err)
	}
	if _, err := client.IngestText(ctx, collection, inferhub.TextDocument{
		ID: "onboarding", Text: "New hires get their laptop on their first day.",
	}); err != nil {
		log.Fatal(err)
	}

	found, err := client.Search(ctx, collection, inferhub.SearchRequest{Query: "When does payroll run?"})
	if err != nil {
		log.Fatal(err)
	}
	for _, hit := range found.Hits {
		fmt.Printf("%s (score %.3f): %s\n", hit.DocumentID, hit.Score, hit.Text)
	}

	k := 3
	answer, err := client.Chat(ctx, inferhub.ChatRequest{
		Model:    "llama3",
		Messages: []inferhub.ChatMessage{{Role: "user", Content: "When does payroll run?"}},
	}, inferhub.RetrievalOptions{Collection: collection, K: &k})
	if err != nil {
		log.Fatal(err)
	}
	if answer.Message != nil {
		fmt.Println(answer.Message.Content)
	}
	fmt.Println("answered from:", answer.SourceIDs)
}
