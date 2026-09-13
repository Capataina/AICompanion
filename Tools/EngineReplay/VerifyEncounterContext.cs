extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.Events;
using Terraria.ID;
using Encounter = live::AICompanion.Companion.Brain.WorldObservation.EncounterSense;
using Threats = live::AICompanion.Companion.Brain.WorldObservation.ThreatSense;
using Threat = live::AICompanion.Companion.Brain.WorldObservation.ThreatRecord;
using Evaluate = live::AICompanion.Companion.Brain.BehaviourSelection.EvaluatePreparedActivities;
using Prepared = live::AICompanion.Companion.Brain.BehaviourSelection.PreparedActivity;
using Comparison = live::AICompanion.Companion.Brain.BehaviourSelection.ActivityComparisonContext;
using Eligibility = live::AICompanion.Companion.Brain.Behaviours.OfferEligibility;
using Policy = live::AICompanion.Companion.Brain.Behaviours.Work.WorkPolicy;
using Weights = live::AICompanion.Companion.Brain.BehaviourSelection.Weights;

/// <summary>
/// Proposal 1's boss and event context, proven at three depths. The observation must say the world is
/// about one thing from native facts alone — a boss flag at any depth, the Old One's Army at any depth, a
/// moon, eclipse or invasion only at the surface — and fall back to sustained reachable crowd pressure,
/// marked unrecognised, for an event nothing names. The shared evaluator must charge that context once,
/// to optional non-combat work only. And the live brain, in one mining scene that differs only in the
/// event flag and the player's depth, must stop mining exactly when the event reaches it.
/// </summary>
internal static class VerifyEncounterContext
{
    public static void Run()
    {
        bool bloodMoon = Main.bloodMoon, eclipse = Main.eclipse, pumpkin = Main.pumpkinMoon, snow = Main.snowMoon;
        bool dd2 = DD2Event.Ongoing;
        int invasion = Main.invasionType;
        double surface = Main.worldSurface;
        try
        {
            TheObservationReadsNativeFactsAndFallsBackToObservedPressure();
            TheEvaluatorChargesAnEncounterOnceAndOnlyToOptionalNonCombatWork();
            TheLiveBrainStopsMiningOnlyWhereTheEventReachesIt();
        }
        finally
        {
            Main.bloodMoon = bloodMoon; Main.eclipse = eclipse; Main.pumpkinMoon = pumpkin; Main.snowMoon = snow;
            DD2Event.Ongoing = dd2;
            Main.invasionType = invasion;
            Main.worldSurface = surface;
        }
    }

    private static void ClearWorldEvents()
    {
        Main.bloodMoon = Main.eclipse = Main.pumpkinMoon = Main.snowMoon = false;
        DD2Event.Ongoing = false;
        Main.invasionType = 0;
    }

    private static (float Intensity, string Source, bool Recognised) Observe(bool surface, int reaching = 0,
        int unreachable = 0, bool boss = false, int ticks = 1, Encounter? sense = null)
    {
        var player = new Player();
        Main.worldSurface = 100;
        player.position = new Vector2(800, (surface ? 60 : 140) * 16);
        var threats = new Threats();
        for (int i = 0; i < reaching; i++) threats.Threats.Add(new Threat { IsBoss = boss && i == 0 });
        for (int i = 0; i < unreachable; i++) threats.Threats.Add(new Threat { CanReachPlayer = false, CanReachCompanion = false });
        sense ??= new Encounter();
        for (int t = 0; t < ticks; t++) sense.Update(player, threats);
        return (sense.Intensity, sense.Source, sense.Recognised);
    }

