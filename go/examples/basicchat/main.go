// Blocking chat against a coordinator (or a solo node — same address, same client).
//
//	INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... go run ./go/examples/basicchat
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

	answer, err := client.Chat(context.Background(), inferhub.ChatRequest{
		Model:    "llama3",
		Messages: []inferhub.ChatMessage{{Role: "user", Content: "Say hi in one word."}},
	})
	if err != nil {
		log.Fatal(err)
	}

	if answer.Message != nil {
		fmt.Println(answer.Message.Content)
	}
}
