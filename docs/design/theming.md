# Theming

Light/dark mode and user-defined themes for both front ends (TUI and WPF). Implemented 2026-09-25.
Screens: View > Theme in the [TUI main screen](../specs/tui-main-screen.md) and the
[WPF main window](../specs/wpf-main-window.md). How-to: [user guide](../user-guide/themes.md).

## What was investigated first

Both UI frameworks have their own theming. Both were tried against this project before building
anything. Neither was adopted as the mechanism.

### WPF: .NET's Fluent theme (`ThemeMode`)

Tried in a throwaway `net10.0-windows` WPF app with the same kinds of controls dev-term uses (menu,
status bar, list box, editable combo box, text box, button, check box, slider, expander). Each was
rendered with `Window.ThemeMode` = None / Light / Dark / System through `RenderTargetBitmap`.

- **It works on `net10.0-windows`,** per window (`Window.ThemeMode`), with no `Application` needed.
  That matters because tests create windows without one. `Application.ThemeMode` also exists.
- **It's still experimental.** Every use raises `WPF0001` ("for evaluation purposes only and is
  subject to change or removal"). With `TreatWarningsAsErrors`, that needs a suppression on every use.
- **It re-templates every control, with much larger metrics.** In the probe, a 60 px list box that
  showed two rows under the stock theme showed one row under Fluent. Buttons, combo boxes and
  expanders all grew. That breaks layouts sized against the stock look: the control panels' fit
  tests, and the Connection Editor, which is being re-laid-out in parallel.
- **Dark mode's window background is transparent** (`#00FFFFFF`, a Mica backdrop). On a real
  Windows 11 desktop the OS paints the backdrop. `RenderTargetBitmap` doesn't, so a dark screenshot
  came out as white text on white. That would break the doc screenshots.

So dev-term keeps the stock (Aero2) control templates and themes them itself (see below).

### TUI: Terminal.Gui v2.5.0's schemes and themes

Checked by reflecting over, and running against, the installed package:

- v2.5.0 has no `ConfigurationManager`. Themes are `Terminal.Gui.Configuration.ThemeManager`
  (`ThemeManager.Theme = "Dark"` switches live and raises `ThemeChanged`). Named schemes are
  `SchemeManager` (`Base`, `Menu`, `Dialog`, `Error`, `Accent`; `GetScheme`, `AddScheme`).
- Built-in themes: `Default`, `8-Bit`, `Amber Phosphor`, `Anders`, `Dark`, `Green Phosphor`,
  `Light`, `TurboPascal 5`.
- **The built-in `Dark` and `Light` themes leave the `Base` background as `None`,** meaning "the
  terminal's own background". `Dark` draws `LightGray`-on-`None`, which is unreadable on a
  light-background terminal. `Default` is `None`-on-`None` throughout. That's also why the headless
  driver reports white-on-white for unstyled cells (see CLAUDE.md).
- **`SchemeManager.AddScheme(name, scheme)` overrides a built-in scheme live.** Every view without its
  own scheme picks it up on its next draw, including views already on screen. The override is
  process-wide, not per `IApplication`. It survives `Application.Init`, and it survives a later
  `ThemeManager.Theme` switch: a runtime override sticks. Re-adding the original schemes restores them.
- `new Scheme(attribute)` derives every other role (Focus, Editable, Disabled...) from one attribute.
  The derived values aren't always usable: in a light scheme, Disabled comes out near-black on white.
  Individual roles can also be set with an object initializer.

So the TUI overrides the named schemes with explicit colors for every role, from dev-term's own
theme model.

## The model (`DevTerm.Configuration`)

- **`ThemeRole`**: semantic color roles, named for what the color is for, not where it's drawn:
  - `background`, `foreground`, `mutedForeground`.
  - `controlBackground`/`controlForeground`/`controlBorder`/`controlHoverBackground`.
  - `fieldBackground`: TUI text fields have no border, so they need their own background.
  - `selectionBackground`/`selectionForeground`, `menuBackground`/`menuForeground`, `accent`.
  - `error`, `warning`, `outputStatus`, `outputError`.
  - `statusConnected`/`statusConnecting`/`statusDisconnected`, each with a `…Text` partner.
  - `recording`, `swatchBorder`.
  - `chartSurface`/`chartGrid`/`chartText`/`chartMuted`.
