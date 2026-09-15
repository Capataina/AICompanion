#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Selection;

/// <summary>
/// Puts the jobs that are close in worth into the order that gets the most done soonest, and lets the first job of the
/// best order lead the comparison. The owner's example of 15 September 2026 is the reason it exists: a slime on the way
/// to a dark corner should be killed, its drops collected and then the corner lit, and a chooser that valued each job
/// only from where the body stood flew past the slime to the corner because the corner scored higher on its own.
///
/// <para>An order is scored by the worth each job delivers and when. Every job's worth is multiplied by
/// window / (window + the tick it finishes), the same time term <see cref="EvaluatePreparedActivities"/> charges a
/// single job, so a one-job order scores exactly what that job scores alone. The first job finishes at its own task
/// time, which carries the activity's own trip estimate; each later job adds the straight flight from where the
/// previous job happened and its own work. Every order does the same work and differs only in its flying and in when
/// the value arrives, which is why the score is worth delivered early rather than the least total time — the least
/// total time would be indifferent between killing the slime first and last.</para>
///
/// <para>Three things this deliberately does not do. It never lifts a job above the best single job's score: the first
/// step of the best order takes the best single score among the ordered jobs and the rest are placed below it in
/// proportion to their own best order, so ordering decides which of several close jobs runs, never whether a job
/// beats keeping company or protecting the player. It keeps nothing between comparisons: only the first step runs and
/// the order is worked out again at the next rescore. And it adds no switching margin of its own: flying toward the
/// first job shortens its time and lengthens every other job's, so the next comparison favours it, and the running
/// job's commitment is already inside its worth, so two orders that were always about even do not flip on noise.
/// Travel between jobs is a straight line over the body's pace, so an order through terrain is scored a little
/// optimistically.</para>
/// </summary>
public static class OrderNearbyTasks
{
    public readonly record struct Result(EvaluatedActivity[] Evaluated, string Order, string RunnerUp);

    /// <summary>A job somewhere, as opposed to being with or protecting the player.</summary>
    public static bool IsTask(in PreparedActivity candidate)
        => candidate.HasTarget && !candidate.IsFollowing && !candidate.ServesPlayerDirectly;

    public static Result Apply(EvaluatedActivity[] evaluated, IReadOnlyList<PreparedActivity> prepared, Vector2 body,
        float speed, float window, float share, int maximum)
    {
        if (!(window > 0f) || !(speed > 0f) || maximum < 2) return new(evaluated, "", "");
        var tasks = new List<int>();
        float best = 0f;
        for (int i = 0; i < evaluated.Length; i++)
        {
            EvaluatedActivity e = evaluated[i];
            if (Find(prepared, e.Index) is not { } p) continue;
            if (e.Error.Length != 0 || !(e.Final > 0f) || !(e.Time > 0f) || !IsTask(p) || p.Site is null) continue;
            tasks.Add(i);
            best = MathF.Max(best, e.Final);
        }
        tasks.RemoveAll(i => evaluated[i].Final < best * (1f - share));
        if (tasks.Count < 2) return new(evaluated, "", "");
        tasks.Sort((a, b) => evaluated[a].Final != evaluated[b].Final
            ? evaluated[b].Final.CompareTo(evaluated[a].Final)
            : evaluated[a].Index.CompareTo(evaluated[b].Index));
        if (tasks.Count > maximum) tasks.RemoveRange(maximum, tasks.Count - maximum);

        int n = tasks.Count;
        var worth = new float[n];
        var site = new Vector2[n];
        var alone = new float[n];
        var work = new float[n];
        for (int k = 0; k < n; k++)
        {
            EvaluatedActivity e = evaluated[tasks[k]];
            PreparedActivity p = Find(prepared, e.Index)!.Value;
            worth[k] = e.Final / e.Time;
            site[k] = p.Site!.Value;
            alone[k] = MathF.Max(p.ForecastTicks, p.TaskTicks);
            work[k] = MathF.Max(0f, alone[k] - Vector2.Distance(body, site[k]) / speed);
        }

        var bestStart = new float[n];
        var order = new int[n];
        var bestOrder = new int[n];
        var runnerOrder = new int[n];
        var used = new bool[n];
        float bestValue = -1f, runnerValue = -1f;

        void Visit(int depth, float ticks, Vector2 at, float value)
        {
            if (depth == n)
            {
                int lead = order[0];
                bestStart[lead] = MathF.Max(bestStart[lead], value);
                if (value > bestValue)
                {
                    if (bestValue >= 0f && bestOrder[0] != lead) { runnerValue = bestValue; Array.Copy(bestOrder, runnerOrder, n); }
                    bestValue = value;
                    Array.Copy(order, bestOrder, n);
                }
                else if (lead != bestOrder[0] && value > runnerValue)
                {
                    runnerValue = value;
                    Array.Copy(order, runnerOrder, n);
                }
                return;
            }
            for (int k = 0; k < n; k++)
            {
                if (used[k]) continue;
                float finish = depth == 0 ? alone[k] : ticks + Vector2.Distance(at, site[k]) / speed + work[k];
                used[k] = true;
                order[depth] = k;
                Visit(depth + 1, finish, site[k], value + worth[k] * window / (window + finish));
                used[k] = false;
            }
        }
        Visit(0, 0f, body, 0f);

        int leader = bestOrder[0];
        float top = evaluated[tasks[0]].Final;
        var result = (EvaluatedActivity[])evaluated.Clone();
        for (int k = 0; k < n; k++)
        {
            int at = tasks[k];
            float placed = k == leader
                ? top
                : MathF.Min(evaluated[at].Final, top * (bestStart[k] / bestStart[leader]) * (bestStart[k] >= bestStart[leader] ? 0.999f : 1f));
            result[at] = result[at] with { Final = placed };
        }
        return new(result, Describe(evaluated, tasks, bestOrder, bestValue),
            runnerValue >= 0f ? Describe(evaluated, tasks, runnerOrder, runnerValue) : "");
    }

    private static PreparedActivity? Find(IReadOnlyList<PreparedActivity> prepared, int index)
    {
        if (index >= 0 && index < prepared.Count && prepared[index].Index == index) return prepared[index];
        foreach (PreparedActivity p in prepared)
            if (p.Index == index) return p;
        return null;
    }

    private static string Describe(EvaluatedActivity[] evaluated, List<int> tasks, int[] order, float value)
    {
        var text = new StringBuilder();
        foreach (int k in order)
        {
            if (text.Length > 0) text.Append('>');
            text.Append(evaluated[tasks[k]].Name);
        }
        return text.Append('=').Append(value.ToString("0.000", CultureInfo.InvariantCulture)).ToString();
    }
}
