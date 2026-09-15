# Platform, Hosting & Configuration

## Purpose

Pins down the concrete .NET technology choices underpinning the architecture described in [architecture.md](architecture.md): target framework, composition/DI model, and how settings are configured.

## Target framework

.NET 10+ across the core engine, plugins, and both front ends (console and WPF). Plugins target the same framework version the host does, per their declared contract version (see [plugin-model.md](plugin-model.md)).

## Dependency injection & hosting

The core engine, every plugin, and both front ends are composed through standard .NET dependency injection (`Microsoft.Extensions.DependencyInjection`), built on the Generic Host (`Microsoft.Extensions.Hosting`). This is the single composition root pattern used everywhere in the app — nothing `new`s up a transport, presenter, or session directly; everything is resolved from the container.

- The core engine exposes a `services.AddDevTermCore(...)` style registration extension that both front ends call, so the console app and the WPF app assemble the *same* core services (session management, plugin host, pipeline) rather than each having their own copy.
- Each front end (console, WPF) builds its own `IHost`/`IServiceProvider` appropriate to its hosting model (a console `IHostedService`-driven app vs. a WPF `Application` wired to a host), but both bottom out in the same core registrations plus whatever plugins are discovered.
- Plugins participate in DI rather than being instantiated ad hoc by the plugin loader: each plugin exposes a small entry point that registers its own services (transport/presenter implementations, their options types) into the host's `IServiceCollection` before the container is built. See [plugin-model.md](plugin-model.md) for the discovery/registration sequence.

## Settings via the Options pattern

All configurable settings — transport defaults (e.g., default baud rate), presenter tuning (e.g. the ASCII presenter's line-buffering limit), plugin host settings (plugins directory, contract compatibility policy), front-end preferences (theme, default layout) — are modeled with `Microsoft.Extensions.Options`, not ad hoc config classes:

- Each configurable component defines its own options class (e.g., `SerialTransportOptions`, `AsciiPresenterOptions`, `PluginHostOptions`) and consumes it via `IOptions<T>` (fixed at startup), `IOptionsSnapshot<T>` (per-scope, e.g. per-request-ish in the WPF/console lifetime), or `IOptionsMonitor<T>` (live-reloadable) as appropriate to whether that setting can sensibly change while the app is running. The component takes `IOptions<T>` directly in its constructor rather than a plain primitive parameter, so a bare `services.AddSingleton<TInterface, TImplementation>()` (or `AddTransient`) resolves it correctly with no factory lambda needed.
- Registration follows the standard `services.Configure<TOptions>(...)` pattern; plugins register their own options sections the same way as core components do.
- **Implemented** configuration layering (console app, via `DevTermConfiguration`, still composed from the standard `Microsoft.Extensions.Configuration` extensions rather than hand-rolled): `appsettings.json` (shipped defaults) → `appsettings.<environment>.json` → `appsettings.Local.json` (an untracked, per-machine saved profile — e.g. "COM3, 4800, 8N1, ascii" — sitting next to the built app, copied there on build if present in the source tree) → environment variables (`DEVTERM_` prefix, to avoid colliding with unrelated ones) → command-line arguments (highest precedence). This is what makes a "saved profile" nothing more than a settings file at the right precedence, no bespoke profile mechanism needed.
- Options with constraints (e.g., a valid baud rate, a resolvable host:port, a non-negative buffer length) are validated with `IValidateOptions<T>`/data annotations so bad configuration fails fast at startup with a clear error, rather than surfacing as a confusing runtime failure once a session tries to open.

### Options vs. mapping/device profiles

The Options pattern is for **structural application/plugin settings** (how the app and its plugins are configured to run). It is deliberately *not* used for the mapping/device-profile data described in [presenters.md](presenters.md) (register maps, channel maps, enum labels) — those are user-authored, per-device data that's loaded, edited, and swapped independently of the app's own configuration, and are handled by a separate mapping-loader service rather than bound as `IOptions<T>`.

## Open questions

- Whether plugin options sections need namespacing/collision rules (e.g., prefixing by plugin id) so two plugins can't accidentally bind to the same configuration section — the console app's own settings are currently all flat/top-level (see `CliOptions`), which won't scale once plugin-contributed options join the same file.
- Whether the single-file `appsettings.Local.json` profile convention should grow into multiple *named* profiles (`--profile <name>`) once someone needs to switch between several saved devices, or whether "one file, edit it" stays sufficient.
