using System.Reflection;
using System.Runtime.Loader;

string root = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/Steam/steamapps/common/tModLoader");
var libraries = Directory.GetFiles(Path.Combine(root, "Libraries"), "*.dll", SearchOption.AllDirectories);
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    string? path = libraries.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).Equals(name.Name, StringComparison.OrdinalIgnoreCase));
    return path == null ? null : context.LoadFromAssemblyPath(path);
};
if (args.Contains("--route-persistence")) return VerifyRoutePersistence.Run();
if (args.Contains("--attack-outcomes")) return VerifyAttackOutcomes.Run();
if (args.Contains("--render-ui")) return RenderNativeInterface.Run(root);
if (args.FirstOrDefault(a => a.StartsWith("--replay-water=")) is string capture)
    return ReplayRecordedWater.Run(capture[15..], int.Parse(args.Single(a => a.StartsWith("--tick="))[7..]));
if (args.Contains("--ore-work")) return VerifyEngineMotion.Run(workOnly: true);
if (args.Contains("--follow")) return VerifyEngineMotion.Run(followOnly: true);
if (args.Contains("--protection-recovery")) return VerifyEngineMotion.Run(protectionOnly: true);
if (args.Contains("--observation")) return VerifyObservationLifecycle.Run();
return VerifyEngineMotion.Run(args.Contains("--lifecycle"), args.Contains("--escape"));
