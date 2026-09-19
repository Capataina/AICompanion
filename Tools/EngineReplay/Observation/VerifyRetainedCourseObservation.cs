#nullable enable

extern alias live;

using System;
using Microsoft.Xna.Framework;
using Terraria;
using PlayerIntentRegion = live::AICompanion.Companion.Brain.Infrastructure.Observation.PlayerIntentRegion;
using PlayerIntentRegions = live::AICompanion.Companion.Brain.Infrastructure.Observation.PlayerIntentRegions;
using ObserveForecastErrors = live::AICompanion.Companion.Brain.Infrastructure.Observation.ObserveForecastErrors;
using Receipts = live::AICompanion.Companion.Brain.Infrastructure.Observation.CollectNativeEffectReceipts;
using Attribution = live::AICompanion.Companion.Brain.Infrastructure.Observation.NativeEffectAttribution;
using ObserveCapabilities = live::AICompanion.Companion.Brain.Infrastructure.Observation.ObserveDecisionCapabilities;
using Gear = live::AICompanion.Companion.Inventory.CompanionGear;
using Bag = live::AICompanion.Companion.Inventory.CompanionInventory;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;

/// <summary>Small source-level contracts for the course facts that do not need a live Terraria world.</summary>
internal static class VerifyRetainedCourseObservation
{
    public static int Run()
    {
        int failures = 0;
        void Require(bool condition, string message) { if (!condition) { failures++; Console.Error.WriteLine(message); } }

        var admission = new PlayerIntentRegion(new Vector2(20, 20), new Vector2(10, 10), new Vector2(20, 20), true);
        var local = new PlayerIntentRegion(Vector2.Zero, new Vector2(10, 10), Vector2.Zero, false);
        var regions = new PlayerIntentRegions(admission, local);
        Require(regions.ContainsContinuation(new Vector2(-10, 0)) && regions.ContainsContinuation(new Vector2(30, 20)),
            "continuation must retain either actual region");
        Require(!regions.ContainsContinuation(new Vector2(0, 20)),
            "continuation must not admit a bounding-rectangle corner that neither region contains");
        Require(regions.GapBeyondContinuation(new Vector2(0, 20)) == 10f,
            "continuation gap must be the nearest union member's gap");

        var residuals = new ObserveForecastErrors();
        var key = new ObserveForecastErrors.Key(3, 7, 2, 4);
        residuals.Issue(key, new Vector2(20, 20), 10);
        Require(!residuals.Observe(3, 7, 2, 4, new Vector2(24, 17), 14, new Vector2(-100, -100), new Vector2(100, 100)),
            "ordinary correction must remain in its motion episode");
        var summary = residuals.Get(key);
        Require(summary.Covered && summary.Samples == 1 && summary.Minimum == new Vector2(4, -3),
            "residual coverage must retain the measured generation/model/age error");
        Require(!residuals.Get(new ObserveForecastErrors.Key(3, 7, 2, 5)).Covered,
            "an unissued or evicted age must remain unknown rather than zero error");

        CapabilityFacts(Require);
        MotionFacts(Require);

        Receipts.ResetWorld();
        long firstPre = Receipts.BeginSyntheticStrike(9, 3, 41, Attribution.CompanionProjectile);
        long secondPre = Receipts.BeginSyntheticStrike(9, 3, 41, Attribution.Unknown);
        Require(firstPre == secondPre, "all observer pre-hooks for one dispatch must share one token");
        var firstPost = Receipts.CompleteSyntheticStrike(9, 3, 41, 7, "first-post");
        var laterPost = Receipts.CompleteSyntheticStrike(9, 3, 41, 7, "later-post");
        Require(firstPost.Id == laterPost.Id && firstPost.Amount == 7 && firstPost.Attribution == Attribution.CompanionProjectile,
            "the first post hook must finalise once while later observer views retain its receipt");
        Require(Receipts.TryConsume(firstPost.Id, "experience") && !Receipts.TryConsume(firstPost.Id, "experience")
            && Receipts.TryConsume(firstPost.Id, "weapon-learning"), "each consumer must process a strike once independently");
        long nextPre = Receipts.BeginSyntheticStrike(9, 3, 41, Attribution.CompanionProjectile);
        var nextPost = Receipts.CompleteSyntheticStrike(9, 3, 41, 7, "same-tick-second-strike");
        Require(nextPre == nextPost.Id && nextPost.Id != firstPost.Id,
            "identical same-tick strikes must remain separate physical receipts");
        long outer = Receipts.BeginSyntheticStrike(4, 1, 10, Attribution.Player);
        long nested = Receipts.BeginSyntheticStrike(5, 1, 11, Attribution.Unknown);
        var nestedReceipt = Receipts.CompleteSyntheticStrike(5, 1, 11, 1, "nested-post");
        var outerReceipt = Receipts.CompleteSyntheticStrike(4, 1, 10, 2, "outer-post");
        Require(outer != nested && nestedReceipt.Id == nested && outerReceipt.Id == outer,
            "nested dispatches with their own physical source and target must retain separate tokens");
        long sameOuter = Receipts.BeginSyntheticStrike(7, 2, 19, Attribution.CompanionProjectile);
        long sameNested = Receipts.BeginSyntheticNestedStrike(7, 2, 19, Attribution.CompanionProjectile);
        long sameNestedSecondPre = Receipts.BeginSyntheticStrike(7, 2, 19, Attribution.Unknown);
        var sameNestedReceipt = Receipts.CompleteSyntheticStrike(7, 2, 19, 4, "same-key-nested-post");
        var sameNestedLaterPost = Receipts.CompleteSyntheticStrike(7, 2, 19, 4, "same-key-nested-later-post");
        Receipts.EndSyntheticNestedStrike();
        var sameOuterReceipt = Receipts.CompleteSyntheticStrike(7, 2, 19, 4, "same-key-outer-post");
        Require(sameOuter != sameNested && sameNestedSecondPre == sameNested && sameNestedReceipt.Id == sameNested
            && sameNestedLaterPost.Id == sameNested && sameOuterReceipt.Id == sameOuter,
            "native re-entry with the same target and source must retain a child token rather than merging into its parent");

        Receipts.ResetWorld();
        long parent = Receipts.BeginSyntheticStrike(2, 1, 8, Attribution.Player);
        long child = Receipts.BeginSyntheticNestedStrike(3, 1, 9, Attribution.Unknown);
        var childFirst = Receipts.CompleteSyntheticStrike(3, 1, 9, 1, "child-first");
        Receipts.EndSyntheticNestedStrike();
        var parentLater = Receipts.CompleteSyntheticStrike(2, 1, 8, 1, "parent-later");
        var ordered = Receipts.DrainThroughOrdinal(parentLater.ObservationOrdinal);
        Require(child > parent && childFirst.Id == child && parentLater.Id == parent && ordered.Count == 2
            && ordered[0].Id == child && ordered[1].Id == parent,
            "ordinal draining must retain callback completion order when a later child ID completes before its parent");

        Receipts.ResetWorld();
        for (int i = 0; i < 257; i++) Receipts.RecordEffect("overflow-fixture", i, -1, 1, Attribution.Unknown);
        Require(Receipts.HasGap && Receipts.DirtyObjects.Count > 0,
            "a bounded receipt queue must expose overflow as a dirty reobservation gap");
        Receipts.AcknowledgeGapAfterReobserve();
        Require(!Receipts.HasGap && Receipts.DirtyObjects.Count == 0,
            "only an explicit reobservation acknowledgement clears a receipt gap");

        Receipts.ResetWorld();
        long retainedActive = Receipts.BeginSyntheticStrike(12, 1, 2, Attribution.Player);
        Receipts.MarkBrainBoundary();
        Require(Receipts.BeginSyntheticStrike(12, 1, 2, Attribution.Unknown) == retainedActive,
            "a brain boundary must not retire an active dispatch token");
        Receipts.CompleteSyntheticStrike(12, 1, 2, 1, "retirement-complete");
        Receipts.MarkBrainBoundary();
        long afterRetirement = Receipts.BeginSyntheticStrike(12, 1, 2, Attribution.Player);
        Require(afterRetirement != retainedActive,
            "a later brain boundary retires completed dispatch state without recycling receipt IDs");

        Console.WriteLine($"retained-course observation facts: {(failures == 0 ? "all checks passed" : failures + " failures")}");
        return failures;
    }

