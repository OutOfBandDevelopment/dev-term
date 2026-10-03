# Java out-of-process plugin

`Shout.java` is a presenter that reports how many bytes the device sent (`hello` shows as `5 bytes`). It is a single source file run directly with `java Shout.java` (JDK 11 or later, no build step). It reads lines from stdin and finds the hex payload with plain string checks so it needs no JSON library; a real plugin would use one. Each reply is printed and flushed.

```
java examples/java/out-of-process-plugin/Shout.java
{"type":"hello"}
{"type":"render","hex":"68656C6C6F"}
```

## The protocol

dev-term starts the program as a child process and talks to it in JSON lines, one object per line, one reply per request, on stdin and stdout. stderr is the plugin's own log.

1. `{"type":"hello"}` is answered with `{"type":"hello","name":"<name>","protocol":1}`. A plugin that does not answer, or answers another protocol, is rejected at start.
2. `{"type":"render","hex":"68656C6C6F"}` carries the bytes the device sent, as hex. The reply is `{"type":"output","lines":["..."]}`, zero or more lines to show.

The host waits at most a reply timeout for each answer. A plugin that exits, goes silent or answers bad JSON is marked faulted and shows nothing from then on; the session carries on. Host side: `ExternalProcessPresenter` in `src/DevTerm.Core/Plugins`; tests: `ExternalProcessPresenterTests`. Full design: [out-of-process-plugins](../../../docs/design/proposals/out-of-process-plugins.md).

