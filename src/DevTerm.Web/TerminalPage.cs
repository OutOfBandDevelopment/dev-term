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
          :root { color-scheme: light dark; --bg: #fff; --fg: #1b1f23; --dim: #59636e; --bar: #f1f3f5; }
          @media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { --bg: #111418; --fg: #d7dde3; --dim: #8b949e; --bar: #1c2128; } }
          :root[data-theme="dark"] { color-scheme: dark; --bg: #111418; --fg: #d7dde3; --dim: #8b949e; --bar: #1c2128; }
          :root[data-theme="light"] { color-scheme: light; --bg: #fff; --fg: #1b1f23; --dim: #59636e; --bar: #f1f3f5; }
          #tools { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; padding: 4px 12px; background: var(--bar); border-bottom: 1px solid var(--dim); }
          #tools select, #tools button { font: inherit; background: var(--bg); color: var(--fg); border: 1px solid var(--dim); padding: 2px 6px; }
          #tools label { color: var(--dim); }
          input[type=checkbox] { width: 16px; height: 16px; flex: none; }
          input[type=range] { height: 20px; }
          .sent { color: var(--dim); }
          html, body { height: 100%; margin: 0; background: var(--bg); color: var(--fg); font: 14px/1.4 ui-monospace, Consolas, monospace; }
          body { display: flex; flex-direction: column; }
          header { padding: 6px 12px; background: var(--bar); color: var(--dim); }
          #out { flex: 1; overflow: auto; margin: 0; padding: 8px 12px; white-space: pre-wrap; word-break: break-all; }
          form { display: flex; gap: 8px; padding: 8px 12px; background: var(--bar); }
          input { flex: 1; font: inherit; padding: 6px; background: var(--bg); color: var(--fg); border: 1px solid var(--dim); }
          .err { color: #d1242f; }
          #tabs { display: flex; gap: 4px; align-items: center; padding: 4px 12px; background: var(--bar); border-bottom: 1px solid var(--dim); flex-wrap: wrap; }
          #tabs .tab { font: inherit; padding: 3px 10px; background: var(--bg); color: var(--fg); border: 1px solid var(--dim); cursor: pointer; }
          #tabs .tab.on { border-color: var(--fg); font-weight: bold; }
          #tabs .close { font: inherit; padding: 1px 5px; margin-left: -4px; margin-right: 8px; background: transparent; color: var(--dim); border: 0; cursor: pointer; }
          #newsession { margin-left: auto; display: flex; gap: 4px; }
          #newsession select { font: inherit; background: var(--bg); color: var(--fg); border: 1px solid var(--dim); padding: 3px; }
          #panel { padding: 8px 12px; background: var(--bar); border-bottom: 1px solid var(--dim); max-height: 40%; overflow: auto; }
          #panel h2 { font-size: 14px; margin: 8px 0 4px; }
          #panel .row { display: flex; align-items: center; gap: 8px; margin: 4px 0; flex-wrap: wrap; }
          #panel .row label { min-width: 10em; }
          #panel .note { color: var(--dim); }
        </style>
        </head>
        <body>
        <nav id="tabs"></nav>
        <header id="status">connecting...</header>
        <nav id="tools">
          <label>Send as <select id="parser"></select></label>
          <label>Device <select id="device"></select></label>
          <label><input type="checkbox" id="echo"> Echo sent commands</label>
          <label id="xonlabel" hidden><input type="checkbox" id="xon"> XON/XOFF</label>
          <button type="button" id="clear">Clear output</button>
          <button type="button" id="log">Start logging</button><a id="download" hidden>Download log</a>
          <label>Theme <select id="theme"><option value="">System</option><option value="light">Light</option><option value="dark">Dark</option></select></label>
        </nav>
        <div id="panel" hidden></div>
        <pre id="out"></pre>
        <form id="f"><input id="line" autocomplete="off" autofocus placeholder="type a line and press Enter"><button>Send</button></form>
        <script>
          const out = document.getElementById('out'), status = document.getElementById('status'), input = document.getElementById('line');
          const tabsEl = document.getElementById('tabs'), panelEl = document.getElementById('panel');
          const tabs = []; let active = null; let panelTimer = null, shownPanel = null;
          let readOnly = false, profiles = [], pickedProfile = null, sessionOpen = false, configured = true, currentProfile = null, switchTo = null;

          // One tab per session: the host's own session (/ws) and every connection opened from a saved profile (/ws/{id}).
          // Each keeps its own WebSocket and output, so switching tabs loses nothing.
          function appendLine(text) {
            const d = document.createElement('div'); d.textContent = text;
            if (text.startsWith('!')) d.className = 'err'; else if (text.startsWith('Out> ')) d.className = 'sent';
            out.appendChild(d); out.scrollTop = out.scrollHeight;
          }
          function render() {
            tabsEl.textContent = '';
            for (const t of tabs) {
              const b = document.createElement('button');
              b.className = 'tab' + (t === active ? ' on' : ''); b.dataset.tab = t.key; b.textContent = t.name; b.onclick = () => select(t);
              tabsEl.append(b);
              if (t.key !== 'main') {
                const x = document.createElement('button');
                x.className = 'close'; x.textContent = 'x'; x.title = 'Close ' + t.name; x.disabled = readOnly; x.onclick = () => closeTab(t);
                tabsEl.append(x);
              }
            }
            const pick = document.createElement('select'); pick.id = 'profile';
            const open = document.createElement('button'); open.id = 'open'; open.textContent = 'Open'; open.disabled = readOnly || profiles.length === 0;
            for (const n of profiles) pick.append(Object.assign(document.createElement('option'), { textContent: n.name, value: n.name, title: n.description }));
            if (profiles.length === 0) pick.append(Object.assign(document.createElement('option'), { textContent: 'no saved profiles' }));
            pick.disabled = profiles.length === 0; pick.value = pickedProfile || pick.value; pick.onchange = () => { pickedProfile = pick.value; };
            open.onclick = () => openProfile(pick.value);
            const add = document.createElement('span'); add.id = 'newsession'; add.append(pick, open);
            tabsEl.append(add);
            if (active) {
              out.textContent = '';
              for (const l of active.lines) appendLine(l);
              const closed = active.key === 'main' && active.state === 'connected' && !sessionOpen;
              status.textContent = active.name + (active.key === 'main' && currentProfile ? ' (' + currentProfile + ')' : '') + ' - ' + (closed ? 'session closed' : active.state);
              const tog = document.createElement('button'); tog.id = 'toggle'; tog.type = 'button';
              if (active.key === 'main') {
                tog.textContent = sessionOpen ? 'Disconnect' : 'Connect';
                tog.disabled = readOnly || (!sessionOpen && !configured);
                tog.onclick = async () => { await fetch('/api/session/' + (sessionOpen ? 'disconnect' : 'connect'), { method: 'POST' }); await refreshSession(); };
                status.append(' ', tog);
                const sw = document.createElement('select'); sw.id = 'switchprofile'; sw.disabled = readOnly || profiles.length === 0;
                for (const n of profiles) sw.append(Object.assign(document.createElement('option'), { textContent: n.name, value: n.name }));
                sw.value = switchTo || currentProfile || sw.value; sw.onchange = () => { switchTo = sw.value; };
                const go = document.createElement('button'); go.id = 'switch'; go.type = 'button'; go.textContent = 'Switch to'; go.disabled = sw.disabled;
                go.onclick = async () => {
                  const res = await fetch('/api/session/profile?name=' + encodeURIComponent(sw.value), { method: 'POST' });
                  if (!res.ok && active) { const m = '! could not switch to ' + sw.value + ' (' + res.status + ')'; active.lines.push(m); appendLine(m); }
                  await refreshSession();
                };
                status.append(' ', sw, ' ', go);
              }
              showTabPanel();
              refreshTools();
            }
          }
          async function refreshSession() {
            const st = await fetch('/api/status').then(r => r.json()).catch(() => null);
            if (!st) return;
            sessionOpen = st.state === 'Open'; configured = st.configured !== false; currentProfile = st.profile || null; render();
          }
          function addTab(key, name, path, makeActive) {
            if (tabs.some(t => t.key === key)) return;
            const tab = { key, name, lines: [], state: 'connecting...' };
            const ws = new WebSocket((location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + path);
            tab.ws = ws;
            ws.onopen = () => { tab.state = 'connected'; if (tab === active) render(); };
            ws.onclose = () => { tab.state = 'disconnected' + (key === 'main' ? ' (reload to reconnect)' : ''); if (tab === active) render(); };
            ws.onmessage = e => { tab.lines.push(e.data); if (tab.lines.length > 5000) tab.lines.shift(); if (tab === active) appendLine(e.data); };
            tabs.push(tab);
            if (makeActive || !active) active = tab;
            render();
          }
          function select(tab) { active = tab; render(); input.focus(); }
          function removeTab(key) {
            const i = tabs.findIndex(t => t.key === key);
            if (i < 0) return;
            const [gone] = tabs.splice(i, 1);
            try { gone.ws.close(); } catch { }
            if (active === gone) active = tabs[Math.max(0, i - 1)];
            render();
          }
          async function closeTab(tab) { await fetch('/api/connections/' + tab.key, { method: 'DELETE' }); removeTab(tab.key); }
          async function openProfile(name) {
            const res = await fetch('/api/connections?name=' + encodeURIComponent(name), { method: 'POST' });
            if (!res.ok) { line('! could not open ' + name + ' (' + res.status + ')'); return; }
            const c = await res.json();
            addTab(c.id, c.name, '/ws/' + c.id, true);
            select(tabs.find(t => t.key === c.id));
          }
          document.getElementById('f').onsubmit = e => {
            e.preventDefault();
            if (active && input.value && active.ws.readyState === WebSocket.OPEN) {
              const sent = input.value;
              (active.history = active.history || []).push(sent); active.recall = active.history.length;
              if (echoEl.checked) { const m = 'Out> ' + sent; active.lines.push(m); appendLine(m); }
              active.ws.send(sent); input.value = '';
            }
          };
          // Up / Down recall this tab's earlier lines, as in the desktop apps' send box.
          input.addEventListener('keydown', e => {
            if (!active || !active.history || (e.key !== 'ArrowUp' && e.key !== 'ArrowDown')) return;
            e.preventDefault();
            const h = active.history; let i = active.recall ?? h.length;
            i = e.key === 'ArrowUp' ? Math.max(0, i - 1) : Math.min(h.length, i + 1);
            active.recall = i; input.value = i < h.length ? h[i] : '';
          });

          // Menu-equivalents: send format, echo, clear, logging, theme.
          const parserEl = document.getElementById('parser'), echoEl = document.getElementById('echo'), logEl = document.getElementById('log'), dlEl = document.getElementById('download'), themeEl = document.getElementById('theme');
          async function refreshTools() {
            if (!active) return;
            refreshDevices();
            const key = active.key; const info = await fetch('/api/sessions/' + key).then(r => r.ok ? r.json() : null).catch(() => null);
            if (!info || key !== active.key) return;
            parserEl.textContent = '';
            for (const p of info.parsers) parserEl.append(Object.assign(document.createElement('option'), { textContent: p, value: p }));
            parserEl.value = info.parser; parserEl.disabled = readOnly;
            const xon = document.getElementById('xon'); document.getElementById('xonlabel').hidden = info.xonxoff == null; xon.checked = !!info.xonxoff; xon.disabled = readOnly;
            logEl.textContent = info.logging ? 'Stop logging' : 'Start logging'; logEl.disabled = readOnly; logEl.title = info.logging || '';
            dlEl.hidden = !info.logging; dlEl.href = '/api/sessions/' + key + '/log'; dlEl.download = '';
          }
          document.getElementById('xon').onchange = async e => { if (active) { await fetch('/api/sessions/' + active.key + '/xonxoff?enabled=' + e.target.checked, { method: 'POST' }); refreshTools(); } };
          parserEl.onchange = async () => { if (active) { await fetch('/api/sessions/' + active.key + '/parser?name=' + encodeURIComponent(parserEl.value), { method: 'POST' }); refreshTools(); } };
          logEl.onclick = async () => { if (active) { await fetch('/api/sessions/' + active.key + '/logging?enabled=' + (logEl.textContent === 'Start logging'), { method: 'POST' }); refreshTools(); } };
          document.getElementById('clear').onclick = () => { if (active) { active.lines = []; out.textContent = ''; } };
          try { themeEl.value = localStorage.getItem('devterm.theme') || ''; } catch { }
          function applyTheme() { if (themeEl.value) document.documentElement.dataset.theme = themeEl.value; else delete document.documentElement.dataset.theme; }
          themeEl.onchange = () => { applyTheme(); try { localStorage.setItem('devterm.theme', themeEl.value); } catch { } };
          applyTheme();

          // Device control panel: rendered generically from the UiDefinition served at /api/panel (404 = none configured).
          async function invoke(commandId, value) {
            const res = await fetch(active && active.panel ? active.panel.invokePath : '/api/invoke', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ commandId, value }) });
            if (!res.ok) { const e = await res.json().catch(() => ({})); line('! ' + (e.error || res.status)); }
          }
          function line(text) { const d = document.createElement('div'); d.textContent = text; d.className = text.startsWith('!') ? 'err' : ''; out.appendChild(d); out.scrollTop = out.scrollHeight; }
          function el(tag, props, ...kids) { const e = Object.assign(document.createElement(tag), props); e.append(...kids); return e; }
          function renderControl(c) {
            const row = el('div', { className: 'row' }); row.dataset.control = c.Id;
            const label = el('label', { textContent: c.Label, title: c.Description || '' });
            const id = c.CommandId || c.Id;
            switch (c.kind) {
              case 'button': {
                const b = el('button', { textContent: c.Label, disabled: readOnly || (c.ParameterFieldIds && c.ParameterFieldIds.length > 0) });
                if (c.ParameterFieldIds && c.ParameterFieldIds.length) b.title = 'Needs parameter fields, not available on the web yet';
                b.onclick = () => invoke(id, null); row.append(b); break; }
              case 'toggle': {
                const t = el('input', { type: 'checkbox', checked: !!c.DefaultValue, disabled: readOnly });
                t.onchange = () => invoke(id, t.checked ? '1' : '0'); row.append(label, t); break; }
              case 'slider': case 'numeric': {
                const n = el('input', { type: c.kind === 'slider' ? 'range' : 'number', min: c.Minimum, max: c.Maximum, step: c.Step || 'any', value: c.DefaultValue, disabled: readOnly });
                n.onchange = () => invoke(id, String(n.value)); row.append(label, n, el('span', { className: 'note', textContent: c.Unit || '' })); break; }
              case 'choice': {
                const sel = el('select', { disabled: readOnly });
                for (const o of c.Options) sel.append(el('option', { textContent: o, selected: o === c.DefaultValue }));
                sel.onchange = () => invoke(id, sel.value); row.append(label, sel); break; }
              case 'textField': {
                const t = el('input', { type: 'text', value: c.DefaultValue || '', maxLength: c.MaxLength || 524288, disabled: readOnly });
                t.onchange = () => invoke(id, t.value); row.append(label, t); break; }
              case 'indicator': {
                const v = el('span', { className: 'note', textContent: '-' }); v.dataset.indicator = c.Id; row.append(label, v, el('span', { className: 'note', textContent: c.Unit || '' })); break; }
              default:
                row.append(label, el('span', { className: 'note', textContent: '(' + c.kind + ' is not shown on the web yet)' }));
            }
            return row;
          }
          // Device menu: the panels that suit the active tab's connection; the chosen one shows above the output, its indicators polled.
          const deviceEl = document.getElementById('device');
          function buildPanel(def) {
            panelEl.textContent = '';
            panelEl.append(el('h2', { textContent: def.Name + (readOnly ? ' (read-only)' : '') }));
            for (const section of def.Sections) {
              if (section.Label) panelEl.append(el('h2', { textContent: section.Label }));
              for (const c of section.Controls) panelEl.append(renderControl(c));
            }
          }
          function applyValues(values) {
            for (const n of panelEl.querySelectorAll('[data-indicator]')) if (values[n.dataset.indicator] !== undefined) n.textContent = values[n.dataset.indicator];
          }
          function showTabPanel() {
            const p = active && active.panel;
            panelEl.hidden = !p;
            if (p && p === shownPanel) return; // re-rendering the page must not rebuild the controls under the user's hand
            clearInterval(panelTimer); panelTimer = null; shownPanel = p || null;
            if (!p) return;
            buildPanel(p.def); applyValues(p.values || {});
            if (p.pollPath) {
              panelTimer = setInterval(async () => {
                const r = await fetch(p.pollPath).then(x => x.ok ? x.json() : null).catch(() => null);
                if (r && active && active.panel === p) applyValues(r.values);
              }, 1000);
            }
          }
          async function chooseDevice(id) {
            if (!active) return;
            if (!id) { active.panel = null; showTabPanel(); return; }
            const path = '/api/sessions/' + active.key + '/panels/' + encodeURIComponent(id);
            const r = await fetch(path).then(x => x.ok ? x.json() : null).catch(() => null);
            if (!r) return;
            active.panel = { id, def: r.definition, values: r.values, invokePath: path + '/invoke', pollPath: path };
            showTabPanel();
          }
          deviceEl.onchange = () => chooseDevice(deviceEl.value);
          async function refreshDevices() {
            if (!active) return;
            const key = active.key; const list = await fetch('/api/sessions/' + key + '/panels').then(r => r.ok ? r.json() : []).catch(() => []);
            if (key !== active.key) return;
            deviceEl.textContent = '';
            deviceEl.append(Object.assign(document.createElement('option'), { textContent: list.length ? '(none)' : 'no panels for this connection', value: '' }));
            for (const p of list) deviceEl.append(Object.assign(document.createElement('option'), { textContent: p.title, value: p.id }));
            deviceEl.value = active.panel && active.panel.id || ''; deviceEl.disabled = list.length === 0;
          }
          (async () => {
            const st = await fetch('/api/status').then(r => r.json()).catch(() => ({}));
            readOnly = !!st.readOnly; sessionOpen = st.state === 'Open'; configured = st.configured !== false; currentProfile = st.profile || null;
            profiles = await fetch('/api/project').then(r => r.json()).catch(() => []);
            addTab('main', 'dev-term', '/ws', true);
            const open = await fetch('/api/connections').then(r => r.json()).catch(() => []);
            for (const c of open) addTab(c.id, c.name, '/ws/' + c.id, false);
            // Other tabs, the /connections page and the REST calls open and close sessions too.
            const es = new EventSource('/api/events');
            es.addEventListener('connection-opened', e => { const c = JSON.parse(e.data); addTab(c.id, c.name, '/ws/' + c.id, false); });
            es.addEventListener('connection-closed', e => removeTab(JSON.parse(e.data).id));
            es.addEventListener('session-state', e => { sessionOpen = JSON.parse(e.data).state === 'Open'; refreshSession(); });
            es.addEventListener('project-changed', async () => { profiles = await fetch('/api/project').then(r => r.json()).catch(() => profiles); render(); });
            const res = await fetch('/api/panel');
            if (!res.ok) return;
            // A panel pinned with Web:Panel is the main tab's panel from the start.
            tabs[0].panel = { id: '', def: await res.json(), values: {}, invokePath: '/api/invoke', pollPath: null }; render();
          })();
        </script>
        </body>
        </html>
        """;
}
