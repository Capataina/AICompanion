using System.Reflection;
using System.Runtime.Loader;

// The installed game's libraries, resolved the way every other tool here resolves them: the first
// bare argument is the tModLoader root, so a different installation can be pointed at without a
// rebuild.
string root = args.FirstOrDefault(a => !a.StartsWith("--"))
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library/Application Support/Steam/steamapps/common/tModLoader");
var libraries = Directory.GetFiles(Path.Combine(root, "Libraries"), "*.dll", SearchOption.AllDirectories);
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    string? path = libraries.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).Equals(name.Name, StringComparison.OrdinalIgnoreCase));
    return path == null ? null : context.LoadFromAssemblyPath(path);
};

// Before anything touches Terraria.Main, whose static constructor combines paths from SavePath and
// poisons every later use of Main if it is null.
typeof(Terraria.Program).GetField("SavePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
    .SetValue(null, Path.GetTempPath());

return WorldRunEntry.Run(args);
