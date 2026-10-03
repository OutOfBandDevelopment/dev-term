using System.Collections.ObjectModel;
using System.Text;
using DevTerm.Configuration;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DevTerm.Console;

/// <summary>
/// The TUI's "Build/Edit Theme..." flow (View &gt; Theme): pick a seed (a built-in or an existing
/// user theme), edit any of the 28 <see cref="ThemeRole"/> colors as hex, watch the whole app
/// re-skin live through <see cref="ActiveTheme.Preview"/>, then either Save (a new named user theme
/// file) or Cancel (<see cref="ActiveTheme.CancelPreview"/> reverts). All the color tracking,
/// live-preview building, and saving logic is the framework-agnostic <see cref="ThemeBuilderState"/>,
/// shared with any future WPF screen — this class is just the Terminal.Gui rendering of it. See
/// docs/design/features/theme-builder.md.
/// </summary>
internal static class ThemeBuilderMode
{
    private const string _overriddenMarker = "● ";
    private const string _unchangedMarker = "  ";

    /// <summary>Picks a seed and runs the builder (a nested <c>Application.Run</c>); does nothing if the seed picker is cancelled.</summary>
    public static void Run(IApplication app, string? themesDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (PickSeed(app) is not { } seed)
        {
            return;
        }

        var state = new ThemeBuilderState(seed.Theme, seed.Name);
        var parts = BuildWindow(app, state, themesDirectory);
        ActiveTheme.Preview(state.Build());
        try
        {
            app.Run(parts.Window);
        }
        finally
        {
            parts.Window.Dispose();

            // Save() already re-selected the saved theme (so this re-resolves to the same thing);
            // Cancel leaves Selection untouched, so this reverts the live preview.
            ActiveTheme.CancelPreview();
        }
    }

    /// <summary>
    /// A small modal picker: every <see cref="ThemeCatalog.SelectionNames"/> entry as a possible seed,
    /// plus a name for the new theme (defaulted from the seed, editable before creating). Returns null
    /// when cancelled.
    /// </summary>
    internal static (DevTermTheme Theme, string Name)? PickSeed(IApplication app)
    {
        var seedNames = ActiveTheme.Catalog.SelectionNames;
        (DevTermTheme Theme, string Name)? picked = null;

        var dialog = new Dialog { Title = "Build/Edit Theme", Width = 56, Height = Math.Clamp(seedNames.Count + 9, 11, 20) };
        var listLabel = new Label { X = 0, Y = 0, Text = "Start from:" };
        var listView = new ListView { X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Fill(4) };
        listView.SetSource(new ObservableCollection<string>(seedNames.Select(SeedLabel)));
        listView.SelectedItem = 0;

        var nameLabel = new Label { X = 0, Y = Pos.AnchorEnd(4), Text = "New theme name:" };
        var nameField = new TextField
        {
            X = 0,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(),
            Text = seedNames.Count > 0 ? DefaultName(seedNames[0]) : "My Theme",
        };

        void Choose()
        {
            if (listView.SelectedItem is not int index || index < 0 || index >= seedNames.Count)
            {
                app.RequestStop();
                return;
            }

            var seedName = seedNames[index];
            var theme = ActiveTheme.Catalog.Resolve(seedName, out _, ActiveTheme.PrefersDark);
            var name = string.IsNullOrWhiteSpace(nameField.Text) ? DefaultName(seedName) : nameField.Text.Trim();
            picked = (theme, name);
            app.RequestStop();
        }

        listView.Accepting += (_, e) =>
        {
            e.Handled = true;
            Choose();
        };
        nameField.Accepting += (_, e) =>
        {
            e.Handled = true;
            Choose();
        };
        var createButton = new Button { X = 0, Y = Pos.AnchorEnd(2), Text = "Create", IsDefault = true };
        createButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Choose();
        };
        var cancelButton = new Button { X = Pos.Right(createButton) + 1, Y = Pos.AnchorEnd(2), Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            app.RequestStop();
        };

        dialog.Add(listLabel, listView, nameLabel, nameField, createButton, cancelButton);
        try
        {
            app.Run(dialog);
        }
        finally
        {
            dialog.Dispose();
        }