- **`DevTermTheme`**: a name, a color for every role (`ThemeColor`, `#RRGGBB`), and a
  `ChartPaletteVariant`. It's immutable. `IsDark` is derived from the background's luminance.
  `ContrastWarnings()` checks the pairs a reader has to read against WCAG ratios: 4.5:1 for text,
  3:1 for secondary text and indicator fills. Tests check the built-ins; for a user theme the
  failures are reported but not rejected.
- **`BuiltInThemes`**: `light`, `dark`, and `system`. `light` keeps the colors the front ends
  hard-coded before theming: WPF's DarkRed/DimGray output lines and SteelBlue info icons, and the
  TUI's green/amber/red status line. `system` isn't a theme of its own. It resolves to `light` or
  `dark` through `SystemThemeDetector`. On Windows that reads `HKCU\…\Themes\Personalize\AppsUseLightTheme`
  (0 = dark); elsewhere it uses the terminal's `COLORFGBG` hint. Undetectable means light.
- **`ThemeFile`**: user themes, as `*.json` under `~/.dev-term/themes`
  (`DevTermUserDataPaths.ThemesDirectory`):

  ```json
  {
    "name": "Solarized Dark",
    "basedOn": "dark",
    "chartPalette": "dark",
    "colors": { "background": "#002B36", "foreground": "#EEE8D5", "outputError": "#DC322F" }
  }
  ```

  Every field is optional:
  - `name` defaults to the file name.
  - `basedOn` (`light`|`dark`) defaults to `light`.
  - `chartPalette` defaults to the base's.
  - Roles left out keep the base's color.

  Comments and trailing commas are allowed. A bad file never throws. It's rejected with every
  problem listed (not just the first), each prefixed with the file name. Problems caught: bad JSON
  (with its line), an unknown setting or role, a malformed color, a reserved name, a bad `basedOn`
  or `chartPalette`.
- **`ThemeCatalog`**: the built-ins plus every valid user theme. Problems collects the rejected files,
  duplicate names, and contrast warnings. `Resolve(name)` treats an unknown name as `system` and
  returns a warning with it.
- **`ActiveTheme`**: the process-wide current theme, and the only place a theme gets selected.
  - `Initialize(configuration)` runs at startup.
  - `Select(name)` runs from View > Theme. It persists the choice and raises `Changed`.
  - `RefreshSystem()` re-resolves `system` when WPF sees the OS setting change.
  - Until `Initialize` runs, it holds Light with no persistence. A window built under test gets
    deterministic colors whatever the machine's OS setting, and never writes the real preferences file.

## Where the selection lives: app preferences, not profiles

The theme is a preference about dev-term itself, not about a device connection. It's stored in
**`~/.dev-term/preferences.json`** (`DevTermUserDataPaths.PreferencesFile`, `AppPreferencesStore`),
shared by both front ends. It's never stored in a connection profile or `appsettings.Local.json`, so
switching profile never changes the theme.

The theme is read straight from configuration (key `Theme`), deliberately not as a `CliOptions`
property. Precedence, highest first:

1. `--theme <light|dark|system|name>`, then `DEVTERM_THEME`, then an appsettings `Theme` key. This is
   a one-run override and isn't saved.
2. The saved View > Theme choice.
3. `system`.

A missing preferences file means defaults. A corrupt one means defaults plus a reported problem.

## How each front end applies it

### WPF (`WpfTheme`, `DarkControls.xaml`)

Every window calls `WpfTheme.Attach(this)` after `InitializeComponent`. The app also calls
`WpfTheme.AttachApplication`, so popups outside any window's resource tree (a text box's context
menu) are themed too. Each attached window gets a merged `ThemeDictionary`:

- **One frozen brush per role, under `"DevTerm.<Role>"`.** XAML uses
  `{DynamicResource DevTerm.OutputError}`. Code-behind uses `SetResourceReference` or the `Themed(...)`
  helper for controls it builds itself. A live switch is just replacing the dictionary.
- **For any theme except the stock `light`, overrides of the `SystemColors` brush keys** (window,
  control, menu, highlight, gray text, info). The stock control styles read their defaults from these
  keys, so text boxes, menus and the status bar recolor without new templates.
