using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace NecromancersTable
{
    [StaticConstructorOnStartup]
    public static class ModInit
    {
        static ModInit()
        {
            Log.Message("NecromancersTable mod initializing...");
            
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

    public class NecromancersTableMod : Mod
    {
        public NecromancersTableMod(ModContentPack content) : base(content)
        {
        }
    }

    public class Hediff_UndeadDecay : HediffWithComps
    {
        private int ticksBetweenDecay = 2500; // Check every 1 hour (reasonable for decay progression)
        private float decayPerInterval = 0.01f; // How much decay per interval
        private float initialFreshness = 1f; // Store the initial freshness

        public override void PostMake()
        {
            base.PostMake();
            // Ensure the hediff starts with 0 severity
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
            // Fixed decay: 30 days (adjust as desired)
            float daysToDecay = 30f;
            
            // CORRECT RimWorld time calculation:
            // 1 day = 60,000 ticks (not the massive number I was using before!)
            float totalTicksToDecay = daysToDecay * 60000f;
            int totalIntervals = (int)(totalTicksToDecay / ticksBetweenDecay);
            
            if (totalIntervals > 0)
            {
                decayPerInterval = 1f / totalIntervals;
            }
            else
            {
                decayPerInterval = 0.01f; // Fallback
            }
            
            // IMPORTANT: Ensure decay starts at 0
            Severity = 0f;
            
            Log.Message($"CORRECTED Decay rate set: {decayPerInterval:F6} per interval, {totalIntervals} total intervals over {daysToDecay} days, initial severity: {Severity}");
        }

        public override void Tick()
        {
            base.Tick();
            
            if (pawn == null || pawn.Dead)
            {
                return; // Don't tick if pawn is invalid or dead
            }
            
            // Performance: Decay every hour (2,500 ticks) - reasonable for progression
            if (pawn.IsHashIntervalTick(ticksBetweenDecay))
            {
                Severity += decayPerInterval;
                
                // Performance: Only log every 6 in-game hours (15,000 ticks) to reduce spam
                if (Find.TickManager.TicksGame % 15000 == 0)
                {
                    Log.Message($"UndeadDecay tick: {pawn.Name.ToStringShort} severity now {Severity:0.00}");
                }
                
                if (Severity >= 1f)
                {
                    if (pawn.Dead == false)
                    {
                        // Apply freshness penalty to corpse
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
        
        public override bool ShouldRemove => false; // Prevent automatic removal
        
        public override bool Visible => Severity > 0.2f; // Only show when there's noticeable decay
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
                {
                    pawn.needs.food.CurLevel = pawn.needs.food.MaxLevel * 0.9f;
                }
                if (pawn.needs.rest != null)
                {
                    pawn.needs.rest.CurLevel = pawn.needs.rest.MaxLevel * 0.9f;
                }
                if (pawn.needs.joy != null)
                {
                    pawn.needs.joy.CurLevel = pawn.needs.joy.MaxLevel * 0.9f;
                }
                if (pawn.needs.mood != null)
                {
                    pawn.needs.mood.CurLevel = pawn.needs.mood.MaxLevel * 0.9f;
                }
            }

            // Set work types - allow all except intellectual/research/art
            if (pawn.workSettings != null)
            {
                pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefs)
                {
                    if (pawn.WorkTypeIsDisabled(workType))
                    {
                        continue; // Skip work types not allowed for this pawn
                    }
        
                    // Disable intellectual, research, and art work types
                    if (workType.defName == "Research" || 
                        workType.defName == "Art" || 
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

            // Apply scaling debuffs based on corpse rot
            ApplyRotScalingDebuffs();
        }

        private void ApplyRotScalingDebuffs()
        {
            // EXTREMELY minimal penalties - necromancy should work great even on very old corpses
            float penaltySeverity = 0f; 
            
            if (corpseRotProgress >= 0.98f)
            {
                penaltySeverity = 0.08f; // Completely dessicated = "minor corpse rot penalty" (-5%)
            }
            else if (corpseRotProgress >= 0.85f)
            {
                penaltySeverity = 0.04f; // Very rotted = "slight corpse rot penalty" (-3%)
            }
            else if (corpseRotProgress >= 0.65f)
            {
                penaltySeverity = 0.01f; // Moderately rotted = "minor decay traces" (-1%)
            }
            // corpseRotProgress < 0.65f = no penalty (most corpses)

            // Only add penalty if there actually is one
            if (penaltySeverity > 0f)
            {
                var rotPenaltyDef = DefDatabase<HediffDef>.GetNamed("UndeadRotPenalty", false);
                if (rotPenaltyDef != null)
                {
                    var rotPenaltyHediff = HediffMaker.MakeHediff(rotPenaltyDef, pawn);
                    rotPenaltyHediff.Severity = penaltySeverity;
                    pawn.health.AddHediff(rotPenaltyHediff);
                    
                    // Check what stage it actually shows
                    var stage = rotPenaltyHediff.CurStage;
                    var stageLabel = stage?.label ?? "unknown";
                    
                    Log.Message($"Applied corpse rot penalty: {penaltySeverity:0.00} severity (rot progress: {corpseRotProgress:0.00}) -> Stage: '{stageLabel}'");
                    
                    // Double-check the severity after adding to pawn
                    var addedHediff = pawn.health.hediffSet.GetFirstHediffOfDef(rotPenaltyDef);
                    if (addedHediff != null)
                    {
                        Log.Message($"Rot penalty hediff on pawn: actual severity {addedHediff.Severity:0.00}, displayed severity: {addedHediff.SeverityLabel}");
                    }
                }
            }
            else
            {
                Log.Message($"No corpse rot penalty applied - corpse was fresh enough (rot progress: {corpseRotProgress:0.00})");
            }
        }

        public override void Tick()
        {
            base.Tick();
            
            // Performance: Keep needs frozen at 90% - check every 6 hours (15,000 ticks)
            // This is much more performance-friendly than checking every 150 ticks
            if (pawn.IsHashIntervalTick(15000)) 
            {
                if (pawn.needs != null)
                {
                    if (pawn.needs.food != null)
                    {
                        pawn.needs.food.CurLevel = pawn.needs.food.MaxLevel * 0.9f;
                    }
                    if (pawn.needs.rest != null)
                    {
                        pawn.needs.rest.CurLevel = pawn.needs.rest.MaxLevel * 0.9f;
                    }
                    if (pawn.needs.joy != null)
                    {
                        pawn.needs.joy.CurLevel = pawn.needs.joy.MaxLevel * 0.9f;
                    }
                    if (pawn.needs.mood != null)
                    {
                        pawn.needs.mood.CurLevel = pawn.needs.mood.MaxLevel * 0.9f;
                    }
                }
                
                // Performance: Remove harmful hediffs every 6 hours instead of every 150 ticks
                RemoveIncompatibleHediffs();
            }
        }
        
        private void RemoveIncompatibleHediffs()
        {
            var hediffsToRemove = new List<Hediff>();
            
            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                // Check if this is a hediff that undead should be immune to
                if (ShouldBeImmuneToHediff(hediff))
                {
                    hediffsToRemove.Add(hediff);
                }
            }
            
            foreach (var hediff in hediffsToRemove)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }
        
        private bool ShouldBeImmuneToHediff(Hediff hediff)
        {
            // List of conditions undead should be immune to
            var immuneToDefNames = new HashSet<string>
            {
                "FoodPoisoning", "ToxicBuildup", "Hypothermia", "Heatstroke",
                "Malnutrition", "DrugOverdose", "Flu", "Plague", "WoundInfection",
                "GutWorms", "MuscleParasites", "FibrousMechanites", "SensoryMechanites"
            };
            
            return immuneToDefNames.Contains(hediff.def.defName) || 
                   hediff.def.makesSickThought ||
                   (hediff.def.tendable && hediff.def.defName.Contains("Infection"));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref corpseRotProgress, "corpseRotProgress", 0f);
        }
    }

    // Hediff to track corpse reuse penalty
    public class Hediff_CorpseRotPenalty : HediffWithComps
    {
        public float rotPenalty = 0.2f; // 20% penalty per reuse

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            // This hediff persists on the corpse
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
                
                if (corpse == null)
                {
                    Log.Error("Corpse is null");
                    return null;
                }
                
                if (corpse.InnerPawn == null)
                {
                    Log.Error("Corpse.InnerPawn is null");
                    return null;
                }
                
                if (map == null)
                {
                    Log.Error("Map is null");
                    return null;
                }
                
                var innerPawn = corpse.InnerPawn;
                Log.Message("InnerPawn: " + innerPawn.Name?.ToStringShort + ", KindDef: " + innerPawn.kindDef?.defName);
                
                if (innerPawn.kindDef == null)
                {
                    Log.Error("InnerPawn.kindDef is null");
                    return null;
                }
                
                var pawnKind = innerPawn.kindDef;
                var faction = Faction.OfPlayer;
                
                if (faction == null)
                {
                    Log.Error("Player faction is null");
                    return null;
                }
                
                // Create new pawn using simplified constructor
                var request = new PawnGenerationRequest(pawnKind, faction, PawnGenerationContext.NonPlayer)
                {
                    FixedGender = innerPawn.gender,
                    ForceGenerateNewPawn = true,
                    CanGeneratePawnRelations = false,
                    ColonistRelationChanceFactor = 0f,
                    AllowDead = false,
                    AllowDowned = false
                };
                
                Log.Message("Calling PawnGenerator.GeneratePawn...");
                var newPawn = PawnGenerator.GeneratePawn(request);
                
                if (newPawn == null)
                {
                    Log.Error("PawnGenerator.GeneratePawn returned null");
                    return null;
                }
                
                Log.Message("Generated pawn: " + newPawn.ThingID);

                // Copy basic info and appearance
                var originalName = innerPawn.Name?.ToStringShort ?? "Revenant";
                newPawn.Name = new NameTriple("Undead", originalName, "");
                Log.Message("Set name to: " + newPawn.Name.ToStringFull);
                
                // Set biological age
                if (newPawn.ageTracker != null && innerPawn.ageTracker != null)
                {
                    newPawn.ageTracker.AgeBiologicalTicks = innerPawn.ageTracker.AgeBiologicalTicks;
                    newPawn.ageTracker.AgeChronologicalTicks = innerPawn.ageTracker.AgeChronologicalTicks;
                    Log.Message("Copied age data");
                }
                
                // Copy appearance details to make undead look like original
                if (newPawn.story != null && innerPawn.story != null)
                {
                    // Copy hair (definition)
                    newPawn.story.hairDef = innerPawn.story.hairDef;
                    
                    // Copy hair color (it's in story, not style!)
                    try
                    {
                        newPawn.story.HairColor = innerPawn.story.HairColor;
                        Log.Message("Copied hair color from story system");
                    }
                    catch (Exception e)
                    {
                        Log.Warning("Could not copy hair color from story: " + e.Message);
                    }
                    
                    // Copy head shape
                    newPawn.story.headType = innerPawn.story.headType;
                    
                    // Copy body type  
                    newPawn.story.bodyType = innerPawn.story.bodyType;
                    
                    // Copy and modify skin color
                    if (innerPawn.story.skinColorOverride != null)
                    {
                        var originalSkin = innerPawn.story.skinColorOverride.Value;
                        // Add blue-grey tint while preserving original skin tone
                        newPawn.story.skinColorOverride = new Color(
                            originalSkin.r * 0.8f + 0.1f, // Slightly reduce red, add a bit
                            originalSkin.g * 0.8f + 0.1f, // Slightly reduce green, add a bit  
                            originalSkin.b * 0.9f + 0.2f  // Keep blue, add more blue tint
                        );
                    }
                    else
                    {
                        // If no skin color override, apply a generic undead tint
                        newPawn.story.skinColorOverride = new Color(0.7f, 0.7f, 0.85f); // Pale blue-grey
                    }
                    
                    Log.Message("Copied appearance details (hair style, hair color, head, body, skin)");
                }
                
                // Copy style details (tattoos, facial hair) - using correct property names
                if (newPawn.style != null && innerPawn.style != null)
                {
                    // Copy facial hair if it exists
                    if (innerPawn.style.beardDef != null)
                    {
                        newPawn.style.beardDef = innerPawn.style.beardDef;
                        Log.Message("Copied facial hair from style system");
                    }
                    
                    // Copy face tattoo using correct property name (FaceTattoo, not faceTattoo)
                    try
                    {
                        if (innerPawn.style.FaceTattoo != null)
                        {
                            newPawn.style.FaceTattoo = innerPawn.style.FaceTattoo;
                            Log.Message("Copied face tattoo: " + innerPawn.style.FaceTattoo.label);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Warning("Could not copy face tattoo: " + e.Message);
                    }
                    
                    // Copy body tattoo using correct property name (BodyTattoo, not bodyTattoo)
                    try
                    {
                        if (innerPawn.style.BodyTattoo != null)
                        {
                            newPawn.style.BodyTattoo = innerPawn.style.BodyTattoo;
                            Log.Message("Copied body tattoo: " + innerPawn.style.BodyTattoo.label);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Warning("Could not copy body tattoo: " + e.Message);
                    }
                }
                else
                {
                    Log.Message("Style system not available - skipping facial hair and tattoos");
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
                    Log.Message("Copied skills");
                }

                // Clear mood memories
                if (newPawn.needs?.mood?.thoughts?.memories != null)
                {
                    newPawn.needs.mood.thoughts.memories.Memories.Clear();
                    Log.Message("Cleared mood memories");
                }
                
                // Calculate rot progress including any previous penalties
                float rotProgress = 0f;
                float additionalRot = 0f;
                
                // Check for existing rot penalty hediff on corpse
                var rotPenaltyDef = DefDatabase<HediffDef>.GetNamed("CorpseRotPenalty", false);
                if (rotPenaltyDef != null)
                {
                    var rotPenaltyHediff = innerPawn.health.hediffSet.GetFirstHediffOfDef(rotPenaltyDef);
                    if (rotPenaltyHediff != null && rotPenaltyHediff is Hediff_CorpseRotPenalty penalty)
                    {
                        additionalRot = penalty.rotPenalty;
                    }
                }
                
                // Calculate rot progress
                if (corpse.GetRotStage() == RotStage.Dessicated)
                {
                    rotProgress = 1f;
                }
                else
                {
                    var rottable = corpse.GetComp<CompRottable>();
                    if (rottable != null)
                    {
                        // Non-linear scaling for early rot (less punishing)
                        float rawRot = rottable.RotProgress;
                        rotProgress = (float)Math.Pow(rawRot, 1.5f);
                    }
                }

                // Apply reuse penalty (from previous animation)
                rotProgress = Math.Min(1f, rotProgress + additionalRot);

                Log.Message($"Calculated rotProgress: {rotProgress:0.00}");
                            
                // Add undead hediffs - FIXED ORDER
                Log.Message("Adding UndeadTraits hediff...");
                var undeadTraitsDef = DefDatabase<HediffDef>.GetNamed("UndeadTraits", false);
                if (undeadTraitsDef != null)
                {
                    var undeadHediff = HediffMaker.MakeHediff(undeadTraitsDef, newPawn) as Hediff_Undead;
                    if (undeadHediff != null)
                    {
                        undeadHediff.SetCorpseRotProgress(rotProgress);
                        newPawn.health.AddHediff(undeadHediff);
                        Log.Message("Added UndeadTraits hediff");
                    }
                    else
                    {
                        Log.Error("Failed to cast undead hediff to Hediff_Undead");
                    }
                }
                else
                {
                    Log.Error("UndeadTraits HediffDef not found!");
                }
                
                // Add decay hediff - SIMPLIFIED
                Log.Message("Adding UndeadDecay hediff...");
                var decayDef = DefDatabase<HediffDef>.GetNamed("UndeadDecay", false);
                if (decayDef != null)
                {
                    Log.Message("Creating decay hediff...");
                    var decayHediff = HediffMaker.MakeHediff(decayDef, newPawn);
                    Log.Message($"Decay hediff created: {decayHediff != null}");
                    
                    if (decayHediff != null)
                    {
                        Log.Message($"Decay hediff type: {decayHediff.GetType().Name}");
                        Log.Message($"Decay hediff initial severity: {decayHediff.Severity}");
                        
                        // Try casting to our custom class
                        if (decayHediff is Hediff_UndeadDecay customDecay)
                        {
                            Log.Message("Successfully cast to Hediff_UndeadDecay");
                            customDecay.SetDecayRate();
                            Log.Message($"After SetDecayRate, severity is: {customDecay.Severity}");
                        }
                        else
                        {
                            Log.Warning("Failed to cast to Hediff_UndeadDecay - using base hediff");
                            decayHediff.Severity = 0f;
                        }
                        
                        // Add hediff to pawn
                        Log.Message("Adding decay hediff to pawn...");
                        newPawn.health.AddHediff(decayHediff);
                        Log.Message("Decay hediff added to pawn");
                        
                        // Immediately verify it was added
                        var verifyHediff = newPawn.health.hediffSet.GetFirstHediffOfDef(decayDef);
                        Log.Message($"Verification: Decay hediff present after adding: {verifyHediff != null}");
                        if (verifyHediff != null)
                        {
                            Log.Message($"Verified hediff severity: {verifyHediff.Severity:0.00}");
                            Log.Message($"Verified hediff stage: {verifyHediff.CurStage?.label ?? "none"}");
                        }
                    }
                    else
                    {
                        Log.Error("HediffMaker.MakeHediff returned null for UndeadDecay");
                    }
                }
                else
                {
                    Log.Error("UndeadDecay HediffDef not found!");
                }

                // Make undead immune to various conditions
                Log.Message("Making undead immune to conditions...");
                MakeUndeadImmune(newPawn);

                // Find a valid spawn location near the target position
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

                // Spawn the pawn
                Log.Message("Spawning undead...");
                GenSpawn.Spawn(newPawn, spawnLoc, map);
                Log.Message("Successfully spawned undead: " + newPawn.Name.ToStringShort + " at " + spawnLoc);
                
                // Force graphics update to show new appearance
                if (newPawn.Drawer?.renderer != null)
                {
                    newPawn.Drawer.renderer.SetAllGraphicsDirty();
                    Log.Message("Updated undead graphics");
                }
                
                // FINAL HEDIFF CHECK - see what's actually on the pawn
                Log.Message("=== FINAL HEDIFF CHECK ===");
                var allHediffs = newPawn.health.hediffSet.hediffs;
                Log.Message($"Total hediffs on pawn: {allHediffs.Count}");
                foreach (var hediff in allHediffs)
                {
                    Log.Message($"Hediff: {hediff.def.defName} ({hediff.def.label}) - Severity: {hediff.Severity:0.00} - Stage: {hediff.CurStage?.label ?? "none"}");
                }
                
                // Specifically check for our expected hediffs
                var undeadTraitsCheck = newPawn.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("UndeadTraits"));
                var decayCheck = newPawn.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("UndeadDecay"));
                var rotPenaltyCheck = newPawn.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("UndeadRotPenalty"));
                
                Log.Message($"UndeadTraits present: {undeadTraitsCheck != null}");
                Log.Message($"UndeadDecay present: {decayCheck != null}");
                Log.Message($"UndeadRotPenalty present: {rotPenaltyCheck != null}");
                Log.Message("=== END FINAL CHECK ===");
                
                // Don't manually destroy the corpse - let RimWorld's recipe system handle cleanup
                // This prevents the "already-destroyed thing" error
                Log.Message("Leaving corpse cleanup to RimWorld's recipe system");
                corpse.Destroy();
                
                Log.Message("Undead creation completed successfully");
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
            // Remove existing disease hediffs that undead should be immune to
            var hediffsToRemove = new List<Hediff>();
            
            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                // Remove diseases, infections, and other conditions undead should be immune to
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
            
            // Remove the hediffs
            foreach (var hediff in hediffsToRemove)
            {
                pawn.health.RemoveHediff(hediff);
            }
            
            // Additional runtime immunities can be added here
            Log.Message($"Removed {hediffsToRemove.Count} incompatible hediffs from undead {pawn.Name.ToStringShort}");
        }
    }

    public class Recipe_CobbleRevenant : RecipeWorker
    {
        private static Corpse pendingCorpse = null;
        
        public Recipe_CobbleRevenant()
        {
            Log.Message("Recipe_CobbleRevenant constructor called");
        }
        
        public override void ConsumeIngredient(Thing ingredient, RecipeDef recipe, Map map)
        {
            Log.Message("ConsumeIngredient called for: " + ingredient?.Label);
            
            // Store the corpse but don't consume it yet
            if (ingredient is Corpse corpse)
            {
                pendingCorpse = corpse;
                Log.Message("Storing corpse for reanimation: " + corpse.InnerPawn.Name.ToStringShort);
                // Don't call base.ConsumeIngredient for corpses - we'll handle it manually
                return;
            }
            
            // Consume other ingredients normally
            base.ConsumeIngredient(ingredient, recipe, map);
        }

        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            Log.Message("Recipe_CobbleRevenant.Notify_IterationCompleted called");
            
            var corpse = pendingCorpse;
            
            if (corpse != null && corpse.Map != null && !corpse.Destroyed)
            {
                Log.Message("Using stored corpse: " + corpse.InnerPawn.Name.ToStringShort);
                var map = corpse.Map;
                var loc = billDoer.Position;
                
                // Store corpse name for message before it gets destroyed
                string corpseName = corpse.InnerPawn.Name.ToStringShort;
                
                try
                {
                    var undead = NecromancyUtility.CreateUndeadFromCorpse(corpse, map, loc);
                    
                    if (undead != null)
                    {
                        Log.Message("Undead creation successful, sending message");
                        Messages.Message("Revenant created from " + corpseName + "!", 
                            undead, MessageTypeDefOf.PositiveEvent);
                        
                        // Success! Now properly consume the corpse through the recipe system
                        if (!corpse.Destroyed)
                        {
                            Log.Message("Properly consuming corpse through recipe system");
                            base.ConsumeIngredient(corpse, recipe, map);
                        }
                        
                        // Clear the corpse reference and exit
                        pendingCorpse = null;
                        Log.Message("Recipe completed successfully");
                        return;
                    }
                    else
                    {
                        Log.Error("CreateUndeadFromCorpse returned null");
                    }
                }
                catch (Exception e)
                {
                    Log.Error("Error creating undead: " + e.Message + "\n" + e.StackTrace);
                }
            }
            else
            {
                Log.Error("No valid stored corpse found for reanimation");
            }
            
            // If we get here, something went wrong - clean up
            if (pendingCorpse != null && !pendingCorpse.Destroyed)
            {
                Log.Message("Cleaning up failed reanimation attempt through recipe system");
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
                Log.Message("Corpse already destroyed, just clearing reference");
                pendingCorpse = null;
            }
        }

        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            // Filter corpses by type and freshness - ONLY allow humanlike corpses
            if (thing is Corpse corpse)
            {
                var pawn = corpse.InnerPawn;
                
                // STRICT humanlike-only check - reject everything else
                if (!pawn.RaceProps.Humanlike)
                {
                    Log.Message($"Rejecting corpse: {pawn.def.defName} - not humanlike");
                    return false;
                }
                
                // Double-check: explicitly reject mechanoids, animals, and entities
                if (pawn.RaceProps.IsMechanoid)
                {
                    Log.Message($"Rejecting corpse: {pawn.def.defName} - is mechanoid");
                    return false;
                }
                
                if (pawn.RaceProps.Animal)
                {
                    Log.Message($"Rejecting corpse: {pawn.def.defName} - is animal");
                    return false;
                }
                
                // Additional safety check for modded entity types
                if (pawn.def.defName.Contains("Mechanoid") || 
                    pawn.def.defName.Contains("Entity") || 
                    pawn.def.defName.Contains("Drone") ||
                    pawn.def.defName.Contains("Anomaly"))
                {
                    Log.Message($"Rejecting corpse: {pawn.def.defName} - detected entity/mechanoid/anomaly in name");
                    return false;
                }
                
                // Check freshness (must be at least 15% fresh)
                float rotProgress = 0f;
                var rottable = corpse.GetComp<CompRottable>();
                if (rottable != null)
                {
                    rotProgress = rottable.RotProgress;
                }
                
                // Include any existing rot penalties
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
                bool freshEnough = freshness >= 0.15f;
                
                if (!freshEnough)
                {
                    Log.Message($"Rejecting corpse: {pawn.def.defName} - too rotted (freshness: {freshness:0.00})");
                }
                
                Log.Message($"Corpse check: {pawn.def.defName} - Humanlike: {pawn.RaceProps.Humanlike}, Fresh enough: {freshEnough}");
                return freshEnough;
            }
            
            return base.AvailableOnNow(thing, part);
        }
    }
}