#nullable enable
using System; using System.Collections.Generic; using System.Globalization; using System.Linq; using System.Text.Json;
using Microsoft.Xna.Framework; using Terraria; using Terraria.ModLoader;
using AICompanion.Companion.Brain.Activities.Combat.Planning;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;
using AICompanion.Companion.Inventory; using AICompanion.Companion.PlayerIntegration;
namespace AICompanion.Companion.Brain.Activities.Combat;

public static class CombatCourseFacts
{
    public const string Domain = "combat", Method = "combat-fire";
    public sealed record Target(int Slot,int Generation,int Type,int Life,int LifeMax,float X,float Y,float VelocityX,float VelocityY);
    public sealed record Weapon(int Slot,int ItemType,int Prefix,int UseTime,int Mana,int Damage,int Shoot,float ShootSpeed,int UseStyle);
    public sealed record Use(string Id,int PlanId,int Segment,int Index,int TargetSlot,int TargetGeneration,int WeaponSlot,int WeaponItemType,int WeaponPrefix,float StandX,float StandY,float AimX,float AimY,float LaunchX,float LaunchY,int FireTick,int UseTicks,float ExpectedTargetDamage,int TargetImpactTicks=-1);
    public static FactKey TargetKey(int slot,int generation)=>new("combat-target",slot.ToString(CultureInfo.InvariantCulture),generation);
    public static FactKey WeaponKey(int slot)=>new("combat-weapon",slot.ToString(CultureInfo.InvariantCulture));
    public static FactKey ManaCapacityKey()=>new("capacity","combat-mana");
    public static FactKey UseKey(string id)=>new("combat-use",id);
    public static string ToolId(int slot,int item,int prefix)=>$"weapon:{slot}:{item}:{prefix}";
    /// <summary>
    /// A use is identified by the shot it is, not by the search that found it.
    ///
    /// This was `plan:{plan}/segment:{segment}/use:{index}`, and the plan number comes from
    /// `combat.NextPlanId++`, so every attack search minted brand-new keys for the same physical shots.
    /// Once the course brain owned the tick and combat re-searched every frame, a course that bound a
    /// shot was invalidated on the very next tick by construction: `ValidateNextUse` looked up the
    /// accepted id, found nothing, and answered `accepted-use-not-present`. The course was released, a
    /// fresh decision started, and because a decision spans ticks that decision was released again
    /// before it could settle — so the companion took combat for five ticks of thirty on a scene with a
    /// damageable hostile in reach and kept company for the rest, with the funnel reading
    /// `combat:5/5 ... decision=deciding orders=0/0`.
    ///
    /// The identity is now target, weapon, stand and the index within that stand's sequence, which is
    /// what makes the same shot re-found next tick the same use. The stand is rounded to whole pixels
    /// because two searches agreeing about a place should not disagree by a fraction of one. Two plans
    /// proposing the same shot now collide on one key deliberately, and the freshest capture wins, which
    /// is the answer you want: they describe one opportunity. What a collision cannot hide is a shot
    /// that genuinely changed, because `ValidateNextUse` still compares the stand pose and the tool and
    /// answers `accepted-use-changed` when either moved.
    /// </summary>
    public static string UseId(int targetSlot,int targetGeneration,int weaponSlot,float standX,float standY,int index)
        => FormattableString.Invariant($"npc:{targetSlot}.{targetGeneration}/weapon:{weaponSlot}/stand:{MathF.Round(standX)},{MathF.Round(standY)}/use:{index}");
    /// <summary>The same identity derived from a live plan, so the capture that publishes a use, the
    /// activity that finds it again in its plan and the firing path that verifies it before pulling the
    /// trigger all build the key one way. Three copies of this derivation is how the capture and the
    /// firing path come to disagree about which shot a course accepted.</summary>
    public static string UseId(AttackPlan plan,int segment,int index)
    {
        AttackSegment held=plan.Segments[segment]; PlannedUse planned=held.Uses[index];
        NPC target=(uint)planned.TargetSlot<(uint)Main.maxNPCs?Main.npc[planned.TargetSlot]:null;
        int generation=target!=null?HostileAttackSources.Generation(target):0;
        return UseId(planned.TargetSlot,generation,planned.WeaponSlot,held.Stand.Stand.X,held.Stand.Stand.Y,index);
    }

