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
// Every flag below is one case, and a case starts from fresh-case state: the suite's cases get it
// from BeforeCase through the ledger, but these early returns never reach the ledger, so without
// this they run on a fresh process's statics — the Thompson sampler's per-process seed and the
// production wall-clock allowances. The combat-purpose matrix showed both: its values moved run to
// run standalone (real shots taught with per-process draws, then read back) while identical in-suite.
ResetProcessState.BeforeCase(keepProductionAllowances: false);

if (args.Contains("--orb-contact")) return VerifyOrbContact.SizeRule() + VerifyOrbContact.DiagonalStep() + VerifyOrbContact.PushOutAndSlide();
if (args.Contains("--free-space")) return VerifyFreeSpace.CorridorsAndLiquids() + VerifyFreeSpace.FloodFinishes() + VerifyFreeSpace.FloodBounded();
if (args.Contains("--route-endings")) return VerifyRouteEndings.Run() == 0 ? 0 : 1;
if (args.Contains("--attack-outcomes")) return VerifyAttackOutcomes.Run();
if (args.Contains("--knockback")) return VerifyKnockbackAwareness.Run();
if (args.Contains("--experience")) return VerifyCompanionExperience.Run();
if (args.Contains("--weapon-learning")) return VerifyWeaponLearning.Run();
if (args.Contains("--volley-learning"))
    return RunOneRow.Case("spread and burst group into one use each", () => VerifyVolleyLearning.SpreadAndBurstGroupIntoOneUseEach())
        + RunOneRow.Case("arcs are learned from the player's flights", () => VerifyVolleyLearning.ArcsAreLearnedFromThePlayersFlights())
        + RunOneRow.Case("companion shots read the companion's aim", () => VerifyVolleyLearning.CompanionShotsReadTheCompanionsAimAsTheirCursor());
if (args.Contains("--flight-laws"))
    return RunOneRow.Case("delayed gravity matches native tick for tick", () => VerifyFlightLaws.DelayedGravityMatchesNativeTickForTick())
        + RunOneRow.Case("bounce points are predicted in a fixture box", () => VerifyFlightLaws.BouncePointsArePredictedInAFixtureBox())
        + RunOneRow.Case("modified traces leave law and hit response unchanged", () => VerifyFlightLaws.ModifiedTracesLeaveLawAndHitResponseUnchanged())
        + RunOneRow.Case("an unpredictable type still fires", () => VerifyFlightLaws.AnUnpredictableTypeStillFires())
        + RunOneRow.Case("homing predicts its path to a placed body", () => VerifyFlightLaws.HomingPredictsItsPathToAPlacedBody())
        + RunOneRow.Case("pass-through is predicted through a wall", () => VerifyFlightLaws.PassThroughIsPredictedThroughAWall())
        + RunOneRow.Case("splitting shots children are predicted by trigger and count", () => VerifyFlightLaws.SplittingShotsChildrenArePredictedByTriggerAndCount());
if (args.Contains("--simulated-uses"))
    return RunOneRow.Case("quarter shares land a quarter per pellet", () => VerifySimulatedUses.QuarterSharesLandAQuarterPerPellet())
        + RunOneRow.Case("unlimited pierce strikes twenty", () => VerifySimulatedUses.UnlimitedPierceStrikesTwenty())
        + RunOneRow.Case("four pellets on a low body record four hits", () => VerifySimulatedUses.FourPelletsOnALowBodyRecordFourHits())
        + RunOneRow.Case("timed children land after their parent", () => VerifySimulatedUses.TimedChildrenLandAfterTheirParent())
        + RunOneRow.Case("the same decision simulated twice is identical", () => VerifySimulatedUses.TheSameDecisionSimulatedTwiceIsIdentical());
if (args.Contains("--weapon-outcome-credit"))
    return RunOneRow.Case("misses stay with their enemy type", () => VerifyWeaponLearning.MissesAgainstOneEnemyTypeStayWithThatType())
        + RunOneRow.Case("a swing kill is the strike", () => VerifyWeaponLearning.ASwingKillIsRecordedAsTheStrike())
        + RunOneRow.Case("the push charge is at the learned hit rate", () => VerifyWeaponLearning.APushIsChargedAtTheLearnedHitRate())
        + RunOneRow.Case("a target killed by someone else teaches nothing", () => VerifyWeaponLearning.AShotWhoseTargetDiedToSomeoneElseTeachesNothing())
        + RunOneRow.Case("the target hold survives ordinary motion", () => VerifyWeaponLearning.TheTargetHoldSurvivesOrdinaryMotion())
        + RunOneRow.Case("every companion projectile is the companion's", () => VerifyWeaponLearning.EveryProjectileTheCompanionSpawnsIsTheCompanions());
if (args.Contains("--combat-activity"))
    return RunOneRow.Case("only combat fires", () => VerifyCombatActivity.OnlyCombatFires())
        + RunOneRow.Case("shared eagerness", () => VerifyCombatActivity.SharedEagerness())
        + RunOneRow.Case("danger over work", () => VerifyCombatActivity.DangerLiftsCombatOverWork())
        + RunOneRow.Case("distant idle enemy", () => VerifyCombatActivity.DistantIdleEnemyKeepsOffTheVein())
        + RunOneRow.Case("dodge bends mining", () => VerifyCombatActivity.DodgeBendsMiningWithoutStoppingIt())
        + RunOneRow.Case("unarmed offers nothing", () => VerifyCombatActivity.UnarmedOffersNoCombat())
        + RunOneRow.Case("hunting-off migration", () => VerifyCombatActivity.HuntingOffMigration());
if (args.Contains("--offer-validity")) return VerifyOfferValidity.Run();
if (args.Contains("--attack-planning"))
    return RunOneRow.Case("a dominated plan never survives the front, whatever the weights", () => VerifyAttackPlanning.ADominatedPlanNeverSurvivesTheFront());
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
if (args.Contains("--activities")) return VerifyEngineMotion.Run(activitiesOnly: true);
return VerifyEngineMotion.Run(args.Contains("--lifecycle"), args.Contains("--liquids"));
