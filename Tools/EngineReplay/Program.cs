using System.Reflection;
using System.Runtime.Loader;

string root = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/Steam/steamapps/common/tModLoader");
var libraries = Directory.GetFiles(Path.Combine(root, "Libraries"), "*.dll", SearchOption.AllDirectories);
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    string? path = libraries.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).Equals(name.Name, StringComparison.OrdinalIgnoreCase));
    return path == null ? null : context.LoadFromAssemblyPath(path);
};
// Before any flag is dispatched, because every flag below is an early return: the process setup
// used to sit at the head of VerifyEngineMotion.Run, which only the default suite reaches, so each
// of these entry points ran against a Main nobody had prepared. --observation died there with a
// null reference and passed inside the suite, on the same code, because a fixture ahead of it in
// the table had filled the slots it needed.
ResetProcessState.PrepareProcess();
ResetProcessState.Register();

if (args.Contains("--orb-contact")) return VerifyOrbContact.SizeRule() + VerifyOrbContact.DiagonalStep() + VerifyOrbContact.PushOutAndSlide();
if (args.Contains("--free-space")) return VerifyFreeSpace.CorridorsAndLiquids() + VerifyFreeSpace.FloodFinishes() + VerifyFreeSpace.FloodBounded();
if (args.Contains("--route-endings")) return VerifyRouteEndings.Run() == 0 ? 0 : 1;
if (args.Contains("--attack-outcomes")) return VerifyAttackOutcomes.Run();
if (args.Contains("--knockback")) return VerifyKnockbackAwareness.Run();
if (args.Contains("--experience")) return VerifyCompanionExperience.Run();
if (args.Contains("--weapon-learning")) return VerifyWeaponLearning.Run();
if (args.Contains("--offer-validity")) return VerifyOfferValidity.Run();
if (args.Contains("--capability")) return VerifyCapabilityRevision.Run() == 0 ? 0 : 1;
if (args.Contains("--light-senses")) return VerifyLightAndReachSenses.Run() == 0 ? 0 : 1;
if (args.Contains("--torch-rule")) return VerifyTorchPlacementRule.Run() == 0 ? 0 : 1;
if (args.Contains("--candidate-funnel")) return VerifyCandidateFunnel.Run() == 0 ? 0 : 1;
if (args.Contains("--doors")) return VerifyDoorPassage.Run() == 0 ? 0 : 1;
if (args.Contains("--courtesy")) return VerifyCourtesy.Run() == 0 ? 0 : 1;
if (args.Contains("--render-ui")) return RenderNativeInterface.Run(root);
if (args.Contains("--ore-work")) return VerifyEngineMotion.Run(workOnly: true);
if (args.Contains("--follow")) return VerifyEngineMotion.Run(followOnly: true);
if (args.Contains("--protection-recovery")) return VerifyEngineMotion.Run(protectionOnly: true);
if (args.Contains("--observation")) return VerifyObservationLifecycle.Run();
if (args.Contains("--travel-episodes")) return VerifyTravelEpisodes.Run();
if (args.Any(a => a == "--evidence-scenes" || a.StartsWith("--evidence-scenes=", StringComparison.Ordinal))) return RecordEvidenceScenes.Run(args);
if (args.Contains("--brain-cost")) return VerifyEngineMotion.Run(brainCostOnly: true);
if (args.Contains("--combat-cost")) return VerifyEngineMotion.Run(combatCostOnly: true);
if (args.Contains("--combat-purpose")) return VerifyEngineMotion.Run(combatPurposeOnly: true);
if (args.Contains("--safety-layer")) return VerifyEngineMotion.Run(safetyLayerOnly: true);
if (args.Contains("--dodge-repro")) return VerifyEngineMotion.Run(dodgeReproOnly: true);
return VerifyEngineMotion.Run(args.Contains("--lifecycle"), args.Contains("--liquids"));