- **For a dark theme, `DarkControls`**: compact replacement templates, keeping the stock metrics, for
  the stock controls whose chrome is hard-coded light and would otherwise show light-on-light text:
  - `Button`, whose hover and pressed backgrounds are fixed light blues.
  - `ComboBox`/`ComboBoxItem`: the toggle chrome and the editable text area.
  - `MenuItem` and `ContextMenu`: the drop-down popup background is fixed `#F0F0F0`.
  - Explicit backgrounds for `ListBox`, `TextBox`, `Menu`, `StatusBar`, `ToolTip`.
  - `ListBoxItem`: the stock hover/selection fills, and the `#DADADA` frame round an unfocused list's
    selected row.
  - `ScrollBar` (track, arrows, thumb: fixed `#F0F0F0`/`#CDCDCD`), `CheckBox`/`RadioButton` (a fixed
    white box/circle), `Expander` (its header's white circle; downward only), `Slider` (its light
    track and thumb; horizontal only), and `GroupBox` (a hard-coded white inner line inside its frame).

  These last ones used to keep their stock light chrome, on the grounds that a white box with a dark
  glyph stays readable. The WPF layout review (`UiLayoutReviewTests`, which fails on any light surface
  in a dark theme) flagged them as what they looked like in the screenshots: a light scroll-bar stripe
  down every list and form, white check boxes and expander buttons, and a doubled white frame round
  the color picker's groups - so they're themed now too. A window's own item style replaces the
  implicit dark `ListBoxItem` style, so windows don't set one (the Device Profiles list handles
  double-click on the `ListBox` instead of with an item `EventSetter`).
- A light-based user theme recolors surfaces and text but keeps the stock templates. Their chrome
  stays light, so they stay readable.
- The chart elements (`LiveDisplayElement`) bind their ink to the chart roles, and the chart palette
  variant to `DevTerm.ChartPaletteDark`, through dependency properties with `AffectsRender`, so a
  switch redraws them.
- `ActiveTheme.Changed` reaches every attached window, marshaled to each one's own dispatcher. It
  runs synchronously when already on that thread.
- `SystemEvents.UserPreferenceChanged` re-resolves `system` live.

### TUI (`TuiTheme`, `TuiThemeMenu`)

- `TuiTheme.Apply(theme)` overrides Terminal.Gui's `Base`/`Dialog`/`Accent` (window), `Menu` and
  `Error` schemes, setting every visual role explicitly:
  - Normal: foreground/background.
  - Focus/Active: selection.
  - Editable/ReadOnly: control foreground on `fieldBackground`.
  - Disabled: muted.

  `Program.cs` applies it before any TUI screen (including the startup Connection Editor), and
  `TuiMode.RunAsync` applies it after `Init`. `TuiTheme.Restore()` puts Terminal.Gui's own schemes
  back (used by tests).
- Colors dev-term draws itself are looked up per role where they're drawn:
  - The status line: `TuiTheme.StatusAttribute`.
  - The Stream Monitor's state line.
  - The chart glyphs (`CellCharts.Muted`, palette variant).
  - The output pane's highlighting. `OutputHighlighting` generates the XSHD from the theme's
    `outputError`/`outputStatus`, because XSHD colors are fixed once loaded. It's cached per theme,
    and the Editor's `HighlightingDefinition` is swapped on a switch.
- `TuiMode.BuildWindow` subscribes to `ActiveTheme.Changed`. On a switch it re-applies the schemes,
  swaps the highlighting, re-marks the menu, and refreshes the status line. That happens directly on
  the UI thread, or through `app.Invoke` from any other thread. A window whose app has shut down
  unsubscribes itself.

### Deliberately not themed

- **The color swatch's own text** (the Busylight panel's picked color, in both front ends) stays
  black or white by the swatch color's luminance (`LastPickedColors.UseDarkText`). It has to contrast
  with the picked color, not with the theme. The swatch border is themed (`swatchBorder`).
- **The chart palette's order.** `ChartPalette` (`DevTerm.UiDefinitions`) keeps its eight fixed,
  colorblind-safe slots. A theme only picks the variant: `Slots` (light) or `DarkSlots`, the same
  hues in the same order, stepped for a dark surface. A channel's own explicit color always wins.

## Open questions

- The TUI Connection Editor's "(not found)" hints aren't colored; WPF's use `error`. Coloring the
  TUI's needs `ConfigureMode` changes, which were deliberately left alone while that screen's layout
  is being rewritten.
- A "terminal" theme for the TUI would keep Terminal.Gui's `None` colors and follow the terminal's
  own palette, as the TUI did before theming. It's not offered. `system` follows the OS setting
  instead, which can differ from the terminal's background.
- WPF's `system` follows the OS live. The TUI's resolves once, at startup or at selection.
