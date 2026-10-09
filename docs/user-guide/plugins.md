# Seeing which plugins loaded

A plugin is a folder `<plugins>/<name>/plugin.json` plus its assemblies (see
[`docs/design/plugin-model.md`](../design/plugin-model.md)). The plugins folder is `plugins` next to the app, or whatever
`--plugins <folder>` (or `DEVTERM_PLUGINS`) points at. Plugins are loaded once, at startup.

## CLI

```bash
dotnet run --project src/DevTerm.Console -- --listplugins true
```

One line per plugin folder, then exit:

```
alpha  loaded  (C:\apps\devterm\plugins\alpha)
beta  skipped: no plugin.json  (C:\apps\devterm\plugins\beta)
```

With no plugins it prints `No plugins found.`

## Reviewing and revoking approvals

A plugin that runs a program of its own (a `process` entry) runs only after you approve it; choosing "always" remembers the
approval for that exact content in `plugin-approvals.json` under the dev-term home (`DEVTERM_HOME`, default `~/.dev-term`).

```bash
dotnet run --project src/DevTerm.Console -- --listapprovals true
dotnet run --project src/DevTerm.Console -- --forgetplugin shout
```

`--listapprovals true` prints one line per remembered approval (name, the first 12 characters of the content hash, when it was
approved), or `No plugin approvals are remembered.` `--forgetplugin <name>` removes that plugin's approval so it asks again
next start; it exits 1 when nothing was remembered under that name. Both exit without opening a UI. Covered by
`ConsoleAppCliTests.ListApprovals_ThenForgetPlugin_...`.

## TUI and WPF

**Device > Plugin approvals...** lists the remembered approvals (the same lines as `--listapprovals true`) and offers to forget all of
them; each plugin then asks again next start. To forget just one, use `--forgetplugin <name>`.

**Device > Plugins...** shows the same lines in a message box. It is always enabled (no connection needed) and does not
rescan: restart dev-term after adding or removing a plugin folder.

A skipped plugin never stops the app; the reason (bad manifest, another contract version, an assembly outside its folder,
no plugin module) is the text after `skipped:`.

Reference: [`tui-main-screen.md`](../specs/tui-main-screen.md), [`wpf-main-window.md`](../specs/wpf-main-window.md).
