using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace NecromancersTable
{
    public class NecromancersTableMod : Mod
    {
        public NecromancersTableMod(ModContentPack content) : base(content)
        {
        }
    }

    public class Hediff_UndeadDecay : HediffWithComps
    {
        private int ticksBetweenDecay = 2500; // How often decay happens (about 1 hour)
        private float decayPerInterval = 0.01f; // How much decay per interval

        public override void PostMake()
        {
            base.PostMake();
        }

        public void SetDecayRate(float corpseRotProgress)
        {
            // Calculate decay rate based on corpse freshness
            // Fresh corpse (0% rot) = 1 year to fully decay
            // Dessicated (100% rot) = 3 days to fully decay
            float freshness = 1f - corpseRotProgress;
            
            // Total decay intervals needed to reach 100%
            int totalIntervalsForFresh = (60 * 24 * 60) / (ticksBetweenDecay / 60); // 1 year worth of intervals
            int totalIntervalsForRotten = (3 * 24 * 60) / (ticksBetweenDecay / 60); // 3 days worth of intervals
            
            int totalIntervals = (int)(totalIntervalsForRotten + (totalIntervalsForFresh - totalIntervalsForRotten) * freshness);
            
            // Calculate decay per interval to reach 1.0 severity in the calculated time
            decayPerInterval = 1f / totalIntervals;
        }

        public override void Tick()
        {
            base.Tick();
            
            // Only decay on intervals, not every tick
            if (pawn.IsHashIntervalTick(ticksBetweenDecay))
            {
                Severity += decayPerInterval;
                
                if (Severity >= 1f)
                {
                    if (pawn.Dead == false)
                    {
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
        }
    }

    public class Hediff_Undead : HediffWithComps
    {
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
            }
        }

        public override void Tick()
        {
            base.Tick();
            
            // Keep needs frozen at 90%
            if (pawn.IsHashIntervalTick(150)) // Check every 150 ticks for performance
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
                }
            }
        }
    }

    [StaticConstructorOnStartup]
    public static class NecromancyUtility
    {
        static NecromancyUtility()
        {
            // Patch the recipe workers
            var cobbleRecipe = DefDatabase<RecipeDef>.GetNamed("CobbleTogetherRevenant", false);
            
            if (cobbleRecipe != null)
            {
                cobbleRecipe.workerClass = typeof(Recipe_CobbleRevenant);
            }
        }

        public static Pawn CreateUndeadFromCorpse(Corpse corpse, Map map, IntVec3 loc)
        {
            var innerPawn = corpse.InnerPawn;
            var pawnKind = innerPawn.kindDef;
            var faction = Faction.OfPlayer;
            
            // Create new pawn using simplified constructor
            var request = new PawnGenerationRequest(pawnKind, faction, PawnGenerationContext.NonPlayer);
            request.FixedGender = innerPawn.gender;
            request.ForceGenerateNewPawn = true;
            request.CanGeneratePawnRelations = false;
            request.ColonistRelationChanceFactor = 0f;
            request.AllowDead = false;
            request.AllowDowned = false;
            request.MustBeCapableOfViolence = true;
            
            var newPawn = PawnGenerator.GeneratePawn(request);

            // Copy basic info
            newPawn.Name = new NameTriple("Undead", innerPawn.Name?.ToStringShort ?? "Revenant", "");
            
            // Set biological age
            newPawn.ageTracker.AgeBiologicalTicks = innerPawn.ageTracker.AgeBiologicalTicks;
            newPawn.ageTracker.AgeChronologicalTicks = innerPawn.ageTracker.AgeChronologicalTicks;
            
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
            if (newPawn.needs?.mood != null)
            {
                newPawn.needs.mood.thoughts.memories.Memories.Clear();
            }
            
            // Add undead hediffs
            var undeadHediff = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("UndeadTraits"), newPawn);
            newPawn.health.AddHediff(undeadHediff);
            
            var decayHediff = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("UndeadDecay"), newPawn) as Hediff_UndeadDecay;
            if (decayHediff != null)
            {
                float rotProgress = 0f;
                if (corpse.GetRotStage() == RotStage.Dessicated)
                {
                    rotProgress = 1f;
                }
                else
                {
                    var rottable = corpse.GetComp<CompRottable>();
                    if (rottable != null)
                    {
                        rotProgress = rottable.RotProgress;
                    }
                }
                decayHediff.SetDecayRate(rotProgress);
                newPawn.health.AddHediff(decayHediff);
            }

            // Spawn the pawn
            GenSpawn.Spawn(newPawn, loc, map);
            
            // Destroy the corpse
            corpse.Destroy();
            
            return newPawn;
        }
    }

    public class Recipe_CobbleRevenant : RecipeWorker
    {
        public override void ConsumeIngredient(Thing ingredient, RecipeDef recipe, Map map)
        {
            // Don't consume yet, we'll handle it manually
        }

        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);
            
            var corpse = ingredients.OfType<Corpse>().FirstOrDefault();
            if (corpse != null)
            {
                var loc = billDoer.Position;
                NecromancyUtility.CreateUndeadFromCorpse(corpse, billDoer.Map, loc);
                
                Messages.Message("Revenant created from corpse!", MessageTypeDefOf.PositiveEvent);
            }
        }
    }
}