    public static string OpportunityTarget(int slot)=>$"npc:{slot}";
    public static IReadOnlyList<DecisionFact> Capture(in ActionContext ctx,CompanionCombat combat,SearchAttackPlans.SearchResult? search)
    {
        var facts=new Dictionary<FactKey,DecisionFact>();
        foreach(ThreatRecord threat in ctx.Senses.Threats.Threats) {
            NPC npc=threat.Npc; if(npc==null||!npc.active||npc.life<=0) continue;
            int generation=HostileAttackSources.Generation(npc); FactKey key=TargetKey(npc.whoAmI,generation);
            facts[key]=new(key,generation,new FactValue(npc.life,npc.Center.X,npc.Center.Y,JsonSerializer.Serialize(new Target(npc.whoAmI,generation,npc.type,npc.life,npc.lifeMax,npc.Center.X,npc.Center.Y,npc.velocity.X,npc.velocity.Y))),FactEvidence.Observed);
        }
        FactKey manaKey=ManaCapacityKey(); float mana=ctx.Companion.Mana.Current;
        facts[manaKey]=new(manaKey,BitConverter.SingleToInt32Bits(mana),new FactValue(mana),FactEvidence.Observed);
        CompanionGear gear=ctx.Player.GetModPlayer<CompanionPlayer>().Gear; IReadOnlyList<CompanionWeapon> weapons=combat.Weapons;
        for(int slot=0;slot<weapons.Count;slot++) { Item item=gear[(GearSlot)slot]; CompanionWeapon w=weapons[slot]; var value=new Weapon(slot,w.ItemType,item.prefix,w.UseTime,w.ManaCost,item.damage,item.shoot,item.shootSpeed,item.useStyle); FactKey key=WeaponKey(slot); facts[key]=new(key,Version(value),new FactValue(Text:JsonSerializer.Serialize(value)),FactEvidence.Observed); }
        foreach(AttackPlan plan in search?.Front??Array.Empty<AttackPlan>()) CapturePlan(plan,weapons,gear,facts);
        return facts.Values.OrderBy(f=>f.Key).ToArray();
    }
    private static void CapturePlan(AttackPlan plan,IReadOnlyList<CompanionWeapon> weapons,CompanionGear gear,Dictionary<FactKey,DecisionFact> facts) {
        for(int s=0;s<plan.Segments.Length;s++) for(int i=0;i<plan.Segments[s].Uses.Length;i++) {
            AttackSegment segment=plan.Segments[s]; PlannedUse planned=segment.Uses[i];
            if((uint)planned.WeaponSlot>=(uint)weapons.Count||(uint)planned.TargetSlot>=(uint)Main.maxNPCs) continue;
            NPC target=Main.npc[planned.TargetSlot]; if(target==null||!target.active||target.life<=0) continue;
            int generation=HostileAttackSources.Generation(target); Item item=gear[(GearSlot)planned.WeaponSlot];
            string id=UseId(plan,s,i);
            var use=new Use(id,plan.Id,s,i,planned.TargetSlot,generation,planned.WeaponSlot,weapons[planned.WeaponSlot].ItemType,item.prefix,segment.Stand.Stand.X,segment.Stand.Stand.Y,planned.AimPoint.X,planned.AimPoint.Y,planned.LaunchDirection.X,planned.LaunchDirection.Y,planned.FireTick,weapons[planned.WeaponSlot].UseTime,planned.ExpectedTargetDamage,planned.TargetImpactTicks);
            FactKey key=UseKey(id); facts[key]=new(key,Version(use),new FactValue(Text:JsonSerializer.Serialize(use)),FactEvidence.Observed);
        }
    }
    internal static T? Read<T>(DecisionFact f)=>Read<T>(f.Value);
    internal static T? Read<T>(FactValue v) { if(string.IsNullOrWhiteSpace(v.Text)) return default; try{return JsonSerializer.Deserialize<T>(v.Text);}catch(JsonException){return default;} }
    private static long Version(Weapon w)=>Mix(w.Slot,w.ItemType,w.Prefix,w.UseTime,w.Mana,w.Damage,w.Shoot,BitConverter.SingleToInt32Bits(w.ShootSpeed),w.UseStyle);
    // The version carries no plan or segment number, for the same reason the key carries none: those
    // rise with every search, so an unchanged shot would publish a changed version every tick and dirty
    // every dependency manifest that had read it — a stable key with a moving version is the same defect
    // one level down, and it would be harder to see because the fact would still be present.
    private static long Version(Use u)=>Mix(u.Index,u.TargetSlot,u.TargetGeneration,u.WeaponSlot,u.WeaponItemType,u.WeaponPrefix,u.FireTick,u.UseTicks,BitConverter.SingleToInt32Bits(u.ExpectedTargetDamage),u.TargetImpactTicks);
    private static long Mix(params int[] values) { long h=1469598103934665603L; foreach(int v in values){h^=(uint)v;h*=1099511628211L;} return h; }
}

