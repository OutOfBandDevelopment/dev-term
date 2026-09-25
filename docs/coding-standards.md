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
  convention rather than adding a second one. Also sets `TreatWarningsAsErrors` (see below) and
  `<NoWarn>AD0001</NoWarn>` — AD0001 is Roslyn's `NotConfigurable` diagnostic tag, so an
  `.editorconfig` `dotnet_diagnostic.AD0001.severity` line is silently ignored for it; `NoWarn`, a
  compiler-option-level filter, is the only thing that actually suppresses it (verified empirically:
  a `.editorconfig` severity override left the warnings byte-for-byte unchanged across a clean
  rebuild with the build server shut down, ruling out staleness before concluding it doesn't work).
- **`tests/DevTerm.CodingStandards.Tests`** — a small MSTest project that reflects over every other
  test assembly (via `ProjectReference`, not a Roslyn analyzer) to check standards `.editorconfig`
  has no way to express, like "every `[TestClass]`/`[TestMethod]` has a real `[TestCategory]`" (see
  Testing below) — a missing or typo'd category doesn't fail a build on its own (MSTest just silently
  excludes that test from any `--filter TestCategory=...`), so nothing else catches it. Runs as part
  of the ordinary `dotnet test`, no separate step.
- **A future `DevTerm.Analyzers` project** (not built yet) — for standards that need a compile-time
  check on ordinary source (not test metadata) and can't be expressed as a generic style rule (e.g.
  "every `ITransport` implementation must no-op on an empty write, not throw" — see `CLAUDE.md`'s
  constraints list for why that one matters). Added once an actual rule needs it, not speculatively.

**Every warning is a build-breaking error** (`Directory.Build.props`'s `TreatWarningsAsErrors`) — the
baseline is 0 warnings across the whole solution, enforced rather than aspirational: a
`severity = warning` `.editorconfig` line (or an analyzer's own out-of-the-box default) now fails
`dotnet build`, not just an IDE squiggle. A handful of specific diagnostics are bumped further, all
the way to an explicit `error` severity in `.editorconfig`'s "My Rules" section anyway (`CS0618`,
`CS8604`, `CA2016`, `MA0040`, `IDE1006`, and the collection-expression/object-initializer `IDE0xxx`
rules) — with `TreatWarningsAsErrors` already blocking the build on any of them, the explicit `error`
is documentation ("this one is load-bearing, not just provisional") rather than a functional
difference from `warning`. Genuinely-still-a-suggestion style preferences (most `csharp_style_*`/
`dotnet_style_*` keys) stay at `suggestion` deliberately — `EnforceCodeStyleInBuild` doesn't turn
those into build warnings at all, so they remain IDE-only nudges, never a red build.

## A known StyleCop quirk (worked around, not just noted)

`severity = none` (including the category-level bulk suppressions above) stops a rule's diagnostic
from being *reported*, but doesn't stop the analyzer from *running* — a few of StyleCop's analyzers
crash instead of just not firing: `SA1201` on a `record struct` declaration (its member-order lookup
table has no entry for that syntax kind) and `SA1500`/`SA1502`/`SA1508` against
`DevTerm.Transports.Tcp.Tests`'s `SocketExceptionStub` class layout (all `NullReferenceException` in
the same internal token-lookup helper). Roslyn reports a crashing analyzer as `AD0001`, which is its
`NotConfigurable` diagnostic tag — an `.editorconfig` `dotnet_diagnostic.AD0001.severity` line is
silently ignored for it, confirmed empirically (a build-server shutdown plus a clean rebuild with
`obj`/`bin` deleted still showed the identical warnings). The actual fix is
`Directory.Build.props`'s `<NoWarn>$(NoWarn);AD0001</NoWarn>` — a compiler-option-level filter,
independent of analyzer severity config entirely. Revisit if/when StyleCop.Analyzers ships a fix for
either crash upstream.

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
- **Primary constructors are not preferred** —
  `csharp_style_prefer_primary_constructors = false:silent`. The handful of existing files already
  using one are left as-is (retroactively flagging them for removal would be a bigger, unasked-for
  change), but `IDE0290` no longer nags to convert an ordinary constructor into one.
- **`private readonly` for a field that's never reassigned outside the constructor** — a field that
  *does* need reassignment later (e.g. `MainWindow._session`, mutable for live profile switching —
  see `docs/design/connection-profiles.md`) is deliberately not `readonly`, and this rule doesn't
  fight that; it's a `suggestion`, not a requirement.

### Naming

- **`PascalCase`** for types, public/internal/protected members.
- **`_camelCase`** (leading underscore) for private fields — matches every field in the codebase
  (`_session`, `_transport`, `_isDirty`, ...).

### Testing

- **Every `[TestClass]` carries a `[TestCategory]`** whose value is one of the two declared in
  `DevTerm.Test.Utilities.TestCategories`: `Unit` (fast, hardware-free) or `Integration` (crosses a
  real process/socket boundary, or drives real physical hardware — see `docs/design/testing.md`'s
  "Two categories" section for the full breakdown) — always at the class level in this codebase,
  never per-method, so every test in a class shares one category. Enforced by
  `tests/DevTerm.CodingStandards.Tests.TestCategoryStandardsTests`, which reflects over every test
  assembly and fails if a class is missing one, uses an unrecognized value, or (checking what MSTest
  actually resolves per test, class-level plus method-level combined) a method ends up with no
  effective category at all.
- **A real-hardware `Integration` test preflights that its device actually exists/is reachable,
  with a short, bounded timeout, before touching it, and reports `Assert.Inconclusive` (never a
  failure or a hang) when it doesn't** — a device intentionally offline is an expected bench state,
  not a red build. There is no separate category for hardware-backed tests; this preflight is what
  lets them share `Integration` with process/socket-only tests while still degrading cleanly when
  no hardware is present. Not currently reflection-enforced (unlike the `[TestCategory]` rule
  above); share the check via `tests/DevTerm.Test.Utilities` (`RealDeviceReachability` for TCP
  today) rather than reimplementing it per test class. See `docs/design/testing.md`'s "Rule: every
  real-hardware test preflights device presence" section.

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
