// Probe a base address and print whether it answered as a coordinator or a solo node.
//
//	INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... go run ./go/examples/nodeprobe
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

	probe, err := client.Probe(context.Background())
	if err != nil {
		log.Fatal(err)
	}

	switch probe.Kind {
	case inferhub.TargetHub:
		fmt.Printf("hub, coordinator %s, %d node(s)\n", probe.Version, len(probe.HubStatus.Nodes))
	case inferhub.TargetSoloNode:
		rerank := "n/a"
		if probe.NodeStatus.Retrieval != nil {
			rerank = probe.NodeStatus.Retrieval.Rerank // a string ("none"/"llm"), never a bool
		}
		fmt.Printf("solo node %q, version %s, retrieval rerank mode: %s\n",
			probe.NodeStatus.Name, probe.Version, rerank)
	}
}
