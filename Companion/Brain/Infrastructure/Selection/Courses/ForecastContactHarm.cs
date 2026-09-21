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
public sealed record ContactActor(HarmActor Actor, double Life, int ReadyTick, IReadOnlyList<ContactBox> Boxes);
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

/// <summary>First contact over captured per-tick geometry. A hit ends that actor's supported
/// continuation: immunity, knockback and native hooks need a successor model before more
/// overlap can be priced as another hit. Missing geometry never means safety.</summary>
public sealed class ForecastContactHarm
{
    private readonly ContactActor[] actors;
    private readonly ContactThreat[] threats;
    private readonly int horizon;
    private readonly List<PredictedHarm> harm = new();
    private int actor, tick, threat;
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
        if (this.actors.Any(value => !double.IsFinite(value.Life) || value.Life < 0 || value.ReadyTick < 0)
            || this.threats.SelectMany(value => value.ToPlayer.Samples.Concat(value.ToCompanion.Samples))
                .Any(value => !double.IsFinite(value.Damage) || value.Damage < 0 || value.ReadyTick < 0)
            || this.actors.SelectMany(value => value.Boxes).Concat(this.threats.SelectMany(value =>
                value.ToPlayer.Samples.Concat(value.ToCompanion.Samples).Select(sample => sample.Box))).Any(box =>
                !double.IsFinite(box.X) || !double.IsFinite(box.Y) || !double.IsFinite(box.Width)
                || !double.IsFinite(box.Height) || !double.IsFinite(box.X + box.Width)
                || !double.IsFinite(box.Y + box.Height) || box.Width < 0 || box.Height < 0))
            throw new ArgumentException("Contact inputs require finite geometry, life and damage.");
        this.horizon = horizon;
        unresolved = !censusComplete;
    }

    public ContactHarmResult? Continue(DecisionWorkBudget budget)
    {
        if (result != null) return result;
        while (actor < actors.Length)
        {
            if (!budget.TrySpend("course-contact-harm")) return null;
            var body = actors[actor];
            if (body.Life == 0 || tick > horizon || threats.Length == 0) { NextActor(); continue; }
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
                    harm.Add(new(body.Actor, contact.Damage, body.Life, tick, EstimateStatus.Nominal));
                    if (tick < horizon) unresolved = true;
                    NextActor(); continue;
                }
            }
            if (++threat == threats.Length) { threat = 0; tick++; }
        }
        return result = new(Array.AsReadOnly(harm.ToArray()), unresolved, horizon);
    }

    private void NextActor() { actor++; tick = 0; threat = 0; }
    private static ContactGeometry Freeze(ContactGeometry geometry)
        => geometry with { Samples = Array.AsReadOnly(geometry.Samples.ToArray()) };
}
