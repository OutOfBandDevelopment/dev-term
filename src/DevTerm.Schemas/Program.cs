using DevTerm.Schemas;

// Usage: DevTerm.Schemas <output-folder>   (the build runs this into <repo>/schemas)
if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: DevTerm.Schemas <output-folder>");
    return 1;
}

Directory.CreateDirectory(args[0]);
foreach (var (name, text) in SchemaGenerator.Generate())
{
    File.WriteAllText(Path.Combine(args[0], name), text);
    Console.WriteLine($"wrote {name}");
}

return 0;