    private static void MotionFacts(Action<bool, string> require)
    {
        var player = new live::AICompanion.Companion.Brain.Infrastructure.Observation.PlayerSense();
        var capture = new live::AICompanion.Companion.Brain.Infrastructure.Observation.ObservePlayerMotionEvidence();
        player.Activity.Observe(Vector2.Zero, Vector2.Zero, false, false, 0);
        capture.Observe(player, 0);
        for (int tick = 1; tick <= 8; tick++)
        {
            float x = tick % 2 == 0 ? 0 : 2;
            var velocity = new Vector2(tick % 2 == 0 ? -2 : 2, 0);
            player.Activity.Observe(new Vector2(x, 0), velocity, false, false, (ulong)tick);
            var motion = capture.Observe(player, tick);
            require(motion.Episode == 0 && !motion.Discontinuous,
                "ordinary left/right weaving must not retire the observed motion episode");
            if (tick == 8)
                require(motion.NetDisplacement == 0 && motion.PathLength == 16 && motion.Coherence == 0,
                    "motion facts must retain actual traversed path separately from net movement and discounted intent");
        }
        player.Activity.Observe(new Vector2(1000, 0), Vector2.Zero, false, false, 9);
        var jumped = capture.Observe(player, 9);
        require(jumped.Discontinuous && jumped.Episode == 1 && jumped.PathLength == 0,
            "an untraversed native position correction must invalidate the old motion episode without adding fictional path");
    }

