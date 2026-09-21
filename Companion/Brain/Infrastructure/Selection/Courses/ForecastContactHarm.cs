using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

public readonly record struct ContactBox(double X, double Y, double Width, double Height)
{
    public bool Intersects(ContactBox other) => Width > 0 && Height > 0 && other.Width > 0 && other.Height > 0
        && X < other.X + other.Width && X + Width > other.X
        && Y < other.Y + other.Height && Y + Height > other.Y;
}
/// <summary>
/// One body a hostile can hurt. <paramref name="ImmunityTicks"/> is how long this actor cannot be hurt
/// again after an ordinary hit and <paramref name="MinimalHitImmunityTicks"/> after one that lands for a
/// single point — the game's own windows, supplied by the native producer rather than written here,
/// because a player's and an NPC's come from different code and the player's doubles with gear.
///
/// The one-damage branch is carried rather than folded away because it halves the cadence, which is the
/// quantity this whole forecast is about: an armoured player whose defence floors a weak hostile's hit at
/// one point is hurt twice as often as the same player taking real damage, and a model that priced both
/// at the ordinary window would understate exactly the case where a persistent threat is most survivable
/// and least worth interrupting a job for. An actor with no such branch passes the same value twice.
///
/// Both are required and neither has a default. A default of zero would make a hostile standing on a
/// body hit it sixty times a second, and a default of "one hit only" is the very thing these replace;
/// either would be a silent answer to the question the caller was supposed to answer.
/// </summary>
public sealed record ContactActor(HarmActor Actor, double Life, int ReadyTick, int ImmunityTicks,
    int MinimalHitImmunityTicks, IReadOnlyList<ContactBox> Boxes);
public readonly record struct ContactSample(ContactBox Box, double Damage, int ReadyTick);
public sealed record ContactGeometry(IReadOnlyList<ContactSample> Samples, bool Supported);
/// <summary>
/// One hostile as a source of contact against either actor, and — when the course under evaluation is
/// predicted to kill it — the tick that course's own effects say it dies on.
///
/// The kill is carried here rather than rewarded anywhere else, because the two are different claims and
/// only one of them is true: a course that kills a zombie does not earn a prevention bonus, it simply
/// has fewer contacts after that tick, for the player as well as for the companion. Expressed as a
/// bonus it would be paid twice for a hostile that was never going to touch anybody; expressed as a
/// truncation it is worth exactly the harm it removes, which is nothing when the hostile was harmless
/// and a great deal when it was about to reach the player.
/// </summary>
public sealed record ContactThreat(int Slot, long Generation, ContactGeometry ToPlayer, ContactGeometry ToCompanion,
    double? KilledAtTick = null);
public sealed record ContactHarmResult(IReadOnlyList<PredictedHarm> Harm, bool TailUnresolved, int RequestedHorizon);

/// <summary>
/// Every contact a body takes over the horizon, over captured per-tick geometry, with the game's own
/// immunity window between one hit and the next.
///
/// **It priced one hit per actor and stopped there until 21 September 2026, and the cost of that was a
/// whole behaviour rather than a wrong number.** A zombie walking at a forty-life player lands its hit
/// about thirty ticks in whatever the companion does, so with one hit priced the harm was identical on
/// every course — mining, fighting and standing still all read 0.3500 — and defending him was worth
/// exactly nothing. The objective could not prefer the course that saves him because there was no term
/// in which the courses differed. In the game he is hit again every immunity window until something
/// kills the zombie, and that difference is the entire value of intervening.
///
/// So a hit no longer ends the actor's continuation. It costs the actor its damage, makes it immune for
/// its own window, and the scan resumes after that window against the same trajectories. An actor whose
/// life the accumulated hits exhaust stops, because nothing further can be taken from it.
///
/// **What this deliberately does not model is knockback, and that makes the cadence an upper bound.**
/// A real hit shoves the victim away, so the next contact comes later than the immunity window alone
/// would say. The captured geometry is a frozen per-tick trajectory and cannot be re-simulated from
/// inside this forecast, so the honest statement is that repeated contacts are counted at the fastest
/// rate the game permits. It is a bound in the direction that matters least: the quantity the objective
/// actually reads is the difference between a course that removes the threat and one that does not, and
/// the kill truncation below is exact, so an over-fast cadence scales both sides of that comparison
/// rather than tilting it. Modelling the shove needs the victim's knockback resistance and the hit's
/// own direction, which belong to the native producer, and that is the next thing to build here.
///
/// Missing geometry never means safety.
/// </summary>
public sealed class ForecastContactHarm
{
    private readonly ContactActor[] actors;
    private readonly ContactThreat[] threats;
    private readonly int horizon;
    private readonly List<PredictedHarm> harm = new();
    private int actor, tick, threat;
    /// <summary>The current actor's life as the accumulated hits have left it. It is carried rather than
    /// read from the record each tick because a second hit has to be priced against what the first left,
    /// which is what lets the objective see a hit that would finish somebody.</summary>
    private double life;
    private bool unresolved;
    private ContactHarmResult? result;

