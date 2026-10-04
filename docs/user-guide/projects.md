# Saving and reopening a set of connections (projects)

A project file remembers connections (transport settings plus presenter choices) so one command brings a bench back.
It is only read when you ask: dev-term never reopens the last project by itself.

Save the connection you describe on the command line, without connecting:

```
dev-term --transport tcp --host 192.168.0.110 --port 23 --presenter ascii --saveproject bench.json
Saved project bench.json
```

Open it again (any front end; the first connection unless you name one):

```
dev-term --project bench.json
dev-term --project bench.json --projectconnection Scope
```

A flag still wins over the project, so `--project bench.json --baud 9600` changes just the baud rate. Add more
connections by editing the file: each entry is a `Name` and a `Profile` in the same shape as a saved profile
(see [managing-profiles.md](managing-profiles.md)). Design: [project-state](../design/proposals/project-state.md).

In the TUI and WPF, **File > Save Project...** writes every open tab's connection to a file you pick, and
**File > Open Project...** opens one new tab per connection in a file and connects each. Nothing is opened
unless you choose it.
