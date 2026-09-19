"""Analytical checks for the proposed objective; not a production-brain test.

Run from the repository root:
    python3 'research/Evaluation and Observability/Probes/CheckCourseObjectiveContracts.py'

The finite-horizon counterexample is intentionally retained beside the selected
discounted policy. A passing row proves arithmetic on these inputs, not native
prediction, correct policy calibration or player acceptance.
"""

from math import exp, isclose


def value(effects, harms=(), gap_intervals=(), scale=100.0):
    reward = sum(worth * exp(-tick / scale) for tick, worth in effects)
    damage = sum(fraction * exp(-tick / scale) for tick, fraction in harms)
    gap = sum(amount * (exp(-start / scale) - exp(-end / scale))
              for start, end, amount in gap_intervals)
    return reward - damage - gap


def check(name, condition):
    if not condition:
        raise AssertionError(name)
    print(f"PASS {name}")


def main():
    horizon = 100
    old_delivered = min(100, horizon)
    old_omitted = horizon
    check("rejected endpoint objective ties completed with omitted", old_delivered == old_omitted)
    check("finite lone effect beats omission at equal costs", value([(100, 1)]) > value([]))
    check("earlier equal effects dominate later effects", value([(10, 1), (30, 1)]) > value([(20, 1), (40, 1)]))
    # Discounted timing deliberately differs from linear completion-time sums.
    check("explicit timing trade-off is not linear delay", value([(0, 1), (100, 1)]) > value([(40, 1), (40, 1)]))
    effects = [(30, 0.4), (100, 0.6)]
    harms = [(50, 0.2)]
    gaps = [(25, 80, 0.3)]
    elapsed = 20
    rebased = value([(t-elapsed, w) for t, w in effects],
                    [(t-elapsed, h) for t, h in harms],
                    [(a-elapsed, b-elapsed, g) for a, b, g in gaps])
    check("all future terms rebase by one positive factor",
          isclose(rebased, exp(elapsed/100) * value(effects, harms, gaps), rel_tol=1e-12))
    census_quantity = 20
    whole = value([(10, 20/census_quantity)])
    split = value([(10, 10/census_quantity), (10, 10/census_quantity)])
    check("same-time stack partition conserves worth", isclose(whole, split))
    ordinary_damage = value([], [(10, 0.2)])
    extra_known_jobs = [(40, 1), (70, 1)]
    incremental_damage = value(extra_known_jobs, [(10, 0.2)]) - value(extra_known_jobs)
    check("unrelated jobs do not rescale a harm event", isclose(ordinary_damage, incremental_damage))
    one_tick_delay = value([(10, 1)]) - value([(11, 1)])
    immediate_harm = 0.2
    check("one tick of waiting is not one life fraction", 0 < one_tick_delay < immediate_harm)
    print("8 analytical contracts passed; native implementation and behavioural fit remain untested.")


if __name__ == "__main__":
    main()