    private static void TheObservationReadsNativeFactsAndFallsBackToObservedPressure()
    {
        ClearWorldEvents();
        var none = Observe(surface: true);
        Require(none is (0f, "none", false), $"a quiet surface with no hostiles must be no encounter; got {none}");

        Main.bloodMoon = true;
        var moonUp = Observe(surface: true);
        var moonDown = Observe(surface: false);
        Require(moonUp is (1f, "event:blood-moon", true), $"a blood moon over a surface player is a recognised encounter; got {moonUp}");
        Require(moonDown is (0f, "none", false),
            $"a blood moon does not reach a player underground, where the game spawns none of it; got {moonDown}");
        ClearWorldEvents();

        Main.invasionType = 1;
        var invasionUp = Observe(surface: true);
        Require(invasionUp is (1f, "event:invasion", true), $"an active invasion at the surface is recognised; got {invasionUp}");
        ClearWorldEvents();

        DD2Event.Ongoing = true;
        var armyDown = Observe(surface: false);
        Require(armyDown is (1f, "event:old-ones-army", true), $"the Old One's Army is recognised at any depth; got {armyDown}");
        ClearWorldEvents();

        var bossDown = Observe(surface: false, reaching: 1, boss: true);
        Require(bossDown is (1f, "boss", true), $"an observed boss is a recognised encounter underground as well; got {bossDown}");

        int crowd = Weights.EncounterPressureHostiles;
        int window = (int)Weights.EncounterPressureTicks;
        var brief = Observe(surface: false, reaching: crowd, ticks: window / 2);
        var sustained = Observe(surface: false, reaching: crowd, ticks: window * 2);
        var thin = Observe(surface: false, reaching: crowd - 1, ticks: window * 2);
        var walledOff = Observe(surface: false, reaching: crowd - 1, unreachable: 1, ticks: window * 2);
        Require(brief.Source == "observed-pressure" && !brief.Recognised && MathF.Abs(brief.Intensity - .5f) < .01f,
            $"half the pressure window of a reachable crowd must read as a half-strength inferred encounter; got {brief}");
        Require(sustained is (1f, "observed-pressure", false),
            $"sustained crowd pressure with no native flag must reach full strength and stay marked unrecognised; got {sustained}");
        Require(thin is (0f, "none", false), $"one hostile short of the crowd threshold must never build pressure; got {thin}");
        Require(walledOff is (0f, "none", false),
            $"a hostile that can reach neither actor must not count toward crowd pressure; got {walledOff}");

        var lapsing = new Encounter();
        Observe(surface: false, reaching: crowd, ticks: window * 2, sense: lapsing);
        var lapsed = Observe(surface: false, reaching: crowd - 1, sense: lapsing);
        Require(lapsed is (0f, "none", false),
            $"the first tick the crowd thins, the inferred encounter must end completely rather than wind down; got {lapsed}");
        Console.WriteLine($"  encounter sense rows: moon surface {moonUp}, moon underground {moonDown}, army {armyDown}, boss {bossDown}, half window {brief}, sustained {sustained}, thin {thin}, walled {walledOff}, lapsed {lapsed}");
    }

