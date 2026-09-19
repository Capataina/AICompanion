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
public sealed record ContactGeometry(IReadOnlyList<ContactBox> Boxes, int ReadyTick, bool Supported);
public sealed record ContactThreat(int Slot, long Generation, double DamageToPlayer, double DamageToCompanion,
    ContactGeometry ToPlayer, ContactGeometry ToCompanion);
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
            || this.threats.Any(value => !double.IsFinite(value.DamageToPlayer) || value.DamageToPlayer < 0
                || !double.IsFinite(value.DamageToCompanion) || value.DamageToCompanion < 0
                || value.ToPlayer.ReadyTick < 0 || value.ToCompanion.ReadyTick < 0)
            || this.actors.SelectMany(value => value.Boxes).Concat(this.threats.SelectMany(value =>
                value.ToPlayer.Boxes.Concat(value.ToCompanion.Boxes))).Any(box =>
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
            if (!geometry.Supported || tick >= geometry.Boxes.Count) unresolved = true;
            else
            {
                double damage = body.Actor == HarmActor.Player ? enemy.DamageToPlayer : enemy.DamageToCompanion;
                if (tick >= geometry.ReadyTick && damage > 0 && body.Boxes[tick].Intersects(geometry.Boxes[tick]))
                {
                    harm.Add(new(body.Actor, damage, body.Life, tick, EstimateStatus.Nominal));
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
        => geometry with { Boxes = Array.AsReadOnly(geometry.Boxes.ToArray()) };
}
