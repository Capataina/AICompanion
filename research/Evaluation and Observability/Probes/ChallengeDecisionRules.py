"""Small deterministic counterexamples; not Terraria simulation or a benchmark.

Run from the repository root:
python3 'research/Evaluation and Observability/Probes/ChallengeDecisionRules.py'
The exhaustive route probe uses a finite, static, one-dimensional unit-speed world,
identical mandatory output sets, exact costs and one-tick interactions. No statement
about noisy forecasts, moving players or native execution follows from its pass.
"""
from itertools import permutations, combinations
from random import Random
import json


def remaining_cost(position, order, sites):
    cost = 0
    for job in order:
        cost += abs(position - sites[job]) + 1
        position = sites[job]
    return cost


def complete_static_scene(sites):
    position = 0
    pending = tuple(range(len(sites)))
    incumbent = pending
    switches = 0
    completed = 0
    ticks = 0
    while pending:
        candidate = min(permutations(pending), key=lambda p: remaining_cost(position, p, sites))
        if remaining_cost(position, candidate, sites) < remaining_cost(position, incumbent, sites):
            if ticks and candidate[0] != incumbent[0]:
                switches += 1
            incumbent = candidate
        target = sites[incumbent[0]]
        if position == target:
            finished = incumbent[0]
            pending = tuple(j for j in pending if j != finished)
            incumbent = tuple(j for j in incumbent if j != finished)
            completed += 1
        else:
            position += 1 if target > position else -1
        ticks += 1
        assert ticks < 500, (sites, incumbent, position)
    return completed, switches, ticks


def main():
    results = {}
    # Current position already accounts for the past journey.
    future = {'finish': 8, 'switch': 5}
    erroneous = {'finish': 8, 'switch': 5 + 20}
    assert min(future, key=future.get) == 'switch'
    assert min(erroneous, key=erroneous.get) == 'finish'
    results['sunk_cost_reverses_correct_choice'] = {'future': future, 'with_past_added': erroneous}

    # A prefix with worse immediate results can reach a different, better state.
    prefixes = {'direct_hit': (5, 5), 'shove_into_line': (0, 20)}
    prefix_winner = max(prefixes, key=lambda x: prefixes[x][0])
    final_winner = max(prefixes, key=lambda x: prefixes[x][1])
    assert prefix_winner != final_winner
    results['prefix_pruning_discards_enabling_action'] = {'prefix_winner': prefix_winner, 'final_winner': final_winner}

    # Fixed reward double-counts overlapping illuminated cells.
    coverage = {'A': {1, 2, 3, 4}, 'B': {1, 2, 3, 4}, 'C': {5, 6, 7}}
    pairs = list(combinations(coverage, 2))
    nominal = max(pairs, key=lambda p: sum(len(coverage[j]) for j in p))
    marginal = max(pairs, key=lambda p: len(set.union(*(coverage[j] for j in p))))
    assert nominal == ('A', 'B') and marginal == ('A', 'C')
    results['coverage_must_be_marginal'] = {'fixed_rewards_choose': nominal, 'actual_unique_cells': 4,
                                          'effect_model_chooses': marginal, 'actual_unique_cells_better': 7}

    # A rolling horizon shorter than indivisible work never sees its completion.
    horizon = 3
    long_duration, long_reward = 4, 10
    short_duration, short_reward = 1, 1
    visible_long = long_reward if long_duration <= horizon else 0
    visible_short = (horizon // short_duration) * short_reward
    assert visible_short > visible_long and long_reward > long_duration * short_reward
    results['short_horizon_can_starve_long_work'] = {'horizon': horizon, 'visible_long': visible_long,
                                                  'visible_short': visible_short, 'four_tick_long_reward': 10,
                                                  'four_tick_short_reward': 4}

    # Price a feasible seed before optional candidate generation exhausts budget.
    allowance = 4
    proposal_cost, price_cost = 4, 1
    generator_first_has_answer = allowance - proposal_cost >= price_cost
    seed_first_has_answer = allowance >= price_cost
    assert not generator_first_has_answer and seed_first_has_answer
    results['generation_can_consume_first_answer_budget'] = {'generator_first_answer': generator_first_has_answer,
                                                           'seed_first_answer': seed_first_has_answer,
                                                           'units': 'abstract operations, not milliseconds'}

    # Incidental work is not free merely because its duration is shorter than travel.
    direct_return, detour_travel, interaction = 10, 8, 4
    assert interaction < direct_return and direct_return + detour_travel + interaction > direct_return
    results['short_insert_still_delays_return'] = {'direct': 10, 'inserted': 22}

    rng = Random(20260919)
    scenes = [rng.sample(range(-20, 21), 5) for _ in range(100)]
    observed = [complete_static_scene(s) for s in scenes]
    assert all(done == 5 and switches == 0 for done, switches, _ in observed)
    results['same_output_future_comparison_static_scenes'] = {
        'scenes': len(scenes), 'completed_jobs': sum(x[0] for x in observed),
        'mid_job_reversals': sum(x[1] for x in observed), 'maximum_ticks': max(x[2] for x in observed),
        'scope': 'exact static finite 1D model; no game integration or runtime claim'}
    print(json.dumps(results, indent=2))


if __name__ == '__main__':
    main()
