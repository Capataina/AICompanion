extern alias live;
using Microsoft.Xna.Framework;
using Terraria;
using H = live::AICompanion.Companion.Brain.Behaviours.Combat.HuntAction;
using T = live::AICompanion.Companion.Brain.WorldObservation.ThreatRecord;
using C = live::AICompanion.Companion.Brain.Behaviours.ActionContext;

internal static class VerifyHuntProgress
{
    public static int Run()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.LocalPlayer.dead = false;
        Main.LocalPlayer.Bottom = companion.NPC.Bottom;
        companion.Brain.Senses.Update(companion.NPC, Main.LocalPlayer, companion.Breath);
        var target = new NPC(); target.SetDefaults(Terraria.ID.NPCID.Zombie);
        target.whoAmI = 12; target.active = true; target.Bottom = companion.NPC.Bottom + new Vector2(160, 0);
        Main.npc[12] = target;
        companion.Brain.Senses.Threats.Threats.Clear();
        companion.Brain.Senses.Threats.Threats.Add(new T { Npc = target, DistanceToCompanion = 160, DistanceToPlayer = 160 });
        var context = new C(companion, companion.Brain.Senses);
        var hunt = new H();
        if (hunt.Score(context) <= 0) throw new InvalidOperationException("Hunt progress fixture must offer a live target");
        for (int i = 0; i < 182; i++) hunt.ObserveOutcome(context);
        if (hunt.Score(context) != 0) throw new InvalidOperationException("Stationary hunt without attacks retained its ineffective target");
        target.position.X += 64;
        if (hunt.Score(context) <= 0) throw new InvalidOperationException("Moved target did not reopen a deferred engagement");
        for (int i = 0; i < 182; i++)
        {
            companion.NPC.position.X += 1;
            hunt.ObserveOutcome(context);
        }
        if (hunt.Score(context) <= 0) throw new InvalidOperationException("Travelling hunt was deferred despite progress");
        Console.WriteLine("PASS hunt progress: ineffective target deferred, moving target reconsidered, travelling hunt retained");
        return 0;
    }
}
