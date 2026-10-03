# Go out-of-process plugin

`main.go` is a presenter that reverses the text the device sent (`hello` shows as `olleh`). It scans stdin line by line, unmarshals each into a small struct, decodes the hex with `encoding/hex`, and prints one JSON reply per request. Run it with `go run` (no module file needed for a single file).

```
go run examples/go/out-of-process-plugin/main.go
{"type":"hello"}
{"type":"render","hex":"68656C6C6F"}
```

## The protocol

dev-term starts the program as a child process and talks to it in JSON lines, one object per line, one reply per request, on stdin and stdout. stderr is the plugin's own log.

1. `{"type":"hello"}` is answered with `{"type":"hello","name":"<name>","protocol":1}`. A plugin that does not answer, or answers another protocol, is rejected at start.
2. `{"type":"render","hex":"68656C6C6F"}` carries the bytes the device sent, as hex. The reply is `{"type":"output","lines":["..."]}`, zero or more lines to show.

The host waits at most a reply timeout for each answer. A plugin that exits, goes silent or answers bad JSON is marked faulted and shows nothing from then on; the session carries on. Host side: `ExternalProcessPresenter` in `src/DevTerm.Core/Plugins`; tests: `ExternalProcessPresenterTests`. Full design: [out-of-process-plugins](../../../docs/design/proposals/out-of-process-plugins.md).

