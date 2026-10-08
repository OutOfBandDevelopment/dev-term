namespace DevTerm.Web;

/// <summary>
/// The host's streaming channels described as AsyncAPI 3.0 (served at <c>/asyncapi.json</c>): the text WebSocket per session
/// and the Server-Sent Events stream. OpenAPI cannot express either, so this is written out by hand; keep it in step with
/// <see cref="WebSocketTunnel"/> and <see cref="HostEvents"/>.
/// </summary>
internal static class AsyncApiDocument
{
    public const string Json = """
    {
      "asyncapi": "3.0.0",
      "info": { "title": "dev-term web host streams", "version": "1.0.0", "description": "Every channel needs the host token (Bearer header, devterm_auth cookie or ?token=). A read-only token may receive but not send." },
      "servers": { "host": { "host": "127.0.0.1:5080", "protocol": "ws" } },
      "channels": {
        "mainSession": {
          "address": "/ws",
          "messages": { "line": { "$ref": "#/components/messages/line" } },
          "description": "Text WebSocket for the shared session. A frame received = one typed line to send; a frame sent = one output or status line (an '!' prefix marks an error)."
        },
        "openedSession": {
          "address": "/ws/{id}",
          "parameters": { "id": { "description": "The id returned by POST /api/connections" } },
          "messages": { "line": { "$ref": "#/components/messages/line" } },
          "description": "The same protocol for one connection opened from the project file."
        },
        "events": {
          "address": "/api/events",
          "messages": {
            "connectionOpened": { "$ref": "#/components/messages/connectionOpened" },
            "connectionClosed": { "$ref": "#/components/messages/connectionClosed" },
            "lineEvent": { "$ref": "#/components/messages/lineEvent" }
          },
          "description": "Server-Sent Events (text/event-stream). Each subscriber has a 256-event queue that drops its oldest."
        }
      },
      "operations": {
        "sendLine": { "action": "send", "channel": { "$ref": "#/channels/mainSession" } },
        "receiveLine": { "action": "receive", "channel": { "$ref": "#/channels/mainSession" } },
        "receiveEvents": { "action": "receive", "channel": { "$ref": "#/channels/events" } }
      },
      "components": {
        "messages": {
          "line": { "contentType": "text/plain", "payload": { "type": "string" } },
          "connectionOpened": { "name": "connection-opened", "payload": { "type": "object", "properties": { "id": { "type": "string" }, "name": { "type": "string" } } } },
          "connectionClosed": { "name": "connection-closed", "payload": { "type": "object", "properties": { "id": { "type": "string" } } } },
          "lineEvent": { "name": "line", "payload": { "type": "object", "properties": { "id": { "type": "string", "description": "main for the shared session" }, "text": { "type": "string" } } } }
        }
      }
    }
    """;
}