    private static void CapabilityFacts(Action<bool, string> require)
    {
        var gear = new Gear();
        var cargo = new Bag();
        var preferences = new Preferences();
        var observer = new ObserveCapabilities();
        gear.Slots[0].type = 100;
        gear.Slots[0].damage = 12;
        gear.Slots[2].type = 200;
        gear.Slots[2].pick = 35;
        var initial = observer.Observe(41, gear, cargo, preferences);
        var identical = observer.Observe(41, gear, cargo, preferences);
        require(initial.CapabilityRevision == 0 && identical.CapabilityRevision == 0
            && initial.FirstWeapon.Type == 100 && initial.Pickaxe.PickPower == 35 && initial.Cargo.Count == Bag.Slots
            && initial.Policy.PotBreaking,
            "initial immutable gear facts must copy each slot without assigning a revision to identical observation");

        gear.Slots[0].damage = 27;
        var inPlace = observer.Observe(41, gear, cargo, preferences);
        require(inPlace.CapabilityRevision == 1 && inPlace.FirstWeapon.Damage == 27,
            "an in-place handed-item power edit must invalidate capability facts");

        gear.Slots[0].shootSpeed = 9f;
        gear.Slots[0].knockBack = 4f;
        gear.Slots[0].scale = 1.4f;
        var weaponPhysics = observer.Observe(41, gear, cargo, preferences);
        require(weaponPhysics.CapabilityRevision == 2 && weaponPhysics.FirstWeapon.ShootSpeed == 9f
            && weaponPhysics.FirstWeapon.KnockBack == 4f && weaponPhysics.FirstWeapon.Scale == 1.4f,
            "every live ItemWeapon geometry and impulse input must invalidate and be copied");

        gear.Slots[0] = new Item { type = 300, damage = 8 };
        gear.Slots[1] = new Item { type = 100, damage = 12 };
        var swapped = observer.Observe(41, gear, cargo, preferences);
        require(swapped.CapabilityRevision == 3 && swapped.FirstWeapon.Type == 300 && swapped.SecondWeapon.Type == 100,
            "replacing or swapping weapon slots must produce copied current capability facts");

        cargo.Items[0] = new Item { type = 400, stack = 1, maxStack = 99 };
        var capacity = observer.Observe(41, gear, cargo, preferences);
        require(capacity.CapabilityRevision == 4 && capacity.CargoOccupiedSlots == 1 && capacity.CargoEmptySlots == Bag.Slots - 1
            && capacity.Cargo[0].Type == 400 && capacity.Cargo[0].MaxStack == 99,
            "cargo capacity changes must invalidate capability facts and report actual occupancy");

        preferences.PotBreaking = false;
        var policy = observer.Observe(41, gear, cargo, preferences);
        require(policy.PolicyRevision == 1 && policy.CapabilityRevision == capacity.CapabilityRevision,
            "a policy writer must change only the policy revision");
        require(observer.Observe(41, gear, cargo, preferences).PolicyRevision == policy.PolicyRevision,
            "an identical policy observation must not churn its revision");

        preferences.MiningList.SetMarked(17, true);
        var miningPolicy = observer.Observe(41, gear, cargo, preferences);
        require(miningPolicy.PolicyRevision == 2 && miningPolicy.Policy.MarkedOres.Count == 1
            && miningPolicy.Policy.MarkedOres[0] == 17,
            "mining-list permission membership must be copied into the immutable policy fact");

        var nextWorld = observer.Observe(42, gear, cargo, preferences);
        require(nextWorld.WorldEpoch == 42 && nextWorld.CapabilityRevision == 0 && nextWorld.PolicyRevision == 0,
            "a changed world epoch must reset observation identity rather than carrying revisions across worlds");

        var replacementGear = new Gear();
        replacementGear.Slots[0].type = 301;
        var replacedOwner = observer.Observe(42, replacementGear, cargo, preferences);
        require(replacedOwner.CapabilityRevision == 1 && replacedOwner.FirstWeapon.Type == 301,
            "replacing a gear owner at the same source version must invalidate the capability snapshot");
    }
}