public sealed class CombatOpportunitySource:IOpportunitySource
{
    public string Name=>CombatCourseFacts.Domain;
    public OpportunitySlice Continue(DecisionFactSnapshot facts,DecisionWorkCursor cursor,DecisionWorkBudget budget) {
        cursor.Bind(facts.WorldEpoch,"combat-world-epoch"); DecisionFact[] uses=facts.Facts.Where(f=>f.Key.Kind=="combat-use").OrderBy(f=>f.Key).ToArray();
        var examined=new List<Opportunity>();
        while(cursor.Offset<uses.Length) { if(!budget.TrySpend("combat-opportunity-census")) break; int index=(int)cursor.Offset; DecisionFact useFact=uses[cursor.Offset];cursor.Advance(); var use=CombatCourseFacts.Read<CombatCourseFacts.Use>(useFact); if(use==null||uses.Take(index).Select(CombatCourseFacts.Read<CombatCourseFacts.Use>).Any(previous=>previous?.TargetSlot==use.TargetSlot&&previous.TargetGeneration==use.TargetGeneration)) continue;
            FactKey targetKey=CombatCourseFacts.TargetKey(use.TargetSlot,use.TargetGeneration); bool known=facts.TryRead(targetKey,out DecisionFact target)&&target.Evidence==FactEvidence.Observed;
            var need=new UsefulNeed(new NeedKey(NeedKind.HostileLife,use.TargetSlot.ToString(CultureInfo.InvariantCulture),use.TargetGeneration),known?Math.Max(0,target.Value.Amount):0,known?Math.Max(1,target.Value.Amount):1,1);
            examined.Add(new Opportunity(new OpportunityKey(Name,"fire",CombatCourseFacts.OpportunityTarget(use.TargetSlot),use.TargetGeneration),known?target.Version:use.TargetGeneration,new CoursePoint(use.StandX,use.StandY),known?OpportunityAdmission.KnownUsable:OpportunityAdmission.Unresolved,known?"captured-target-front":"target-capture-missing",new[]{need},new[]{CombatCourseFacts.Method},DependencyManifest.Empty,targetKey));
        }
        if(cursor.Offset>=uses.Length)cursor.Complete(); return new(examined,new(Name,facts.WorldEpoch,cursor.Offset,uses.Length,cursor.Exhausted,budget.Cut,"captured-combat-use-front"));
    }
}

public sealed class CombatOpportunityBinder:IOpportunityBinder
{
    public string Domain=>CombatCourseFacts.Domain;
    public BindingResult Bind(Opportunity opportunity,ProjectedCourseState state,TrackedFactReader facts,DecisionWorkCursor cursor,DecisionWorkBudget budget) {
        if(!TryTarget(opportunity.Key,out int slot,out int generation))return new(null,OpportunityAdmission.KnownUnusable,"opportunity-identity-invalid",false);
        FactKey targetKey=CombatCourseFacts.TargetKey(slot,generation);
        if(state.HasUnresolvedChange(targetKey)) return new(null,OpportunityAdmission.Unresolved,"combat-target-successor-unresolved",false);
        DecisionFact observed=facts.Read(targetKey); var target=CombatCourseFacts.Read<CombatCourseFacts.Target>(state.Read(targetKey,facts));
        if(target==null||observed.Evidence!=FactEvidence.Observed)return new(null,OpportunityAdmission.Unresolved,"target-capture-missing",false);
        if(target.Generation!=generation||target.Life<=0)return new(null,OpportunityAdmission.KnownUnusable,"captured-target-changed",false);
        DecisionFact[] uses=facts.Facts.Where(f=>f.Key.Kind=="combat-use").OrderBy(f=>f.Key).ToArray();
        cursor.Bind(facts.SnapshotId ^ opportunity.Key.GetHashCode(), "combat-bind-use-census");
        while(cursor.Offset<uses.Length) {
            if(!budget.TrySpend("combat-opportunity-bind-use")) return new(null,OpportunityAdmission.Unresolved,"budget-cut",true);
            DecisionFact raw=uses[cursor.Offset];
            var planned=CombatCourseFacts.Read<CombatCourseFacts.Use>(facts.Read(raw.Key));
            if(planned==null||planned.TargetSlot!=slot||planned.TargetGeneration!=generation
                ||!float.IsFinite(planned.ExpectedTargetDamage)||planned.ExpectedTargetDamage<=0||planned.TargetImpactTicks<=0) { cursor.Advance(); continue; }
            var capturedTravel=ReadCourseTravel.Read(state,new CoursePoint(planned.StandX,planned.StandY),facts);
            if(capturedTravel==null) {
                var request=new CourseTravelRequest(state.Pose,state.Velocity,new(planned.StandX,planned.StandY));
                if(facts.Read(request.Key).Evidence==FactEvidence.Missing)
                    return new(null,OpportunityAdmission.Unresolved,"combat-travel-pending",true,new[]{request});
            }
            cursor.Advance();
            if(capturedTravel==null||capturedTravel.Admission!=OpportunityAdmission.KnownUsable) continue;
            return BindUse(opportunity,state,facts,targetKey,target,planned,capturedTravel,slot,generation);
        }
        cursor.Complete();
        return new(null,OpportunityAdmission.Unresolved,"no-use-with-captured-travel-and-target-impact",false);
    }

