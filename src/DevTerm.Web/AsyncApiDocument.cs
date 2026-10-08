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

    /// <summary>A dependency-free page (no CDN, so it works on an offline bench) that fetches <c>/asyncapi.json</c> and lists its channels, operations and messages.</summary>
    public const string Viewer = """
    <!doctype html>
    <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
    <title>dev-term streams</title>
    <style>
    :root { color-scheme: light dark; --bg: #fff; --fg: #1d2330; --muted: #5d6677; --line: #d5dae3; --code: #eef1f6; }
    @media (prefers-color-scheme: dark) { :root { --bg: #14171f; --fg: #e4e8f0; --muted: #98a2b3; --line: #2c3340; --code: #1e2330; } }
    body { margin: 0; padding: 24px 16px; background: var(--bg); color: var(--fg); font: 15px/1.5 system-ui, sans-serif; }
    main { max-width: 860px; margin: 0 auto; }
    h1 { font-size: 1.5rem; margin: 0 0 4px; } h2 { font-size: 1.1rem; margin: 28px 0 8px; border-bottom: 1px solid var(--line); padding-bottom: 4px; }
    .muted { color: var(--muted); } code, pre { background: var(--code); border-radius: 4px; font: 13px ui-monospace, Consolas, monospace; }
    code { padding: 1px 5px; } pre { padding: 10px; overflow-x: auto; margin: 6px 0; }
    section { margin: 12px 0; } .tag { font-size: 12px; border: 1px solid var(--line); border-radius: 10px; padding: 0 8px; margin-left: 6px; }
    </style></head><body><main id="app"><p class="muted">Loading /asyncapi.json ...</p></main>
    <script>
    const el = (tag, text, cls) => { const e = document.createElement(tag); if (text !== undefined) e.textContent = text; if (cls) e.className = cls; return e; };
    fetch('/asyncapi.json').then(r => r.json()).then(doc => {
      const app = document.getElementById('app'); app.replaceChildren();
      app.append(el('h1', doc.info.title), el('p', 'AsyncAPI ' + doc.asyncapi + ' - v' + doc.info.version, 'muted'), el('p', doc.info.description || ''));
      app.append(el('h2', 'Channels'));
      for (const [name, ch] of Object.entries(doc.channels || {})) {
        const s = el('section'); const h = el('div'); h.append(el('code', ch.address), el('span', name, 'tag')); s.append(h, el('div', ch.description || '', 'muted'));
        for (const m of Object.values(ch.messages || {})) {
          const key = (m.$ref || '').split('/').pop(); const msg = (doc.components.messages || {})[key] || m;
          s.append(el('div', 'Message ' + (msg.name || key) + (msg.contentType ? ' (' + msg.contentType + ')' : '')), el('pre', JSON.stringify(msg.payload, null, 2)));
        }
        app.append(s);
      }
      app.append(el('h2', 'Operations'));
      for (const [name, op] of Object.entries(doc.operations || {})) { const s = el('section'); s.append(el('code', op.action), document.createTextNode(' ' + name + ' on ' + op.channel.$ref.split('/').pop())); app.append(s); }
    }).catch(e => { document.getElementById('app').textContent = 'Could not load /asyncapi.json: ' + e; });
    </script></body></html>
    """;
}