        return picked;
    }

    /// <summary>
    /// The builder window around <paramref name="state"/>, without running it — the seam tests drive
    /// headlessly. <paramref name="themesDirectory"/> defaults to the real
    /// <see cref="DevTermUserDataPaths.ThemesDirectory"/>; a test passes its own temp directory instead,
    /// the same way <see cref="ConnectionProfileStore"/> does.
    /// </summary>
    public static ThemeBuilderWindowParts BuildWindow(IApplication app, ThemeBuilderState state, string? themesDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(state);
        var directory = themesDirectory ?? DevTermUserDataPaths.ThemesDirectory;

        var roles = Enum.GetValues<ThemeRole>();
        var window = new Window { Title = "dev-term — Theme Builder (Ctrl+Q to quit)", X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };

        var nameLabel = new Label { X = 0, Y = 0, Text = "Name:" };
        var nameField = new TextField { X = Pos.Right(nameLabel) + 1, Y = 0, Width = 28, Text = state.Name };

        var paletteLabel = new Label { X = Pos.Right(nameField) + 3, Y = 0, Text = "Chart palette:" };
        var paletteSelector = new OptionSelector
        {
            X = Pos.Right(paletteLabel) + 1,
            Y = 0,
            Orientation = Orientation.Horizontal,
            HorizontalSpace = 2,
            Labels = ["Light", "Dark"],

            // Labels are fixed data, not menu text - no '_' hotkey marker meaning intended.
            HotKeySpecifier = (Rune)0xFFFF,
            Value = state.ChartPalette == ChartPaletteVariant.Dark ? 1 : 0,
        };

        var rolesLabel = new Label { X = 0, Y = 2, Text = "Colors (Enter or Edit... to change; ● marks one overridden from the seed):" };
        var rolesList = new ListView { X = 0, Y = 3, Width = 48, Height = Dim.Fill(5) };

        var warningsLabel = new Label { X = 0, Y = Pos.AnchorEnd(5), Width = Dim.Fill(), Height = 3, HotKeySpecifier = (Rune)0xFFFF };

        void Preview() => ActiveTheme.Preview(state.Build());

        void RefreshRoles()
        {
            var selected = rolesList.SelectedItem;
            rolesList.SetSource(new ObservableCollection<string>(roles.Select(role => RoleRow(state, role))));
            if (selected is int index && index >= 0 && index < roles.Length)
            {
                rolesList.SelectedItem = index;
            }
        }

        void RefreshWarnings()
        {
            var warnings = state.ContrastWarnings();
            warningsLabel.Text = warnings.Count == 0 ? "No contrast problems." : string.Join('\n', warnings.Take(3));
        }

        RefreshRoles();
        RefreshWarnings();

        nameField.TextChanged += (_, _) =>
        {
            state.Name = string.IsNullOrWhiteSpace(nameField.Text) ? state.Name : nameField.Text.Trim();
            Preview();
        };
        paletteSelector.ValueChanged += (_, _) =>
        {
            state.ChartPalette = paletteSelector.Value is 1 ? ChartPaletteVariant.Dark : ChartPaletteVariant.Light;
            Preview();
        };

        void EditSelectedRole()
        {
            if (rolesList.SelectedItem is not int index || index < 0 || index >= roles.Length)
            {
                return;
            }

            var role = roles[index];
            if (EditColor(app, role, state[role]) is { } color)
            {
                state.Set(role, color);
                RefreshRoles();
                RefreshWarnings();
                Preview();
            }
        }

        rolesList.Accepting += (_, e) =>
        {
            e.Handled = true;
            EditSelectedRole();
        };
        var editButton = new Button { X = Pos.Right(rolesList) + 2, Y = 3, Text = "Edit...", ShadowStyle = ShadowStyles.None };
        editButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            EditSelectedRole();
        };
        var resetButton = new Button { X = Pos.Right(rolesList) + 2, Y = Pos.Bottom(editButton), Text = "Reset to Seed", ShadowStyle = ShadowStyles.None };
        resetButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (rolesList.SelectedItem is int index && index >= 0 && index < roles.Length)
            {
                state.ResetToSeed(roles[index]);
                RefreshRoles();
                RefreshWarnings();
                Preview();
            }
        };

        void Save()
        {
            var name = nameField.Text.Trim();
            if (!ProfileName.IsValid(name))
            {
                MessageBox.ErrorQuery(app, "dev-term", "Enter a valid theme name.", "Ok");
                return;
            }

            if (BuiltInThemes.IsReservedName(name))
            {
                MessageBox.ErrorQuery(app, "dev-term", $"'{name}' is reserved for a built-in theme; pick another name.", "Ok");
                return;
            }

            state.Name = name;
            var path = Path.Combine(directory, $"{name}.json");
            if (File.Exists(path) && MessageBox.Query(app, "dev-term", $"A theme named '{name}' already exists. Overwrite it?", ["Yes", "No"]) != 0)
            {
                return;
            }

            if (state.Save(path) is { } error)
            {
                MessageBox.ErrorQuery(app, "dev-term", error, "Ok");
                return;
            }

            ActiveTheme.UseCatalog(ThemeCatalog.Load(directory));
            ActiveTheme.Select(name);
            app.RequestStop();
        }

        var saveButton = new Button { X = 0, Y = Pos.AnchorEnd(1), Text = "Save", IsDefault = true, ShadowStyle = ShadowStyles.None };
        saveButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Save();
        };
        var cancelButton = new Button { X = Pos.Right(saveButton) + 1, Y = Pos.AnchorEnd(1), Text = "Cancel", ShadowStyle = ShadowStyles.None };
        cancelButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            app.RequestStop();
        };

        window.Add(nameLabel, nameField, paletteLabel, paletteSelector, rolesLabel, rolesList, editButton, resetButton, warningsLabel, saveButton, cancelButton);

        return new ThemeBuilderWindowParts
        {
            Window = window,
            State = state,
            NameField = nameField,
            PaletteSelector = paletteSelector,
            RolesList = rolesList,
            WarningsLabel = warningsLabel,
            EditButton = editButton,
            ResetButton = resetButton,
            SaveButton = saveButton,
            CancelButton = cancelButton,
        };
    }

    /// <summary>A small modal hex-color editor for one role. Returns null when cancelled.</summary>
    internal static ThemeColor? EditColor(IApplication app, ThemeRole role, ThemeColor current)
    {
        ThemeColor? picked = null;
        var dialog = new Dialog { Title = $"Edit {ThemeFile.RoleName(role)}", Width = 44, Height = 8 };
        var label = new Label { X = 0, Y = 0, Text = "Hex color (#RRGGBB):" };
        var field = new TextField { X = 0, Y = 1, Width = Dim.Fill(), Text = current.ToHex() };

        void Accept()
        {
            if (!ThemeColor.TryParse(field.Text, out var parsed))
            {
                MessageBox.ErrorQuery(app, "dev-term", $"'{field.Text}' isn't a valid #RRGGBB color.", "Ok");
                return;
            }

            picked = parsed;
            app.RequestStop();
        }

        field.Accepting += (_, e) =>
        {
            e.Handled = true;
            Accept();
        };
        var okButton = new Button { X = 0, Y = Pos.AnchorEnd(2), Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Accept();
        };
        var cancelButton = new Button { X = Pos.Right(okButton) + 1, Y = Pos.AnchorEnd(2), Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            app.RequestStop();
        };

        dialog.Add(label, field, okButton, cancelButton);
        try
        {
            app.Run(dialog);
        }
        finally
        {
            dialog.Dispose();
        }

        return picked;
    }

    private static string RoleRow(ThemeBuilderState state, ThemeRole role) =>
        (state.IsOverridden(role) ? _overriddenMarker : _unchangedMarker) + ThemeFile.RoleName(role).PadRight(24) + state[role].ToHex();

    private static string SeedLabel(string name) => name switch
    {
        BuiltInThemes.LightName => "Light",
        BuiltInThemes.DarkName => "Dark",
        BuiltInThemes.SystemName => "System (follow the OS)",
        _ => name,
    };

    private static string DefaultName(string seedName) => $"{SeedLabel(seedName)} copy";
}

/// <summary>The controls a test needs to drive <see cref="ThemeBuilderMode"/> headlessly.</summary>
internal sealed class ThemeBuilderWindowParts
{
    public required Window Window { get; init; }

    public required ThemeBuilderState State { get; init; }

    public required TextField NameField { get; init; }

    public required OptionSelector PaletteSelector { get; init; }

    public required ListView RolesList { get; init; }

    public required Label WarningsLabel { get; init; }

    public required Button EditButton { get; init; }

    public required Button ResetButton { get; init; }

    public required Button SaveButton { get; init; }

    public required Button CancelButton { get; init; }
}
