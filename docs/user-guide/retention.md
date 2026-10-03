# Keeping old logs and exports tidy

Session logs (`~/.dev-term/logs`) and exports such as Stream Monitor captures (`~/.dev-term/exports`) pile up. dev-term
can prune them for you each time it starts. By default it deletes **nothing**; you opt in by adding a rule to
`~/.dev-term/preferences.json` (the file the theme choice is saved in; `DEVTERM_HOME` moves the whole folder).

```json
{
  "LogRetention":    { "MaxAgeDays": 30 },
  "ExportRetention": { "MaxAgeDays": 90, "MaxFiles": 200 }
}
```

| Rule field | Meaning |
|---|---|
| `MaxAgeDays` | Delete files last written more than this many days ago. |
| `MaxFiles` | Keep only this many of the newest files. |

A file is removed when it breaks either limit that is set. Either field may be left out. Only `.jsonl` log files are
touched in the logs folder; subfolders are never entered, and a file that is locked or read-only is simply kept.

The sweep runs once at startup, in the console, TUI and WPF alike, before a new log or export is created. There is no
screen for these settings yet, so edit the file by hand. Precise rules: `RetentionRule` and `RetentionSweeper` in
`DevTerm.Configuration`; tests: `RetentionRuleTests`.
