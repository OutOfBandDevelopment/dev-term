namespace DevTerm.Web;

/// <summary>The single-page terminal: an output pane and a send box talking to <c>/ws</c>.</summary>
internal static class TerminalPage
{
    public const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>dev-term</title>
        <style>
          :root { color-scheme: light dark; --bg: #fff; --fg: #1b1f23; --dim: #6a737d; --bar: #f1f3f5; }
          @media (prefers-color-scheme: dark) { :root { --bg: #111418; --fg: #d7dde3; --dim: #8b949e; --bar: #1c2128; } }
          html, body { height: 100%; margin: 0; background: var(--bg); color: var(--fg); font: 14px/1.4 ui-monospace, Consolas, monospace; }
          body { display: flex; flex-direction: column; }
          header { padding: 6px 12px; background: var(--bar); color: var(--dim); }
          #out { flex: 1; overflow: auto; margin: 0; padding: 8px 12px; white-space: pre-wrap; word-break: break-all; }
          form { display: flex; gap: 8px; padding: 8px 12px; background: var(--bar); }
          input { flex: 1; font: inherit; padding: 6px; background: var(--bg); color: var(--fg); border: 1px solid var(--dim); }
          .err { color: #d1242f; }
        </style>
        </head>
        <body>
        <header id="status">connecting...</header>
        <pre id="out"></pre>
        <form id="f"><input id="line" autocomplete="off" autofocus placeholder="type a line and press Enter"><button>Send</button></form>
        <script>
          const out = document.getElementById('out'), status = document.getElementById('status'), input = document.getElementById('line');
          const ws = new WebSocket((location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/ws');
          ws.onopen = () => status.textContent = 'dev-term - connected';
          ws.onclose = () => status.textContent = 'dev-term - disconnected (reload to reconnect)';
          ws.onmessage = e => {
            const span = document.createElement('div');
            span.textContent = e.data;
            if (e.data.startsWith('!')) span.className = 'err';
            out.appendChild(span);
            out.scrollTop = out.scrollHeight;
          };
          document.getElementById('f').onsubmit = e => {
            e.preventDefault();
            if (input.value && ws.readyState === WebSocket.OPEN) { ws.send(input.value); input.value = ''; }
          };
        </script>
        </body>
        </html>
        """;
}
