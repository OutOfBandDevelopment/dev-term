using System.Runtime.CompilerServices;
using System.Windows;

// Lets DevTerm.Wpf.Tests drive MainWindow's testable entry points (ConnectAsync,
// SendCurrentInputAsync) and its XAML-named controls (internal by WPF's default
// x:FieldModifier) directly, instead of needing OS-level UI Automation for basic coverage.
[assembly: InternalsVisibleTo("DevTerm.Wpf.Tests")]

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
