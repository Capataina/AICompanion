#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The accompanying walk does not live on a wall standing inside its box.
///
/// <para>The region is a box around the player and knows nothing about terrain, so a wall, an overhang or a
/// pillar can stand inside it; the tour draws each leg from the far edge of the part open to it, and the far
/// edge can therefore be somewhere no straight walk reaches. The 10:05 capture of 22 September 2026 is that
/// case, and it is the owner's "always too close to the floor… it never stayed up": from tick 1,891 the body's
/// recorded `npc_px` x is frozen at 55733 for more than two hundred ticks while its y slides between 5622 and
/// 5697, at a recorded clearance of 0.1 to 0.6 px, with `control_source=accompany`, the navigator idle and the
/// chosen park seventeen tiles east behind rock. Nothing is straining eastward — `desired_vel` reads −0.08,
/// 0.00, +0.09 across those ticks — so the *target* is what is sliding along the face: every straight step
/// toward a goal drawn beyond the rock is refused, the blocked step is re-aimed along whichever turn is
/// clearest, and beside a vertical face the clearest turn runs up or down it.</para>
///
/// <para><b>The scene is the capture's own terrain rather than a drawing of it</b>, cut by
/// `--extract-scenario` at tick 1,950 and committed as
/// `Tools/Scenarios/extracted-2026-09-22_10-05-56-125-tick-1950.txt`, with the recorded box, the recorded lead
/// and the recorded body position. That matters here more than usual, because the first version of this row
/// drew what the reading described — one wall from ceiling to floor with the box's far third behind it — and
/// the walk handled it perfectly: 101 % coverage of the reachable width and a longest wall-hug of seven ticks.
/// The real geometry is not a wall but a pocket. A five-wide pillar runs from the ceiling down to a chamber
/// roof; west of it the rock closes in overhead into a wedge that opens westward as it descends; the tile
/// directly below the body is solid, the tiles directly east are the pillar, and the only way out is west
/// along two rows and then down and round. A synthetic wall could not have produced it, and no amount of
/// retuning a synthetic wall would have.</para>
///
/// <para>The player is not simulated: the box is held where the capture had it, which is the harder case and
/// the one the capture ran — the player stood still from tick 2,103 to the end and the pin outlived him
/// starting to move again. Hazards and the refusal predicate are both empty, so nothing here is about an enemy
/// or the player's footprint; what is left is the tour against terrain.</para>
/// </summary>
internal static class VerifyAccompanyGetsPastAnObstruction
{
    private const string Scenario = "extracted-2026-09-22_10-05-56-125-tick-1950.txt";
    private const int Ticks = 600;

    /// <summary>The recorded geometry of that tick, read off the capture's own columns: `npc_px`,
    /// `region_player_px` (which is the box's centre despite its name) and `region_comfort` (its half-size).
    /// The lead is derived, because it is not a column: horizontally the box's centre less the player's, and
    /// vertically the same less the standing offset the region puts him at.</summary>
    private static readonly Vector2 Body = new(55733f, 5685f);
    private static readonly Vector2 BoxCentre = new(55826.90f, 5714.27f);
    private static readonly Vector2 HalfSize = new(261.83f, 104.73f);
    private static readonly Vector2 Lead = new(45.45f, -2.91f);

    /// <summary>Within this of a wall the body is hugging it rather than passing it; the orb is twenty across.</summary>
    private const float Hugging = 2f;

    /// <summary>A second of play. The capture's run was two hundred ticks; a leg that grazes a wall on its way
    /// past is not the defect, and a body that lives on the face is.</summary>
    private const int LongestHug = 60;

    public static int Run()
    {
        LimitPlanningWork.Unbounded = true;
        ITileWorld? previous = null;
        try { previous = MovementQueries.World; } catch (InvalidOperationException) { }
        try
        {
            var world = Scene();
            MovementQueries.World = world;
            MovementQueries.Hazards = Array.Empty<Rectangle>();
            FreeSpaceSearch.WorldOverride = world;
            ClearanceField.Shared.Invalidate();

            // The premise, because the whole row is about a body that starts pinned: if the scene does not
            // reproduce the capture's contact it is measuring something else, and a green run would mean nothing.
            float startClearance = CircleContact.Clearance(world, Body);
            if (startClearance > Hugging)
                return Red($"the premise: the scene must start the body against the rock the capture had it against; "
                    + $"clearance at {Body} is {startClearance:0.00} px, and the capture recorded 0.1");

            var movement = new CoordinateMovement();
            Vector2 centre = Body, velocity = Vector2.Zero;
            int hugging = 0, longest = 0, hugStart = -1, worstStart = -1;
            float furthestOff = 0f;
            int freeAt = -1;
            for (int tick = 0; tick < Ticks; tick++)
            {
                Step(world, ref centre, ref velocity,
                    movement.Accompany(new OrbState(centre, velocity), BoxCentre, HalfSize, Lead, _ => false));
                float clearance = CircleContact.Clearance(world, centre);
                furthestOff = MathF.Max(furthestOff, clearance);
                if (freeAt < 0 && clearance > Hugging * 4f) freeAt = tick;
                if (clearance <= Hugging)
                {
                    if (hugging == 0) hugStart = tick;
                    hugging++;
                    if (hugging > longest) { longest = hugging; worstStart = hugStart; }
                }
                else hugging = 0;
            }

            string run = $"longest run within {Hugging:0} px of rock {longest} of {Ticks} ticks from tick {worstStart}; "
                + $"best clearance reached {furthestOff:0.0} px, first got {Hugging * 4f:0} px clear at tick {freeAt}; "
                + $"ended at {centre.X:0},{centre.Y:0} against a start of {Body.X:0},{Body.Y:0}";

            if (longest > LongestHug)
                return Red($"the accompanying walk must not hold the body against rock inside its box: {run}. "
                    + "That is the 10:05 capture's own signature — a leg is drawn from the far edge of the box, the far "
                    + "edge is behind the pillar, every straight step toward it is refused, and the blocked step is "
                    + "re-aimed along the clearest turn, which beside a vertical face runs up and down it.");
            Console.WriteLine($"accompanying past an obstruction: {run}");
            return 0;
        }
        finally
        {
            LimitPlanningWork.Unbounded = false;
            FreeSpaceSearch.WorldOverride = null;
            if (previous != null) MovementQueries.World = previous;
            ClearanceField.Shared.Invalidate();
        }
    }

    private static TextTileWorld Scene()
    {
        string path = FindScenario(Scenario);
        var lines = new List<string>(File.ReadAllLines(path));
        return TextTileWorld.Parse(lines, out _, out _);
    }

    /// <summary>The committed scenario folder, found by walking up from wherever the tool was started, because it
    /// runs from the repository root under the verify script and from its own folder by hand.</summary>
    private static string FindScenario(string name)
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, "Tools", "Scenarios", name);
                if (File.Exists(candidate)) return candidate;
            }
        throw new FileNotFoundException($"the committed scenario {name} was not found under any Tools/Scenarios above {Environment.CurrentDirectory}");
    }

    private static void Step(ITileWorld world, ref Vector2 centre, ref Vector2 velocity, Controls controls)
    {
        velocity = OrbPace.Step(velocity, controls.Desired, controls.Burst);
        centre += velocity;
        CircleContact.Resolve(world, ref centre, ref velocity);
    }

    private static int Red(string message)
    {
        Console.WriteLine("RED: " + message);
        return 1;
    }
}
