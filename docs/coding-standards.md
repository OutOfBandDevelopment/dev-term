# Coding Standards

The declared, enforced coding standards for dev-term — what's here is a rule someone actually
stated, backed by an `.editorconfig`/analyzer setting that catches it, not a style guide written in
the abstract. New standards get added here **and** turned on in `.editorconfig`/`Directory.Build.props`
in the same change; a rule that's only written down here but not enforced anywhere isn't done yet.

## How this is enforced

- **`.editorconfig`** (repo root) — the built-in .NET/Roslyn code-style analyzers
  (`csharp_style_*`/`dotnet_style_*` keys, naming rules) plus per-rule severity overrides for
  [StyleCop.Analyzers](https://github.com/DotNetAnalyzers/StyleCopAnalyzers).
- **`Directory.Build.props`** (repo root) — applies to every project automatically. References
  StyleCop.Analyzers solution-wide and sets `EnforceCodeStyleInBuild=true`, so `.editorconfig`'s
  style rules actually surface as `dotnet build` warnings, not just IDE squiggles — matching
  `CLAUDE.md`'s existing "no separate lint step; `dotnet build` surfaces analyzer warnings"
  convention rather than adding a second one.
- **A future `DevTerm.Analyzers` project** (not built yet) — for standards that are specific to this
  codebase's own semantics and can't be expressed as a generic style rule (e.g. "every `ITransport`
  implementation must no-op on an empty write, not throw" — see `CLAUDE.md`'s constraints list for
  why that one matters). Added once an actual rule needs it, not speculatively.

**Severities are `suggestion`/`warning`, essentially never `error`**, deliberately: this project's
existing convention is that a warning is a real, worth-reading signal (see `CLAUDE.md`'s Terminal.Gui
obsolete-API warnings, tracked and accepted on purpose, not suppressed) — turning a style preference
into a build-breaking error is a bigger step than "declare a standard," and hasn't been asked for.

## StyleCop.Analyzers starts silent, on purpose

The package is referenced (`Directory.Build.props`) so any rule can be turned on with one line, but
**every rule it ships starts at `severity = none`** in `.editorconfig` (by category, plus a couple of
individual stragglers). A first real build against this codebase with StyleCop's out-of-the-box
defaults produced **~1,800 warnings** — 1,396 of them `SA1101` ("prefix local calls with `this`")
alone, a style this codebase has never used anywhere. That's not a baseline anyone should have to dig
out from under; it's noise. Turning a specific `SAxxxx` rule on is exactly what happens when a
standard below gets declared that StyleCop already knows how to check.

## Declared standards

### Formatting

- **UTF-8, LF line endings, final newline, no trailing whitespace** — `.editorconfig`'s `[*]`
  section. Matches every file in the repo already (checked directly, not assumed, before writing
  this rule down).
- **4-space indentation for C#, 2-space for XAML/XML/project files/YAML/JSON.**
- **Allman braces** (opening brace on its own line) — `csharp_new_line_before_open_brace = all`.
- **Braces required on every `if`/`for`/`while`/etc. body, even a single statement** —
  `csharp_prefer_braces = true:warning`. No `if (x) return;` single-line style.

### Language style

- **File-scoped namespaces everywhere** (`namespace Foo.Bar;`, no braces) —
  `csharp_style_namespace_declarations = file_scoped:warning`. The one rule in this baseline set to
  `warning` rather than `suggestion`, since it's the most mechanically obvious to fix.
- **`var` wherever the type is obvious or built-in** — matches existing usage throughout
  (`csharp_style_var_*` keys, all `:suggestion`).
- **Expression-bodied members for simple one-line properties/accessors** — matches e.g.
  `ConnectionEditorViewModel`'s `public string Port { get => _port; set => SetField(ref _port, value); }`.
- **No `this.`/type-name qualification** for members accessed from within their own class — matches
  existing usage (no `this.` prefix anywhere in the codebase today).
- **`using` directives**: `System.*` first, then alphabetical, outside the namespace block, no blank
  line between groups.
- **`private readonly` for a field that's never reassigned outside the constructor** — a field that
  *does* need reassignment later (e.g. `MainWindow._session`, mutable for live profile switching —
  see `docs/design/connection-profiles.md`) is deliberately not `readonly`, and this rule doesn't
  fight that; it's a `suggestion`, not a requirement.

### Naming

- **`PascalCase`** for types, public/internal/protected members.
- **`_camelCase`** (leading underscore) for private fields — matches every field in the codebase
  (`_session`, `_transport`, `_isDirty`, ...).

## Adding a new standard

1. State the rule here, in **Declared standards**, with the same shape as the entries above (what,
   plus a one-line "matches existing usage" or "new direction, applies going forward" note).
2. Turn on the corresponding `.editorconfig` key (a built-in `csharp_style_*`/`dotnet_style_*` key,
   or `dotnet_diagnostic.SAxxxx.severity = warning` for a StyleCop rule — check
   [StyleCop.Analyzers' rule list](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/Index.md)
   for the right ID first, don't guess).
3. Run a full `dotnet build --no-incremental` and check the new warning count is what you'd expect
   (either ~0, because the codebase already follows the rule, or a real, reviewable list of places
   that don't yet — fix those in the same change, or note them as an explicit follow-up, don't leave
   a new rule silently generating warnings nobody looks at).
4. If the rule can't be expressed via `.editorconfig`/an existing analyzer at all (a project-specific
   semantic rule, not a style one), that's when `DevTerm.Analyzers` gets built — see "How this is
   enforced" above.