    public ForecastContactHarm(IEnumerable<ContactActor> actors, IEnumerable<ContactThreat> threats, int horizon, bool censusComplete)
    {
        if (horizon < 0) throw new ArgumentOutOfRangeException(nameof(horizon));
        this.actors = actors.Select(value => value with { Boxes = Array.AsReadOnly(value.Boxes.ToArray()) }).ToArray();
        this.threats = threats.OrderBy(value => value.Slot)
            .Select(value => value with { ToPlayer = Freeze(value.ToPlayer), ToCompanion = Freeze(value.ToCompanion) }).ToArray();
        if (this.actors.Select(value => value.Actor).Distinct().Count() != this.actors.Length
            || this.threats.Select(value => value.Slot).Distinct().Count() != this.threats.Length)
            throw new ArgumentException("A contact census must contain each actor and hostile slot once.");
        if (this.actors.Any(value => !double.IsFinite(value.Life) || value.Life < 0 || value.ReadyTick < 0
                || value.ImmunityTicks < 1 || value.MinimalHitImmunityTicks < 1)
            || this.threats.SelectMany(value => value.ToPlayer.Samples.Concat(value.ToCompanion.Samples))
                .Any(value => !double.IsFinite(value.Damage) || value.Damage < 0 || value.ReadyTick < 0)
            || this.actors.SelectMany(value => value.Boxes).Concat(this.threats.SelectMany(value =>
                value.ToPlayer.Samples.Concat(value.ToCompanion.Samples).Select(sample => sample.Box))).Any(box =>
                !double.IsFinite(box.X) || !double.IsFinite(box.Y) || !double.IsFinite(box.Width)
                || !double.IsFinite(box.Height) || !double.IsFinite(box.X + box.Width)
                || !double.IsFinite(box.Y + box.Height) || box.Width < 0 || box.Height < 0))
            throw new ArgumentException("Contact inputs require finite geometry, life, damage and an immunity window of at least one tick.");
        this.horizon = horizon;
        unresolved = !censusComplete;
        life = this.actors.Length > 0 ? this.actors[0].Life : 0;
    }

    public ContactHarmResult? Continue(DecisionWorkBudget budget)
    {
        if (result != null) return result;
        while (actor < actors.Length)
        {
            if (!budget.TrySpend("course-contact-harm")) return null;
            var body = actors[actor];
            if (life == 0 || tick > horizon || threats.Length == 0) { NextActor(); continue; }
            if (tick < body.ReadyTick) { tick = body.ReadyTick; continue; }
            if (tick >= body.Boxes.Count) { unresolved = true; NextActor(); continue; }
            var enemy = threats[threat];
            var geometry = body.Actor == HarmActor.Player ? enemy.ToPlayer : enemy.ToCompanion;
            // A hostile this course kills makes no contact from the tick it dies on, and that is a
            // resolved answer rather than a gap: the course's own effects say it is gone, so nothing is
            // missing and the tail is not made unresolved by it. Reading it as unresolved instead would
            // leave a course that clears the room permanently unable to be better than one that does
            // not, which is the whole behaviour this exists for.
            if (enemy.KilledAtTick is { } dead && tick >= dead) { }
            else if (!geometry.Supported || tick >= geometry.Samples.Count) unresolved = true;
            else
            {
                var contact = geometry.Samples[tick];
                if (tick >= contact.ReadyTick && contact.Damage > 0 && body.Boxes[tick].Intersects(contact.Box))
                {
                    // The life carried into the hit, so a reader can tell a scratch from the one that
                    // finishes somebody, and then the life the hit leaves.
                    harm.Add(new(body.Actor, contact.Damage, life, tick, EstimateStatus.Nominal));
                    life = Math.Max(0, life - contact.Damage);
                    // A body with nothing left takes nothing more. Everything after this tick belongs to
                    // a world where it is already down, and pricing further contacts on it would charge a
                    // course for harm to somebody the same forecast has just said is gone.
                    if (life == 0) { NextActor(); continue; }
                    // Otherwise the game's own immunity window, and the scan resumes after it against the
                    // same trajectories. The tail is no longer made unresolved by the hit itself — it was,
                    // while a hit ended the scan, because everything after one was genuinely unknown.
                    // What is unknown now is only what the geometry stops supporting, below.
                    // The game's own branch: a hit that lands for a single point buys half the window.
                    tick += contact.Damage <= 1 ? body.MinimalHitImmunityTicks : body.ImmunityTicks;
                    threat = 0;
                    continue;
                }
            }
            if (++threat == threats.Length) { threat = 0; tick++; }
        }
        return result = new(Array.AsReadOnly(harm.ToArray()), unresolved, horizon);
    }

    private void NextActor()
    {
        actor++; tick = 0; threat = 0;
        life = actor < actors.Length ? actors[actor].Life : 0;
    }
    private static ContactGeometry Freeze(ContactGeometry geometry)
        => geometry with { Samples = Array.AsReadOnly(geometry.Samples.ToArray()) };
}
