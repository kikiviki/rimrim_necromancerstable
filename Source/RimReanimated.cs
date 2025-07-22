using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace RimReanimated
{
    [StaticConstructorOnStartup]
    public static class ModInit
    {
        static ModInit()
        {
            Log.Message("RimReanimated mod initializing...");
            
            // Verify our recipe worker is registered
            var recipe = DefDatabase<RecipeDef>.GetNamed("CobbleTogetherRevenant", false);
            if (recipe != null)
            {
                Log.Message("Found CobbleTogetherRevenant recipe, workerClass: " + recipe.workerClass?.Name);
            }
            else
            {
                Log.Error("CobbleTogetherRevenant recipe not found!");
            }
        }
    }

    public class RimReanimatedMod : Mod
    {
        public RimReanimatedMod(ModContentPack content) : base(content)
        {
            Log.Message("RimReanimated mod loaded");
        }
    }

    public class Hediff_UndeadDecay : HediffWithComps
    {
        private int ticksBetweenDecay = 2500; // Check every 1 hour
        private float decayPerInterval = 0.01f;
        private float initialFreshness = 1f;

        public override void PostMake()
        {
            base.PostMake();
            Severity = 0f;
            Log.Message($"UndeadDecay PostMake: severity set to {Severity}");
        }
        
        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            Log.Message($"UndeadDecay PostAdd: severity is {Severity}");
        }

        public void SetDecayRate()
        {
            float daysToDecay = 30f;
            float totalTicksToDecay = daysToDecay * 60000f;
            int totalIntervals = (int)(totalTicksToDecay / ticksBetweenDecay);
            
            if (totalIntervals > 0)
            {
                decayPerInterval = 1f / totalIntervals;
            }
            else
            {
                decayPerInterval = 0.01f;
            }
            
            Severity = 0f;
            Log.Message($"Decay rate set: {decayPerInterval:F6} per interval, {totalIntervals} total intervals over {daysToDecay} days");
        }

        public override void Tick()
        {
            base.Tick();
            
            if (pawn == null || pawn.Dead) return;
            
            if (pawn.IsHashIntervalTick(ticksBetweenDecay))
            {
                Severity += decayPerInterval;
                
                if (Find.TickManager.TicksGame % 15000 == 0)
                {
                    Log.Message($"UndeadDecay tick: {pawn.Name.ToStringShort} severity now {Severity:0.00}");
                }
                
                if (Severity >= 1f)
                {
                    if (pawn.Dead == false)
                    {
                        var corpseRotPenaltyDef = DefDatabase<HediffDef>.GetNamed("CorpseRotPenalty", false);
                        if (corpseRotPenaltyDef != null)
                        {
                            pawn.health.AddHediff(corpseRotPenaltyDef);
                        }
                        
                        pawn.Kill(null);
                        Messages.Message("Undead " + pawn.Name.ToStringShort + " has crumbled to dust.", MessageTypeDefOf.NegativeEvent);
                    }
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksBetweenDecay, "ticksBetweenDecay", 2500);
            Scribe_Values.Look(ref decayPerInterval, "decayPerInterval", 0.01f);
            Scribe_Values.Look(ref initialFreshness, "initialFreshness", 1f);
        }
        
        public override bool ShouldRemove => false;
        public override bool Visible => Severity > 0.2f;
    }

    public class Hediff_Undead : HediffWithComps
    {
        private float corpseRotProgress = 0f;

        public void SetCorpseRotProgress(float rotProgress)
        {
            corpseRotProgress = rotProgress;
        }

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            
            // Set all needs to 90% and freeze them
            if (pawn.needs != null)
            {
                if (pawn.needs.food != null)
                    pawn.needs.food.CurLevel = pawn.needs.food.MaxLevel * 0.9f;
                if (pawn.needs.rest != null)
                    pawn.needs.rest.CurLevel = pawn.needs.rest.MaxLevel * 0.9f;
                if (pawn.needs.joy != null)
                    pawn.needs.joy.CurLevel = pawn.needs.joy.MaxLevel * 0.9f;
                if (pawn.needs.mood != null)
                    pawn.needs.mood.CurLevel = pawn.needs.mood.MaxLevel * 0.9f;
            }

            // Set work types
            if (pawn.workSettings != null)
            {
                pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefs)
                {
                    if (pawn.WorkTypeIsDisabled(workType)) continue;
        
                    if (workType.defName == "Research" || 
                        workType.defName == "Art" || 
                        workType.defName == "PatientBedRest" ||
                        workType.defName == "Patient" ||
                        (workType.relevantSkills != null && workType.relevantSkills.Any(s => s.defName == "Intellectual")))
                    {
                        pawn.workSettings.Disable(workType);
                    }
                    else
                    {
                        pawn.workSettings.SetPriority(workType, 3);
                    }
                }
            }

            if (pawn.playerSettings != null)
            {
                pawn.playerSettings.medCare = MedicalCareCategory.NoCare;
            }

            ApplyRotScalingDebuffs();
        }

        private void ApplyRotScalingDebuffs()
        {
            float penaltySeverity = 0f; 
            
            if (corpseRotProgress >= 0.98f)
                penaltySeverity = 0.08f;
            else if (corpseRotProgress >= 0.85f)
                penaltySeverity = 0.04f;
            else if (corpseRotProgress >= 0.65f)
                penaltySeverity = 0.01f;

            if (penaltySeverity > 0f)
            {
                var rotPenaltyDef = DefDatabase<HediffDef>.GetNamed("UndeadRotPenalty", false);
                if (rotPenaltyDef != null)
                {
                    var rotPenaltyHediff = HediffMaker.MakeHediff(rotPenaltyDef, pawn);
                    rotPenaltyHediff.Severity = penaltySeverity;
                    pawn.health.AddHediff(rotPenaltyHediff);
                    Log.Message($"Applied corpse rot penalty: {penaltySeverity:0.00} severity");
                }
            }
        }

        public override void Tick()
        {
            base.Tick();
            
            // Safety check - don't tick if pawn is dead, dying, or destroyed
            if (pawn == null || pawn.Dead || pawn.Destroyed) return;
            
            // Keep needs frozen - check every 6 hours
            if (pawn.IsHashIntervalTick(15000)) 
            {
                try
                {
                    if (pawn.needs != null)
                    {
                        if (pawn.needs.food != null)
                            pawn.needs.food.CurLevel = pawn.needs.food.MaxLevel * 0.9f;
                        if (pawn.needs.rest != null)
                            pawn.needs.rest.CurLevel = pawn.needs.rest.MaxLevel * 0.9f;
                        if (pawn.needs.joy != null)
                            pawn.needs.joy.CurLevel = pawn.needs.joy.MaxLevel * 0.9f;
                        if (pawn.needs.mood != null)
                            pawn.needs.mood.CurLevel = pawn.needs.mood.MaxLevel * 0.9f;
                    }
                    
                    RemoveIncompatibleHediffs();
                }
                catch (Exception e)
                {
                    Log.Warning($"Error in undead needs/hediff management: {e.Message}");
                }
            }

            // Clean up undead death thoughts - check once per hour
            if (pawn.IsHashIntervalTick(60000))
            {
                CleanupUndeadDeathThoughts();
            }
        }
        
        private void RemoveIncompatibleHediffs()
        {
            try
            {
                if (pawn?.health?.hediffSet?.hediffs == null) return;
                
                var hediffsToRemove = new List<Hediff>();
                
                foreach (var hediff in pawn.health.hediffSet.hediffs.ToList()) // ToList() for safety
                {
                    if (ShouldBeImmuneToHediff(hediff))
                    {
                        hediffsToRemove.Add(hediff);
                    }
                }
                
                foreach (var hediff in hediffsToRemove)
                {
                    try
                    {
                        pawn.health.RemoveHediff(hediff);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"Failed to remove hediff {hediff.def.defName}: {e.Message}");
                    }
                }

                // Ensure undead cannot have mood breaks - force mood to stay high if it's getting low
                if (pawn.needs?.mood != null && pawn.needs.mood.CurLevel < 0.5f)
                {
                    pawn.needs.mood.CurLevel = 0.9f;
                }
            }
            catch (Exception e)
            {
                Log.Error($"Error in RemoveIncompatibleHediffs: {e.Message}");
            }
        }
        
        private bool ShouldBeImmuneToHediff(Hediff hediff)
        {
            try
            {
                if (hediff?.def?.defName == null) return false;
                
                var immuneToDefNames = new HashSet<string>
                {
                    "FoodPoisoning", "ToxicBuildup", "Hypothermia", "Heatstroke",
                    "Malnutrition", "DrugOverdose", "Flu", "Plague", "WoundInfection",
                    "GutWorms", "MuscleParasites", "FibrousMechanites", "SensoryMechanites"
                };
                
                // Remove any bleeding hediffs - undead don't bleed
                bool isBleedingHediff = hediff.def.defName.Contains("Bleeding") || 
                                       (hediff.Bleeding && hediff.def.lethalSeverity > 0);
                
                return immuneToDefNames.Contains(hediff.def.defName) || 
                       hediff.def.makesSickThought ||
                       isBleedingHediff ||
                       (hediff.def.tendable && hediff.def.defName.Contains("Infection"));
            }
            catch (Exception e)
            {
                Log.Warning($"Error checking hediff immunity: {e.Message}");
                return false;
            }
        }

        private void CleanupUndeadDeathThoughts()
        {
            try
            {
                // Safety checks - don't run if pawn is dead/dying or references are invalid
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                if (pawn.Map == null || pawn.Map.mapPawns == null) return;
                
                var colonists = pawn.Map.mapPawns.FreeColonists;
                if (colonists == null) return;
                
                foreach (var colonist in colonists.ToList()) // ToList() to avoid collection modification issues
                {
                    if (colonist?.needs?.mood?.thoughts?.memories == null) continue;
                    if (colonist.Dead || colonist.Destroyed) continue;

                    var memories = colonist.needs.mood.thoughts.memories;
                    if (memories.Memories == null) continue;
                    
                    var thoughtsToRemove = new List<Thought_Memory>();
                    
                    foreach (var memory in memories.Memories.ToList()) // ToList() for safety
                    {
                        if (IsUndeadRelatedDeathThought(memory))
                        {
                            thoughtsToRemove.Add(memory);
                        }
                    }
                    
                    foreach (var thought in thoughtsToRemove)
                    {
                        try
                        {
                            memories.RemoveMemory(thought);
                            Log.Message($"Removed undead death thought '{thought.def.defName}' from {colonist.Name.ToStringShort}");
                        }
                        catch (Exception e)
                        {
                            Log.Warning($"Failed to remove thought: {e.Message}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"Error in CleanupUndeadDeathThoughts: {e.Message}");
            }
        }
        
        private bool IsUndeadRelatedDeathThought(Thought_Memory thought)
        {
            try
            {
                if (thought?.def?.defName == null) return false;
                
                var deathThoughtNames = new HashSet<string>
                {
                    "ColonistDied", "ColonistDiedFriend", "ColonistDiedRival",
                    "ColonistDiedFamily", "ColonistDiedSpouse", "ColonistDiedLover", 
                    "ColonistDiedChild", "ColonistDiedParent", "ColonistDiedSibling",
                    "WitnessedDeathAlly", "WitnessedDeathNonAlly", "KnowColonistDied",
                    "ObservedLayingCorpse", "ObservedLayingRottingCorpse"
                };
                
                if (!deathThoughtNames.Contains(thought.def.defName)) return false;
                
                // Check if the thought is about someone who was/is undead
                if (thought.otherPawn != null && !thought.otherPawn.Destroyed)
                {
                    // Check if they're currently undead
                    var undeadTraitsDef = DefDatabase<HediffDef>.GetNamedSilentFail("UndeadTraits");
                    if (undeadTraitsDef != null)
                    {
                        var undeadHediff = thought.otherPawn.health?.hediffSet?.GetFirstHediffOfDef(undeadTraitsDef);
                        if (undeadHediff != null) return true;
                    }
                    
                    // Check if they were previously undead (have corpse rot penalty)
                    var rotPenaltyDef = DefDatabase<HediffDef>.GetNamedSilentFail("CorpseRotPenalty");
                    if (rotPenaltyDef != null)
                    {
                        var rotPenalty = thought.otherPawn.health?.hediffSet?.GetFirstHediffOfDef(rotPenaltyDef);
                        if (rotPenalty != null) return true;
                    }
                }
                
                return false;
            }
            catch (Exception e)
            {
                Log.Warning($"Error checking undead death thought: {e.Message}");
                return false;
            }
        }
    }

    public class Hediff_CorpseRotPenalty : HediffWithComps
    {
        public float rotPenalty = 0.2f;

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref rotPenalty, "rotPenalty", 0.2f);
        }
    }
    
    public static class NecromancyUtility
    {
        public static Pawn CreateUndeadFromCorpse(Corpse corpse, Map map, IntVec3 loc)
        {
            try
            {
                Log.Message("Starting undead creation from corpse: " + corpse?.InnerPawn?.Name?.ToStringShort);

                if (corpse == null || corpse.InnerPawn == null || map == null)
                {
                    Log.Error("Invalid parameters for undead creation");
                    return null;
                }

                var innerPawn = corpse.InnerPawn;
                var pawnKind = innerPawn.kindDef;
                var faction = Faction.OfPlayer;

                if (pawnKind == null || faction == null)
                {
                    Log.Error("Missing pawn kind or faction");
                    return null;
                }

                var request = new PawnGenerationRequest(pawnKind, faction, PawnGenerationContext.NonPlayer)
                {
                    FixedGender = innerPawn.gender,
                    ForceGenerateNewPawn = true,
                    CanGeneratePawnRelations = false,
                    ColonistRelationChanceFactor = 0f,
                    AllowDead = false,
                    AllowDowned = false
                };

                var newPawn = PawnGenerator.GeneratePawn(request);
                if (newPawn == null)
                {
                    Log.Error("PawnGenerator.GeneratePawn returned null");
                    return null;
                }

                // Copy basic info and appearance
                var originalName = innerPawn.Name?.ToStringShort ?? "Revenant";
                newPawn.Name = new NameTriple("Undead", originalName, "");

                if (newPawn.ageTracker != null && innerPawn.ageTracker != null)
                {
                    newPawn.ageTracker.AgeBiologicalTicks = innerPawn.ageTracker.AgeBiologicalTicks;
                    newPawn.ageTracker.AgeChronologicalTicks = innerPawn.ageTracker.AgeChronologicalTicks;
                }

                // Copy appearance
                if (newPawn.story != null && innerPawn.story != null)
                {
                    newPawn.story.hairDef = innerPawn.story.hairDef;
                    try { newPawn.story.HairColor = innerPawn.story.HairColor; } catch { }
                    newPawn.story.headType = innerPawn.story.headType;
                    newPawn.story.bodyType = innerPawn.story.bodyType;

                    if (innerPawn.story.skinColorOverride != null)
                    {
                        var originalSkin = innerPawn.story.skinColorOverride.Value;
                        newPawn.story.skinColorOverride = new Color(
                            originalSkin.r * 0.8f + 0.1f,
                            originalSkin.g * 0.8f + 0.1f,
                            originalSkin.b * 0.9f + 0.2f
                        );
                    }
                    else
                    {
                        newPawn.story.skinColorOverride = new Color(0.7f, 0.7f, 0.85f);
                    }
                }

                // Copy style details
                if (newPawn.style != null && innerPawn.style != null)
                {
                    if (innerPawn.style.beardDef != null)
                        newPawn.style.beardDef = innerPawn.style.beardDef;
                    
                    try 
                    { 
                        if (innerPawn.style.FaceTattoo != null)
                            newPawn.style.FaceTattoo = innerPawn.style.FaceTattoo;
                    } catch { }
                    
                    try 
                    { 
                        if (innerPawn.style.BodyTattoo != null)
                            newPawn.style.BodyTattoo = innerPawn.style.BodyTattoo;
                    } catch { }
                }

                // Copy skills
                if (newPawn.skills != null && innerPawn.skills != null)
                {
                    foreach (var skillDef in DefDatabase<SkillDef>.AllDefs)
                    {
                        var sourceSkill = innerPawn.skills.GetSkill(skillDef);
                        var targetSkill = newPawn.skills.GetSkill(skillDef);
                        if (sourceSkill != null && targetSkill != null)
                        {
                            targetSkill.Level = sourceSkill.Level;
                            targetSkill.passion = sourceSkill.passion;
                        }
                    }
                }

                // Clear mood memories
                if (newPawn.needs?.mood?.thoughts?.memories != null)
                {
                    newPawn.needs.mood.thoughts.memories.Memories.Clear();
                }

                // Set to primary colony ideology
                try
                {
                    if (ModsConfig.IdeologyActive)
                    {
                        var playerFaction = Faction.OfPlayer;
                        if (playerFaction?.ideos?.PrimaryIdeo != null)
                        {
                            if (newPawn.ideo != null)
                            {
                                newPawn.ideo.SetIdeo(playerFaction.ideos.PrimaryIdeo);
                                Log.Message($"Set undead ideology to: {playerFaction.ideos.PrimaryIdeo.name}");
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Warning($"Failed to set undead ideology: {e.Message}");
                }

                // Calculate rot progress
                float rotProgress = 0f;
                float additionalRot = 0f;

                var rotPenaltyDef = DefDatabase<HediffDef>.GetNamed("CorpseRotPenalty", false);
                if (rotPenaltyDef != null)
                {
                    var rotPenaltyHediff = innerPawn.health.hediffSet.GetFirstHediffOfDef(rotPenaltyDef);
                    if (rotPenaltyHediff != null && rotPenaltyHediff is Hediff_CorpseRotPenalty penalty)
                    {
                        additionalRot = penalty.rotPenalty;
                    }
                }

                if (corpse.GetRotStage() == RotStage.Dessicated)
                {
                    rotProgress = 1f;
                }
                else
                {
                    var rottable = corpse.GetComp<CompRottable>();
                    if (rottable != null)
                    {
                        float rawRot = rottable.RotProgress;
                        rotProgress = (float)Math.Pow(rawRot, 1.5f);
                    }
                }

                rotProgress = Math.Min(1f, rotProgress + additionalRot);

                // Add undead hediffs
                var undeadTraitsDef = DefDatabase<HediffDef>.GetNamed("UndeadTraits", false);
                if (undeadTraitsDef != null)
                {
                    var undeadHediff = HediffMaker.MakeHediff(undeadTraitsDef, newPawn) as Hediff_Undead;
                    if (undeadHediff != null)
                    {
                        undeadHediff.SetCorpseRotProgress(rotProgress);
                        newPawn.health.AddHediff(undeadHediff);
                    }
                }

                var decayDef = DefDatabase<HediffDef>.GetNamed("UndeadDecay", false);
                if (decayDef != null)
                {
                    var decayHediff = HediffMaker.MakeHediff(decayDef, newPawn);
                    if (decayHediff is Hediff_UndeadDecay customDecay)
                    {
                        customDecay.SetDecayRate();
                    }
                    else
                    {
                        decayHediff.Severity = 0f;
                    }
                    newPawn.health.AddHediff(decayHediff);
                }

                MakeUndeadImmune(newPawn);

                // Find spawn location
                IntVec3 spawnLoc = loc;
                if (!spawnLoc.Standable(map))
                {
                    bool foundSpot = false;
                    for (int i = 0; i < 9; i++)
                    {
                        IntVec3 testLoc = loc + GenAdj.AdjacentCellsAndInside[i];
                        if (testLoc.InBounds(map) && testLoc.Standable(map))
                        {
                            spawnLoc = testLoc;
                            foundSpot = true;
                            break;
                        }
                    }

                    if (!foundSpot)
                    {
                        Log.Error("No valid spawn location for undead");
                        return null;
                    }
                }

                GenSpawn.Spawn(newPawn, spawnLoc, map);

                if (newPawn.Drawer?.renderer != null)
                {
                    newPawn.Drawer.renderer.SetAllGraphicsDirty();
                }

                corpse.Destroy();
                return newPawn;
            }
            catch (Exception e)
            {
                Log.Error("Exception in CreateUndeadFromCorpse: " + e.Message + "\n" + e.StackTrace);
                return null;
            }
        }

        private static void MakeUndeadImmune(Pawn pawn)
        {
            var hediffsToRemove = new List<Hediff>();

            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff.def.defName == "FoodPoisoning" ||
                    hediff.def.defName == "ToxicBuildup" ||
                    hediff.def.defName == "Hypothermia" ||
                    hediff.def.defName == "Heatstroke" ||
                    hediff.def.defName == "Malnutrition" ||
                    hediff.def.defName == "DrugOverdose" ||
                    hediff.def.makesSickThought ||
                    hediff.def.tendable)
                {
                    hediffsToRemove.Add(hediff);
                }
            }

            foreach (var hediff in hediffsToRemove)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }
    }

    public class Recipe_CobbleRevenant : RecipeWorker
    {
        private static Corpse pendingCorpse = null;
        
        public override void ConsumeIngredient(Thing ingredient, RecipeDef recipe, Map map)
        {
            if (ingredient is Corpse corpse)
            {
                pendingCorpse = corpse;
                return;
            }
            
            base.ConsumeIngredient(ingredient, recipe, map);
        }

        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            var corpse = pendingCorpse;
            
            if (corpse != null && corpse.Map != null && !corpse.Destroyed)
            {
                var map = corpse.Map;
                var loc = billDoer.Position;
                string corpseName = corpse.InnerPawn.Name.ToStringShort;
                
                try
                {
                    var undead = NecromancyUtility.CreateUndeadFromCorpse(corpse, map, loc);
                    
                    if (undead != null)
                    {
                        Messages.Message("Revenant created from " + corpseName + "!", 
                            undead, MessageTypeDefOf.PositiveEvent);
                        
                        if (!corpse.Destroyed)
                        {
                            base.ConsumeIngredient(corpse, recipe, map);
                        }
                        
                        pendingCorpse = null;
                        return;
                    }
                }
                catch (Exception e)
                {
                    Log.Error("Error creating undead: " + e.Message);
                }
            }
            
            // Cleanup on failure
            if (pendingCorpse != null && !pendingCorpse.Destroyed)
            {
                try
                {
                    base.ConsumeIngredient(pendingCorpse, recipe, pendingCorpse.Map);
                }
                catch (Exception e)
                {
                    Log.Warning("Error consuming corpse during cleanup: " + e.Message);
                }
                pendingCorpse = null;
            }
            else if (pendingCorpse != null)
            {
                pendingCorpse = null;
            }
        }

        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            if (thing is Corpse corpse)
            {
                var pawn = corpse.InnerPawn;
                
                if (!pawn.RaceProps.Humanlike) return false;
                if (pawn.RaceProps.IsMechanoid) return false;
                if (pawn.RaceProps.Animal) return false;
                
                if (pawn.def.defName.Contains("Mechanoid") || 
                    pawn.def.defName.Contains("Entity") || 
                    pawn.def.defName.Contains("Drone") ||
                    pawn.def.defName.Contains("Anomaly"))
                {
                    return false;
                }
                
                float rotProgress = 0f;
                var rottable = corpse.GetComp<CompRottable>();
                if (rottable != null)
                {
                    rotProgress = rottable.RotProgress;
                }
                
                var rotPenaltyDef = DefDatabase<HediffDef>.GetNamed("CorpseRotPenalty", false);
                if (rotPenaltyDef != null)
                {
                    var rotPenaltyHediff = pawn.health.hediffSet.GetFirstHediffOfDef(rotPenaltyDef);
                    if (rotPenaltyHediff != null && rotPenaltyHediff is Hediff_CorpseRotPenalty penalty)
                    {
                        rotProgress += penalty.rotPenalty;
                    }
                }
                
                float freshness = 1f - rotProgress;
                return freshness >= 0.15f;
            }
            
            return base.AvailableOnNow(thing, part);
        }
    }
}