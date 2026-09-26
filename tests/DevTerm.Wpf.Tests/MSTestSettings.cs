// No parallel tests in this assembly. Each WPF test runs on its own STA thread (StaTestRunner), and
// WPF's XAML parser initializes shared static caches on first use: two windows parsed at the same
// moment on different threads intermittently failed with "XamlParseException: The given key 'Title'
// was not present in the dictionary" (from a ConcurrentDictionary inside the parser) or a bare
// NullReferenceException. Found 2026-09-25 when MainWindowErrorHandlingTests - the one class that
// had no [DoNotParallelize] - ran its MainWindow tests concurrently. Assembly-wide so a new test
// class can't reintroduce it by forgetting the attribute.
[assembly: DoNotParallelize]
