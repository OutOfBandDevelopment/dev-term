// Example out-of-process dev-term presenter in Go: reverses the text the device sent.
// Protocol (JSON lines on stdin/stdout): see docs/design/proposals/out-of-process-plugins.md.
// Run: go run examples/go/out-of-process-plugin/main.go
package main

import (
	"bufio"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"os"
)

type message struct {
	Type string `json:"type"`
	Hex  string `json:"hex"`
}

func main() {
	in := bufio.NewScanner(os.Stdin)
	for in.Scan() {
		var m message
		if err := json.Unmarshal(in.Bytes(), &m); err != nil {
			continue
		}
		switch m.Type {
		case "hello":
			fmt.Println(`{"type":"hello","name":"go-reverse","protocol":1}`)
		case "render":
			raw, _ := hex.DecodeString(m.Hex)
			for i, j := 0, len(raw)-1; i < j; i, j = i+1, j-1 {
				raw[i], raw[j] = raw[j], raw[i]
			}
			out, _ := json.Marshal(map[string]any{"type": "output", "lines": []string{string(raw)}})
			fmt.Println(string(out))
		default:
			fmt.Println(`{"type":"output","lines":[]}`)
		}
	}
}
