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

All configurable settings — transport defaults (e.g., default baud rate), plugin host settings (plugins directory, contract compatibility policy), front-end preferences (theme, default layout) — are modeled with `Microsoft.Extensions.Options`, not ad hoc config classes:

- Each configurable component defines its own options class (e.g., `SerialTransportOptions`, `PluginHostOptions`) and consumes it via `IOptions<T>` (fixed at startup), `IOptionsSnapshot<T>` (per-scope, e.g. per-request-ish in the WPF/console lifetime), or `IOptionsMonitor<T>` (live-reloadable) as appropriate to whether that setting can sensibly change while the app is running.
- Registration follows the standard `services.Configure<TOptions>(configuration.GetSection("..."))` pattern; plugins register their own options sections the same way as core components do.
- Configuration sources are layered in the usual .NET order: `appsettings.json` (defaults shipped with the app) → a per-user config file (user overrides, e.g. `%APPDATA%`/`~/.config`) → environment variables → command-line arguments (highest precedence) — so the console front end's CLI flags naturally override the same settings the WPF front end reads from the user config file.
- Options with constraints (e.g., a valid baud rate, a resolvable host:port) are validated with `IValidateOptions<T>`/data annotations so bad configuration fails fast at startup with a clear error, rather than surfacing as a confusing runtime failure once a session tries to open.

### Options vs. mapping/device profiles

The Options pattern is for **structural application/plugin settings** (how the app and its plugins are configured to run). It is deliberately *not* used for the mapping/device-profile data described in [presenters.md](presenters.md) (register maps, channel maps, enum labels) — those are user-authored, per-device data that's loaded, edited, and swapped independently of the app's own configuration, and are handled by a separate mapping-loader service rather than bound as `IOptions<T>`.

## Open questions

- Exact user-config file location/format convention (single file vs. one file per plugin) and whether it's meant to be hand-edited or only written by the app.
- Whether plugin options sections need namespacing/collision rules (e.g., prefixing by plugin id) so two plugins can't accidentally bind to the same configuration section.