    private static void TheEvaluatorChargesAnEncounterOnceAndOnlyToOptionalNonCombatWork()
    {
        var board = new[]
        {
            new Prepared(0, "mine", .8f, 60, IsExcursion: true, HasTarget: true, IsFollowing: false, IsIncumbent: false, Eligibility.Usable),
            new Prepared(1, "hunt", .3f, 60, IsExcursion: true, HasTarget: true, IsFollowing: false, IsIncumbent: false, Eligibility.Usable, ServesEncounter: true),
            new Prepared(2, "keep-company", .2f, 0, IsExcursion: false, HasTarget: false, IsFollowing: true, IsIncumbent: false, Eligibility.Usable),
        };
        Comparison Context(float urgency, float encounter, bool stranded = false)
            => new(urgency, stranded, float.PositiveInfinity, 30, 600, 1, false, 1, 0, encounter);

        var calm = Evaluate.Evaluate(board, Context(0, 0));
        var full = Evaluate.Evaluate(board, Context(0, 1));
        var half = Evaluate.Evaluate(board, Context(0, .5f));
        var urgentOnly = Evaluate.Evaluate(board, Context(.6f, 0));
        var both = Evaluate.Evaluate(board, Context(.6f, .6f));
        var stranded = Evaluate.Evaluate(board, Context(0, 1, stranded: true));
        var invalid = Evaluate.Evaluate(board, Context(0, 1.5f));

        Require(calm[0].Final > calm[1].Final && calm[0].Final > calm[2].Final,
            $"with no encounter the fixture must start with mining ahead, or the pair proves nothing; mine={calm[0].Final} hunt={calm[1].Final} keep={calm[2].Final}");
        Require(full[0].Final == 0f && full[1].Final == calm[1].Final && full[2].Final == calm[2].Final,
            $"a full encounter must remove mining's value and leave hunting and keeping company exactly as they were; mine={full[0].Final} hunt={full[1].Final}/{calm[1].Final} keep={full[2].Final}/{calm[2].Final}");
        Require(MathF.Abs(half[0].Protection - .5f) < 1e-5f, $"a half-strength encounter halves optional work; protection={half[0].Protection}");
        Require(MathF.Abs(both[0].Protection - urgentOnly[0].Protection) < 1e-5f && MathF.Abs(both[0].Protection - .4f) < 1e-5f,
            $"urgency and an equal encounter are one danger read twice, so mining must pay 0.4 once, not 0.16; both={both[0].Protection} urgency-only={urgentOnly[0].Protection}");
        Require(MathF.Abs(both[1].Protection - .4f) < 1e-5f,
            $"combat still pays the player's urgency during an encounter, and only that; hunt protection={both[1].Protection}");
        Require(stranded[0].Protection == 1f, $"a stranded companion's optional work is not charged for an encounter either; protection={stranded[0].Protection}");
        Require(invalid[0].Error == "invalid-encounter", $"an intensity outside 0..1 is an adapter defect and must be refused; error='{invalid[0].Error}'");
        Console.WriteLine($"  encounter evaluation rows: calm mine {calm[0].Final:0.###} hunt {calm[1].Final:0.###} keep {calm[2].Final:0.###}; full mine {full[0].Final:0.###} hunt {full[1].Final:0.###} keep {full[2].Final:0.###}; urgency+encounter protection {both[0].Protection:0.###}");
    }

    private static (string? Chosen, float Mine, string Source) MiningScene(bool bloodMoon, bool playerOnSurface)
    {
        ClearWorldEvents();
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Opportunistic, TileID.Copper, new Point(25, 89));
        Main.bloodMoon = bloodMoon;
        Main.worldSurface = playerOnSurface ? 120 : 40;
        for (int t = 0; t < 3; t++) VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
        var chooser = ctx.Companion.Brain.Chooser;
        float mine = 0f;
        foreach (var score in chooser.LastScores)
            if (score.Action.Name == "mine") mine = score.Final;
        return (chooser.Current?.Name, mine, ctx.Companion.Brain.Senses.Encounter.Source);
    }

    private static void TheLiveBrainStopsMiningOnlyWhereTheEventReachesIt()
    {
        var quiet = MiningScene(bloodMoon: false, playerOnSurface: true);
        var moonUp = MiningScene(bloodMoon: true, playerOnSurface: true);
        var moonDown = MiningScene(bloodMoon: true, playerOnSurface: false);
        Console.WriteLine($"  encounter live rows: quiet {quiet}, blood moon surface {moonUp}, blood moon underground {moonDown}");
        Require(quiet.Chosen == "mine" && quiet.Mine > 0f,
            $"the quiet scene must choose the reachable ore, or the pair proves nothing; got {quiet}");
        Require(moonUp.Source == "event:blood-moon" && moonUp.Mine == 0f && moonUp.Chosen != "mine",
            $"a blood moon over the player must stop the same mining job through the shared evaluator; got {moonUp}");
        Require(moonDown.Source == "none" && moonDown.Chosen == "mine" && MathF.Abs(moonDown.Mine - quiet.Mine) < 1e-4f,
            $"the same blood moon with the player underground must leave mining exactly as the quiet scene valued it; quiet={quiet} underground={moonDown}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("encounter context: " + message);
    }
}