    private static BindingResult BindUse(Opportunity opportunity, ProjectedCourseState state, TrackedFactReader facts,
        FactKey targetKey, CombatCourseFacts.Target target, CombatCourseFacts.Use use, CapturedCourseTravel capturedTravel,
        int slot, int generation)
    {
        if(capturedTravel.ArrivalPose is not {} arrivalPose)return new(null,OpportunityAdmission.Unresolved,"combat-arrival-pose-unresolved",false);
        UseInputs(use,facts,out DecisionFact weaponFact,out DecisionFact manaFact,out CombatCourseFacts.Weapon? weapon);
        if(weapon==null||weaponFact.Evidence!=FactEvidence.Observed||manaFact.Evidence!=FactEvidence.Observed)return new(null,OpportunityAdmission.Unresolved,"combat-input-capture-missing",false);
        if(weapon.ItemType!=use.WeaponItemType||weapon.Prefix!=use.WeaponPrefix)return new(null,OpportunityAdmission.KnownUnusable,"captured-tool-changed",false);
        // Mana never refuses a cast.  ItemWeapon reads the gradient before Spend floors the
        // pool, then the body's delayed regeneration owns the later refill.  It is therefore
        // neither a capacity reservation nor an admission gate here.
        double travel=capturedTravel.Ticks;
        var fight=FightAhead(facts,state,slot,generation,target,travel);
        // The body and the hand are occupied until the last shot this step's effect claims lands, not
        // until the first one is fired. A phase that ends after one use lets the order search schedule a
        // second step inside a window the fight already owns, and it charged the companionship gap for
        // one use against a credit spanning the whole fight — the asymmetry `2e187a7` named and moved
        // only half of. The debit is the side that moves: the occupancy is real.
        double useTicks=Math.Max(Math.Max(1,use.UseTicks),fight.LatestTick-(state.Tick+travel));
        double impact=fight.NominalTick,damage=fight.Damage;
        var after=new FactValue(Math.Max(0,target.Life-damage),target.X,target.Y,JsonSerializer.Serialize(target with {Life=(int)Math.Max(0,Math.Floor(target.Life-damage))}));
        var dependencies=facts.Manifest(); var parents=state.ReadEffects;
        var effect=new PredictedEffect(CourseIdentity.Next(),new NeedKey(NeedKind.HostileLife,slot.ToString(CultureInfo.InvariantCulture),generation),damage,impact,fight.EarliestTick,fight.LatestTick,EstimateStatus.Nominal,parents,new[]{new EffectDelta(targetKey,after)},dependencies);
        var binding=new StepBinding(CourseIdentity.Next(),opportunity.Key,CombatCourseFacts.Method,new CoursePoint(use.StandX,use.StandY),CombatCourseFacts.ToolId(use.WeaponSlot,use.WeaponItemType,use.WeaponPrefix),facts.SnapshotId,facts.WorldEpoch,travel,useTicks,0,new[]{new ResourcePhase(CourseResource.Body,state.Tick,state.Tick+travel+useTicks,1),new ResourcePhase(CourseResource.Hand,state.Tick+travel,state.Tick+travel+useTicks,1)},new[]{effect},parents,dependencies,true,capturedTravel.ArrivalVelocity,use.Id,arrivalPose);
        return new(binding,OpportunityAdmission.KnownUsable,use.Id,false);
    }
    /// <summary>
    /// What taking this fight is forecast to do to the target, which is every use the captured front
    /// holds against it rather than only the next one.
    ///
    /// **A course is charged the whole excursion and must be credited the whole of it.** Until
    /// 22 September 2026 the effect was one use's damage, while the companionship gap the same order
    /// paid ran to the reunion at the end of the fight — so a fight was priced as one arrow against a
    /// six-second trip, and the empty course beat it. Measured on the play of 0.38.13 through the world
    /// run: a 45-life zombie 363 px away, six uses in the front totalling 48 damage, and the order priced
    /// at useful 0.0083 against gap 0.0203, total −0.0120, for 513 consecutive ticks with a bow in hand
    /// and nothing refused. One arrow of six is not what binding that step does.
    ///
    /// Each use is timed by its own place in the plan — the travel to the stand, plus how long after the
    /// first shot the plan fires it, plus that shot's own flight — because the plan's fire ticks are
    /// absolute and its later stands are reached during the fight rather than before it. The amount is
    /// summed and the total capped at the observed life, so a front that over-kills claims a kill and no
    /// more; <see cref="CourseComparisonEpisode"/> caps it a second time against the need's remaining
    /// amount, which is the authority when two orders both claim the same body.
    ///
    /// The nominal tick is the damage-weighted mean impact rather than the first or the last, and the
    /// interval carries the two: the first impact is the earliest this damage begins and the last is when
    /// it is complete. A first-tick nominal would price a long fight as if it were instant, and a
    /// last-tick nominal would discount the opening shot as heavily as the closing one. The evidence
    /// stays <see cref="EstimateStatus.Nominal"/>, so this claims no justified bounds and the order it
    /// belongs to still reports <c>effect-uncertain</c>; over-claiming a fight that is abandoned after
    /// one shot is what the effect ledger's receipts and the forecast-error observer exist to catch.
    /// </summary>
    private static (double Damage,double NominalTick,double EarliestTick,double LatestTick) FightAhead(
        TrackedFactReader facts,ProjectedCourseState state,int slot,int generation,
        CombatCourseFacts.Target target,double travel)
    {
        // **Every use this claim is computed from goes through `Read`, so the manifest the binding
        // carries names all of them.** It used to deserialise straight out of `facts.Facts`, which is the
        // raw snapshot, so an effect claiming three uses declared a dependency on the one the `Bind` loop
        // happened to read — and `DependencyManifest.Changed`, `RetainCourse`, `BindCourseOrder` and
        // `RepairCourse.Invalidate` all decide whether a retained course is still priced on facts the
        // world still holds by consulting exactly that manifest. A change confined to the second or third
        // use reached none of them. It stayed quiet only because the co-read `combat-target` fact carries
        // the hostile's live centre and so dirties the manifest whenever the target moves, which is luck;
        // `UseId` deliberately excludes the plan and segment numbers, so a fresh search reproduces the
        // first use's key exactly while replacing everything after it.
        //
        // The target filter reads the *key* rather than the value, because `UseId` puts the target slot
        // and generation in the identity — so the uses belonging to other targets are never consumed and
        // never enter this manifest, and the declaration stays complete without becoming a subscription
        // to every shot in the snapshot.
        string mine=FormattableString.Invariant($"npc:{slot}.{generation}/");
        var front=facts.Facts.Where(f=>f.Key.Kind=="combat-use"
                &&f.Key.Identity.StartsWith(mine,StringComparison.Ordinal))
            .OrderBy(f=>f.Key).Select(f=>CombatCourseFacts.Read<CombatCourseFacts.Use>(facts.Read(f.Key)))
            .Where(u=>u!=null&&u.TargetSlot==slot&&u.TargetGeneration==generation
                &&float.IsFinite(u.ExpectedTargetDamage)&&u.ExpectedTargetDamage>0&&u.TargetImpactTicks>0)
            .OrderBy(u=>u!.FireTick).ToArray();
        if(front.Length==0) return (0,state.Tick+travel,state.Tick+travel,state.Tick+travel);
        int opening=front[0]!.FireTick;
        double remaining=target.Life,total=0,weighted=0,earliest=double.MaxValue,latest=0;
        foreach(var shot in front)
        {
            if(remaining<=0) break;
            double share=Math.Min(remaining,shot!.ExpectedTargetDamage);
            remaining-=share; total+=share;
            double at=state.Tick+travel+Math.Max(0,shot.FireTick-opening)+shot.TargetImpactTicks;
            weighted+=share*at; earliest=Math.Min(earliest,at); latest=Math.Max(latest,at);
        }
        if(total<=0) return (0,state.Tick+travel,state.Tick+travel,state.Tick+travel);
        return (total,weighted/total,earliest,latest);
    }

