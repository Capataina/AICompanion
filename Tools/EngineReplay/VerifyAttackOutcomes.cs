extern alias live;
using E = live::AICompanion.Companion.Weapons.EvaluateAttackOutcomes;

internal static class VerifyAttackOutcomes
{
    public static int Run()
    {
        var shotgun = new E.Attack(0, 0, 60, 10, new[] { new E.Hit(0, 40), new E.Hit(1, 40), new E.Hit(2, 40) });
        var sniper = new E.Attack(1, 0, 60, 10, new[] { new E.Hit(0, 300) });
        var slimes = new[] { new E.Target(0, 20, 0, 0), new E.Target(1, 20, 0, 0), new E.Target(2, 20, 0, 0) };
        var spread = E.Evaluate(shotgun, new[] { shotgun, sniper }, slimes, 0, 60);
        var single = E.Evaluate(sniper, new[] { shotgun, sniper }, slimes, 0, 60);
        Require(spread.Damage == 60 && spread.Kills == 3 && single.Damage == 20 && spread.Value > single.Value, "health-capped crowd damage");

        var urgent = new E.Attack(0, 0, 30, 5, new[] { new E.Hit(0, 20) });
        var distant = new E.Attack(0, 1, 30, 30, new[] { new E.Hit(1, 20) });
        var threats = new[] { new E.Target(0, 5, 1, 100), new E.Target(1, 200, .05f, 20) };
        Require(E.Evaluate(urgent, new[] { urgent, distant }, threats, 0, 180).Value
            > E.Evaluate(distant, new[] { urgent, distant }, threats, 0, 180).Value, "timely removal beats leaving a nearby threat alive");

        var weak = new E.Attack(0, 0, 60, 5, new[] { new E.Hit(0, 600) });
        var strong = new E.Attack(0, 1, 60, 5, new[] { new E.Hit(1, 600) });
        var equalDanger = new[] { new E.Target(0, 1, 0, 0), new E.Target(1, 500, 0, 0) };
        Require(E.Evaluate(strong, new[] { strong, weak }, equalDanger, 0, 60).Value
            > E.Evaluate(weak, new[] { strong, weak }, equalDanger, 0, 60).Value, "kill count does not erase useful damage");

        var quick = Enumerable.Range(0, 3).Select(i => new E.Attack(0, i, 6, 1, new[] { new E.Hit(i, 2) })).ToArray();
        var cleanup = E.Evaluate(quick[0], quick, slimes.Select(t => t with { Life = 1 }).ToArray(), 0, 30);
        Require(cleanup.Kills == 3 && cleanup.Damage == 3, "rapid follow-up shots retarget without overkill");
        Require(E.Evaluate(shotgun, new[] { shotgun }, slimes, 60, 60) == default, "cooldown beyond the horizon buys no damage");
        var repeated = new E.Attack(0, 0, 60, 1, new[] { new E.Hit(0, 15), new E.Hit(0, 15), new E.Hit(0, 15) });
        var repeatedOutcome = E.Evaluate(repeated, new[] { repeated }, slimes, 0, 60);
        Require(repeatedOutcome.Damage == 20 && repeatedOutcome.Kills == 1, "multiple pellets cannot earn the same kill twice");
        Console.WriteLine("PASS attack outcomes: crowd hits, urgent finishing, useful damage, rapid retargeting and cooldown");
        return 0;
    }

    private static void Require(bool condition, string claim)
    {
        if (!condition) throw new InvalidOperationException("Attack outcome regression: " + claim);
    }
}
