# Python out-of-process plugin

`shout.py` is a presenter that upper-cases the text a device sent (`hello` shows as `HELLO`). It reads each line from stdin with `json.loads`, decodes the hex payload with `bytes.fromhex`, and prints one JSON reply per request with `flush=True` so the host is never left waiting on a buffer.

Needs Python 3 only (standard library). Try it by hand:

```
python examples/python/out-of-process-plugin/shout.py
{"type":"hello"}
{"type":"render","hex":"68656C6C6F"}
```

## The protocol

dev-term starts the program as a child process and talks to it in JSON lines, one object per line, one reply per request, on stdin and stdout. stderr is the plugin's own log.

1. `{"type":"hello"}` is answered with `{"type":"hello","name":"<name>","protocol":1}`. A plugin that does not answer, or answers another protocol, is rejected at start.
2. `{"type":"render","hex":"68656C6C6F"}` carries the bytes the device sent, as hex. The reply is `{"type":"output","lines":["..."]}`, zero or more lines to show.

The host waits at most a reply timeout for each answer. A plugin that exits, goes silent or answers bad JSON is marked faulted and shows nothing from then on; the session carries on. Host side: `ExternalProcessPresenter` in `src/DevTerm.Core/Plugins`; tests: `ExternalProcessPresenterTests`. Full design: [out-of-process-plugins](../../../docs/design/proposals/out-of-process-plugins.md).