    public BindingValidation ValidateNextUse(StepBinding binding,DecisionFactSnapshot facts) {
        if(binding.Method!=CombatCourseFacts.Method||!TryTarget(binding.Opportunity,out int slot,out int generation))return new(OpportunityAdmission.KnownUnusable,"method-or-target-changed",true);
        var reader=facts.Track();
        DecisionFact targetFact=reader.Read(CombatCourseFacts.TargetKey(slot,generation));
        var target=CombatCourseFacts.Read<CombatCourseFacts.Target>(targetFact);
        if(targetFact.Evidence!=FactEvidence.Observed||target==null)return new(OpportunityAdmission.Unresolved,"target-capture-missing",true);
        if(target.Generation!=generation||target.Life<=0)return new(OpportunityAdmission.KnownUnusable,"accepted-target-changed",true);
        if(string.IsNullOrEmpty(binding.NativeUseId))return new(OpportunityAdmission.KnownUnusable,"accepted-use-id-missing",true);
        DecisionFact useFact=reader.Read(CombatCourseFacts.UseKey(binding.NativeUseId));
        var use=CombatCourseFacts.Read<CombatCourseFacts.Use>(useFact);
        if(useFact.Evidence!=FactEvidence.Observed||use==null)return new(OpportunityAdmission.KnownUnusable,"accepted-use-not-present",true);
        if(use.TargetSlot!=slot||use.TargetGeneration!=generation||binding.Pose!=new CoursePoint(use.StandX,use.StandY)
            ||binding.Tool!=CombatCourseFacts.ToolId(use.WeaponSlot,use.WeaponItemType,use.WeaponPrefix))
            return new(OpportunityAdmission.KnownUnusable,"accepted-use-changed",true);
        DecisionFact weaponFact=reader.Read(CombatCourseFacts.WeaponKey(use.WeaponSlot));
        var weapon=CombatCourseFacts.Read<CombatCourseFacts.Weapon>(weaponFact);
        return weaponFact.Evidence==FactEvidence.Observed&&weapon?.ItemType==use.WeaponItemType&&weapon.Prefix==use.WeaponPrefix
            ? new(OpportunityAdmission.KnownUsable,"accepted-use-still-captured",false)
            : new(OpportunityAdmission.KnownUnusable,"accepted-tool-changed",true);
    }
    private static void UseInputs(CombatCourseFacts.Use use,TrackedFactReader facts,out DecisionFact weapon,out DecisionFact mana,out CombatCourseFacts.Weapon? value){weapon=facts.Read(CombatCourseFacts.WeaponKey(use.WeaponSlot));mana=facts.Read(CombatCourseFacts.ManaCapacityKey());value=CombatCourseFacts.Read<CombatCourseFacts.Weapon>(weapon);}
    private readonly record struct Candidate(CombatCourseFacts.Use Use,CapturedCourseTravel Travel){public int CompareTo(Candidate other){int r=-Use.ExpectedTargetDamage.CompareTo(other.Use.ExpectedTargetDamage);if(r==0)r=Travel.Ticks.CompareTo(other.Travel.Ticks);if(r==0)r=Use.TargetImpactTicks.CompareTo(other.Use.TargetImpactTicks);return r==0?StringComparer.Ordinal.Compare(Use.Id,other.Use.Id):r;}}
    private static bool TryTarget(OpportunityKey key,out int slot,out int generation){generation=checked((int)key.Generation);slot=-1;const string p="npc:";return key.Domain==CombatCourseFacts.Domain&&key.Purpose=="fire"&&key.Target.StartsWith(p,StringComparison.Ordinal)&&int.TryParse(key.Target[p.Length..],NumberStyles.Integer,CultureInfo.InvariantCulture,out slot)&&slot>=0;}
}
