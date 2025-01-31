﻿using HugsLib;
using RimWorld;
using rjw;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RJW_Menstruation
{
    [Flags]
    public enum SeasonalBreed
    {
        Always = 0,
        Spring = 1,
        Summer = 2,
        Fall = 4,
        Winter = 8,
        FirstHalf = Spring | Summer,
        SecondHalf = Fall | Winter
    }


    public class CompProperties_Menstruation : HediffCompProperties
    {
        public float maxCumCapacity; // ml
        public float baseImplantationChanceFactor;
        public float basefertilizationChanceFactor;
        public float deviationFactor;
        public int folicularIntervalDays = 14; //before ovulation including beginning of bleeding
        public int lutealIntervalDays = 14; //after ovulation until bleeding
        public int bleedingIntervalDays = 6; //must be less than folicularIntervalDays
        public int recoveryIntervalDays = 10; //additional infertile days after gave birth
        public int eggLifespanDays = 2; //fertiledays = ovaluationday - spermlifespan ~ ovaluationday + egglifespanday
        public string wombTex = "Womb/Womb"; //fertiledays = ovaluationday - spermlifespan ~ ovaluationday + egglifespanday
        public string vagTex = "Genitals/Vagina"; //fertiledays = ovaluationday - spermlifespan ~ ovaluationday + egglifespanday
        public bool infertile = false;
        public int ovaryPower = 600000000; // default: almost unlimited ovulation 
        public bool consealedEstrus = false;
        public SeasonalBreed breedingSeason = SeasonalBreed.Always;
        public int estrusDaysBeforeOvulation = 3;


        public CompProperties_Menstruation()
        {

            compClass = typeof(HediffComp_Menstruation);
        }
    }


    public class CompProperties_Anus : HediffCompProperties
    {
        public string analTex = "Genitals/Anal";

        public CompProperties_Anus()
        {
            compClass = typeof(HediffComp_Anus);
        }
    }

    public class HediffComp_Menstruation : HediffComp
    {
        const float minmakefilthvalue = 1.0f;
        //const int ovarypowerthreshold = 72;

        public static readonly int tickInterval = 2500; // an hour
        public CompProperties_Menstruation Props;
        public Stage curStage = Stage.Follicular;
        public int curStageHrs = 0;
        public Action actionref;
        public bool loaded = false;
        public int ovarypower = -100000;
        public int eggstack = 0;
        public bool DoCleanWomb = false;

        public enum Stage
        {
            Follicular,
            Ovulatory,
            Luteal,
            Bleeding,
            Fertilized, //Obsoleted
            Pregnant,
            Recover,
            None,
            Young,
            ClimactericFollicular,
            ClimactericLuteal,
            ClimactericBleeding,
            Anestrus
        }


        public static readonly Dictionary<Stage, Texture2D> StageTexture = new Dictionary<Stage, Texture2D>()
        {
            { Stage.Follicular, TextureCache.humanTexture },
            { Stage.ClimactericFollicular, TextureCache.humanTexture },
            { Stage.Luteal, TextureCache.fertilityTexture },
            { Stage.ClimactericLuteal, TextureCache.fertilityTexture },
            { Stage.Bleeding, TextureCache.khorneTexture },
            { Stage.ClimactericBleeding, TextureCache.khorneTexture },
            { Stage.Recover, TextureCache.nurgleTexture }
        };


        protected List<Cum> cums;
        protected List<Egg> eggs;
        protected int follicularIntervalhours = -1;
        protected int lutealIntervalhours = -1;
        protected int bleedingIntervalhours = -1;
        protected int recoveryIntervalhours = -1;
        protected int currentIntervalhours = -1;
        protected float crampPain = -1;
        protected Need sexNeed = null;
        protected string customwombtex = null;
        protected string customvagtex = null;
        protected bool estrusflag = false;
        protected int opcache = -1;
        protected HediffComp_Breast breastcache = null;
        protected float antisperm = 0.0f;
        protected float? originvagsize = null;

        public int ovarypowerthreshold
        {
            get
            {
                if (opcache < 0) opcache = (int)(72f * parent.pawn.def.race.lifeExpectancy / ThingDefOf.Human.race.lifeExpectancy);
                return opcache;
            }
        }

        public int FollicularIntervalHours
        {
            get
            {
                return (int)((follicularIntervalhours - bleedingIntervalhours) * CycleFactor);
            }
        }

        public float TotalCum
        {
            get
            {
                float res = 0;
                if (cums.NullOrEmpty()) return 0;
                foreach (Cum cum in cums)
                {
                    res += cum.Volume;
                }
                return res;
            }
        }
        public float TotalFertCum
        {
            get
            {
                float res = 0;
                if (cums.NullOrEmpty()) return 0;
                foreach (Cum cum in cums)
                {
                    if (!cum.notcum) res += cum.FertVolume;
                }
                return res;
            }
        }
        public float TotalCumPercent
        {
            get
            {
                float res = 0;
                if (cums.NullOrEmpty()) return 0;
                foreach (Cum cum in cums)
                {
                    res += cum.Volume;
                }
                return res / Props.maxCumCapacity;
            }
        }
        public float CumCapacity
        {
            get
            {
                float res = Props.maxCumCapacity * parent.pawn.BodySize;
                if (curStage != Stage.Pregnant) res *= 2500f; // originally 500
                return res;
            }
        }
        public float CumInFactor
        {
            get
            {
                float res = 5.0f;
                if (parent.pawn.health.hediffSet.HasHediff(VariousDefOf.RJW_IUD)) res = 0.005f;
                return res;
            }
        }
        //make follicular interval into half and double egg lifespan
        public float CycleFactor
        {
            get
            {
                if (xxx.has_quirk(parent.pawn, "Breeder")) return 0.5f;

                return 1.0f;
            }
        }
        //effect on implant chance
        public float ImplantFactor
        {
            get
            {
                float factor = 1.0f;
                if (parent.pawn.Has(Quirk.Breeder)) factor = 10.0f;
                //if (xxx.is_animal(parent.pawn)) factor *= RJWPregnancySettings.animal_impregnation_chance / 100f;
                //else factor *= RJWPregnancySettings.humanlike_impregnation_chance / 100f;
                return parent.pawn.health.capacities.GetLevel(xxx.reproduction) * factor;
            }
        }
        public IEnumerable<string> GetCumsInfo
        {
            get
            {
                if (!cums.NullOrEmpty()) foreach (Cum cum in cums)
                    {
                        if (!cum.notcum) yield return String.Format(cum.pawn?.Label + ": {0:0.##}ml", cum.Volume);
                        else yield return String.Format(cum.notcumLabel + ": {0:0.##}ml", cum.Volume);
                    }
                else yield return Translations.Info_noCum;
            }
        }
        public Color GetCumMixtureColor
        {
            get
            {
                Color mixedcolor = Color.white;

                if (!cums.NullOrEmpty())
                {
                    float mixedsofar = 0;
                    foreach (Cum cum in cums)
                    {
                        if (cum.Volume > 0)
                        {
                            mixedcolor = Colors.CMYKLerp(mixedcolor, cum.color, cum.Volume / (mixedsofar + cum.Volume));
                            mixedsofar += cum.Volume;
                        }
                    }
                }
                return mixedcolor;
            }
        }
        public string GetCurStageLabel
        {
            get
            {
                switch (curStage)
                {
                    case Stage.Follicular:
                        return Translations.Stage_Follicular;
                    case Stage.Ovulatory:
                        return Translations.Stage_Ovulatory;
                    case Stage.Luteal:
                        return Translations.Stage_Luteal;
                    case Stage.Bleeding:
                        return Translations.Stage_Bleeding;
                    case Stage.Fertilized:
                        return Translations.Stage_Fertilized;
                    case Stage.Pregnant:
                        if (Configurations.InfoDetail == Configurations.DetailLevel.All || (PregnancyHelper.GetPregnancy(parent.pawn)?.Visible ?? false)) return Translations.Stage_Pregnant;
                        else return Translations.Stage_Luteal;
                    case Stage.Recover:
                        return Translations.Stage_Recover;
                    case Stage.None:
                    case Stage.Young:
                        return Translations.Stage_None;
                    case Stage.ClimactericFollicular:
                        return Translations.Stage_Follicular + " - " + Translations.Stage_Climacteric;
                    case Stage.ClimactericLuteal:
                        return Translations.Stage_Luteal + " - " + Translations.Stage_Climacteric;
                    case Stage.ClimactericBleeding:
                        return Translations.Stage_Bleeding + " - " + Translations.Stage_Climacteric;
                    case Stage.Anestrus:
                        return Translations.Stage_Anestrus;
                    default:
                        return "";
                }
            }

        }
        public string wombTex
        {
            get
            {
                if (customwombtex == null) return Props.wombTex;
                else return customwombtex;
            }
            set
            {
                customwombtex = value;
            }
        }
        public string vagTex
        {
            get
            {
                if (customvagtex == null) return Props.vagTex;
                else return customvagtex;
            }
            set
            {
                customvagtex = value;
            }
        }
        public string GetFertilizingInfo
        {
            get
            {
                string res = "";
                if (!eggs.NullOrEmpty())
                {
                    int fertilized = 0;
                    foreach (Egg egg in eggs)
                    {
                        if (egg.fertilized) fertilized++;
                    }
                    if (fertilized != 0) res += fertilized + " " + Translations.Dialog_WombInfo05;
                    if (fertilized != 0 && eggs.Count - fertilized != 0) res += ", ";
                    if (cums.NullOrEmpty() || TotalFertCum == 0)
                    {
                        if (eggs.Count - fertilized != 0) res += eggs.Count - fertilized + " " + Translations.Dialog_WombInfo07;
                    }
                    else
                    {
                        if (eggs.Count - fertilized != 0) res += eggs.Count - fertilized + " " + Translations.Dialog_WombInfo06;
                    }
                }
                return res;
            }
        }
        public bool IsEggFertilizing
        {
            get
            {
                if (!eggs.NullOrEmpty())
                {
                    if (!cums.NullOrEmpty()) foreach (Cum cum in cums)
                        {
                            if (cum.FertVolume > 0) return true;
                        }
                    return false;

                }
                else return false;
            }
        }
        /// <summary>
        /// returns fertstage. if not fertilized returns -1
        /// </summary>
        public int IsFertilized
        {
            get
            {
                if (!eggs.NullOrEmpty()) foreach (Egg egg in eggs)
                    {
                        if (egg.fertilized) return egg.fertstage;
                    }
                return -1;
            }
        }
        public bool IsEggExist
        {
            get
            {
                return !eggs.NullOrEmpty();
            }
        }
        public bool IsDangerDay
        {
            get
            {
                if (parent.pawn.health.hediffSet.HasHediff(VariousDefOf.RJW_IUD)) return false;

                if (curStage == Stage.Follicular || curStage == Stage.ClimactericFollicular)
                {
                    if (curStageHrs > 0.7f * (follicularIntervalhours - bleedingIntervalhours)) return true;
                }
                else if (curStage == Stage.Luteal || curStage == Stage.ClimactericLuteal)
                {
                    if (curStageHrs < Props.eggLifespanDays * 24) return true;
                }
                else if (curStage == Stage.Ovulatory) return true;
                return false;
            }
        }
        public int GetNumofEggs
        {
            get
            {
                if (eggs.NullOrEmpty()) return 0;
                else return eggs.Count;
            }
        }
        public Color BloodColor
        {
            get
            {
                try
                {
                    Color c = parent.pawn.def.race.BloodDef.graphicData.color;
                    return c;
                }
                catch
                {
                    return Colors.blood;
                }

            }
        }
        public HediffComp_Breast Breast
        {
            get
            {
                if (breastcache == null)
                {
                    breastcache = parent.pawn.GetBreastComp();
                }
                return breastcache;
            }
        }

        public float OriginVagSize
        {
            get
            {
                if (originvagsize == null)
                {
                    originvagsize = parent.Severity;
                }
                return originvagsize ?? 0.1f;
            }
            set
            {
                originvagsize = value;
            }
        }

        public float CurStageIntervalHours
        {
            get
            {
                switch (curStage)
                {
                    case Stage.Follicular:
                    case Stage.ClimactericFollicular:
                        return FollicularIntervalHours;
                    case Stage.Luteal:
                    case Stage.ClimactericLuteal:
                        return lutealIntervalhours;
                    case Stage.Bleeding:
                    case Stage.ClimactericBleeding:
                        return bleedingIntervalhours;
                    case Stage.Recover:
                        return recoveryIntervalhours;
                    case Stage.Pregnant:
                        return currentIntervalhours;
                    default:
                        return float.PositiveInfinity;
                }
            }
        }

        public float StageProgress
        {
            get
            {
                return Mathf.Clamp01(curStageHrs / CurStageIntervalHours);
            }
        }

        public Texture2D GetStageTexture
        {
            get
            {
                Texture2D tex;
                if (!StageTexture.TryGetValue(curStage, out tex)) tex = TextureCache.tzeentchTexture;
                return tex;
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Collections.Look(ref cums, saveDestroyedThings: true, label: "cums", lookMode: LookMode.Deep, ctorArgs: new object[0]);
            Scribe_Collections.Look(ref eggs, saveDestroyedThings: true, label: "eggs", lookMode: LookMode.Deep, ctorArgs: new object[0]);
            Scribe_Values.Look(ref curStage, "curStage", curStage, true);
            Scribe_Values.Look(ref curStageHrs, "curStageHrs", curStageHrs, true);
            Scribe_Values.Look(ref follicularIntervalhours, "follicularIntervalhours", follicularIntervalhours, true);
            Scribe_Values.Look(ref lutealIntervalhours, "lutealIntervalhours", lutealIntervalhours, true);
            Scribe_Values.Look(ref bleedingIntervalhours, "bleedingIntervalhours", bleedingIntervalhours, true);
            Scribe_Values.Look(ref recoveryIntervalhours, "recoveryIntervalhours", recoveryIntervalhours, true);
            Scribe_Values.Look(ref currentIntervalhours, "currentIntervalhours", currentIntervalhours, true);
            Scribe_Values.Look(ref crampPain, "crampPain", crampPain, true);
            Scribe_Values.Look(ref ovarypower, "ovarypower", ovarypower, true);
            Scribe_Values.Look(ref eggstack, "eggstack", eggstack, true);
            Scribe_Values.Look(ref estrusflag, "estrusflag", estrusflag, true);
            Scribe_Values.Look(ref originvagsize, "originvagsize", originvagsize, true);
            Scribe_Values.Look(ref DoCleanWomb, "DoCleanWomb", DoCleanWomb, true);
        }


        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            if (!loaded)
            {
                Initialize();
            }
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            //initializer moved to SpawnSetup
            //if (!loaded)
            //{
            //    Initialize();
            //}
        }

        public override void CompPostPostRemoved()
        {
            if (parent?.pawn?.GetMenstruationComp() == this)
            {
                Log.Warning("Something tried to remove hediff with wrong way.");
            }
            else
            {
                HugsLibController.Instance.TickDelayScheduler.TryUnscheduleCallback(actionref);
                Log.Message(parent.pawn.Label + "tick scheduler removed");
                base.CompPostPostRemoved();
            }
        }





        /// <summary>
        /// Get fluid in womb that not a cum
        /// </summary>
        /// <param name="notcumlabel"></param>
        /// <returns></returns>
        public Cum GetNotCum(string notcumlabel)
        {
            if (!cums.NullOrEmpty()) foreach (Cum cum in cums)
                {
                    if (cum.notcum && cum.notcumLabel.Equals(notcumlabel)) return cum;
                }
            return null;
        }

        /// <summary>
        /// Get pawn's cum in womb
        /// </summary>
        /// <param name="pawn"></param>
        /// <returns></returns>
        public Cum GetCum(Pawn pawn)
        {
            if (!cums.NullOrEmpty()) foreach (Cum cum in cums)
                {
                    if (!cum.notcum && cum.pawn.Equals(pawn)) return cum;
                }
            return null;
        }

        /// <summary>
        /// Inject pawn's cum into womb
        /// </summary>
        /// <param name="pawn"></param>
        /// <param name="injectedvolume"></param>
        /// <param name="fertility"></param>
        /// <param name="filthdef"></param>
        public void CumIn(Pawn pawn, float injectedvolume, float fertility = 1.0f, ThingDef filthdef = null)
        {
            float volume = injectedvolume * CumInFactor;
            float cumd = TotalCumPercent;
            float tmp = TotalCum + volume;
            if (tmp > CumCapacity)
            {
                float cumoutrate = 1 - (CumCapacity / tmp);
                bool merged = false;
                if (!cums.NullOrEmpty()) foreach (Cum cum in cums)
                    {
                        if (cum.pawn.Equals(pawn))
                        {
                            cum.MergeWithCum(volume, fertility, filthdef);
                            merged = true;
                        }
                        cum.DismishForce(cumoutrate);
                    }
                if (!merged) cums.Add(new Cum(pawn, volume * (1 - cumoutrate), fertility, filthdef));
            }
            else
            {

                bool merged = false;
                if (!cums.NullOrEmpty()) foreach (Cum cum in cums)
                    {
                        if (cum.pawn.Equals(pawn))
                        {
                            cum.MergeWithCum(volume, fertility, filthdef);
                            merged = true;
                        }
                    }
                if (!merged) cums.Add(new Cum(pawn, volume, fertility, filthdef));
            }
            cumd = TotalCumPercent - cumd;

            parent.pawn.records.AddTo(VariousDefOf.AmountofCreampied, injectedvolume);
            AfterCumIn(pawn);
            AfterFluidIn(cumd);
        }

        /// <summary>
        /// Inject pawn's fluid into womb
        /// </summary>
        /// <param name="pawn"></param>
        /// <param name="volume"></param>
        /// <param name="notcumlabel"></param>
        /// <param name="decayresist"></param>
        /// <param name="filthdef"></param>
        public void CumIn(Pawn pawn, float volume, string notcumlabel, float decayresist = 0, ThingDef filthdef = null)
        {
            float tmp = TotalCum + volume;
            float cumd = TotalCumPercent;
            if (tmp > CumCapacity)
            {
                float cumoutrate = 1 - (CumCapacity / tmp);
                bool merged = false;
                if (!cums.NullOrEmpty()) foreach (Cum cum in cums)
                    {
                        if (cum.notcum && cum.pawn.Equals(pawn) && cum.notcumLabel.Equals(notcumlabel))
                        {
                            cum.MergeWithFluid(volume, decayresist, filthdef);
                            merged = true;
                        }
                        cum.DismishForce(cumoutrate);
                    }
                if (!merged) cums.Add(new Cum(pawn, volume * (1 - cumoutrate), notcumlabel, decayresist, filthdef));
            }
            else
            {

                bool merged = false;
                if (!cums.NullOrEmpty()) foreach (Cum cum in cums)
                    {
                        if (cum.notcum && cum.pawn.Equals(pawn) && cum.notcumLabel.Equals(notcumlabel))
                        {
                            cum.MergeWithFluid(volume, decayresist, filthdef);
                            merged = true;
                        }
                    }
                if (!merged) cums.Add(new Cum(pawn, volume, notcumlabel, decayresist, filthdef));
            }
            cumd = TotalCumPercent - cumd;
            AfterNotCumIn();
            AfterFluidIn(cumd);
        }

        protected virtual void AfterCumIn(Pawn cummer)
        {
            ThoughtCumInside(cummer);
            
            if (Configurations.EnableCreampieMessages)
            {
                Messages.Message($"{cummer.Name} came inside {parent.pawn.Name}", parent.pawn, MessageTypeDefOf.NeutralEvent);
            }
        }

        protected virtual void AfterNotCumIn()
        {

        }

        /// <summary>
        /// Action for both Cum and NotCum
        /// </summary>
        /// <param name="fd">Fluid deviation</param>
        protected virtual void AfterFluidIn(float fd)
        {


        }


        protected void BeforeCumOut(out Absorber absorber)
        {
            Hediff asa = parent.pawn.health.hediffSet.GetFirstHediffOfDef(VariousDefOf.Hediff_ASA);
            float asafactor = asa?.Severity ?? 0f;

            if (parent.pawn.health.hediffSet.HasHediff(VariousDefOf.RJW_IUD)) antisperm = 0.70f + asafactor;
            else antisperm = 0.0f + asafactor;

            absorber = (Absorber)parent.pawn.apparel?.WornApparel?.Find(x => x is Absorber);
            if (absorber != null)
            {
                absorber.WearEffect();
                if (absorber.dirty && absorber.EffectAfterDirty) absorber.DirtyEffect();
            }
        }

        /// <summary>
        /// For natural leaking
        /// </summary>
        protected virtual void AfterCumOut()
        {
            parent.pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(VariousDefOf.LeakingFluids);
        }

        /// <summary>
        /// For all type of leaking
        /// </summary>
        /// <param name="fd"></param>
        protected virtual void AfterFluidOut(float fd)
        {

        }




        /// <summary>
        /// Excrete cums in womb naturally
        /// </summary>
        public void CumOut()
        {
            float leakfactor = 1.0f;
            float totalleak = 0f;
            float cumd = TotalCumPercent;
            List<string> filthlabels = new List<string>();
            BeforeCumOut(out Absorber absorber);
            if (cums.NullOrEmpty()) return;
            else if (absorber != null && absorber.dirty && !absorber.LeakAfterDirty) leakfactor = 0f;
            List<Cum> removecums = new List<Cum>();
            foreach (Cum cum in cums)
            {
                cum.CumEffects(parent.pawn);
                float vd = cum.DismishNatural(leakfactor, antisperm);
                cum.MakeThinner(Configurations.CycleAcceleration);
                totalleak += AbsorbCum(cum, vd, absorber);
                string tmp = "FilthLabelWithSource".Translate(cum.FilthDef.label, cum.pawn?.LabelShort ?? "Unknown", 1.ToString());
                filthlabels.Add(tmp.Replace(" x1", ""));
                if (cum.ShouldRemove()) removecums.Add(cum);
            }
            if (cums.Count > 1) MakeCumFilthMixture(totalleak, filthlabels);
            else if (cums.Count == 1) MakeCumFilth(cums.First(), totalleak);
            foreach (Cum cum in removecums)
            {
                cums.Remove(cum);
            }
            removecums.Clear();
            cumd = TotalCumPercent - cumd;
            if (totalleak >= 1.0f) AfterCumOut();
            AfterFluidOut(cumd);

        }

        /// <summary>
        /// Force excrete cums in womb and get excreted amount of specific cum.
        /// </summary>
        /// <param name="targetcum"></param>
        /// <param name="portion"></param>
        /// <returns>Amount of target cum</returns>
        public float CumOut(Cum targetcum, float portion = 0.1f)
        {
            if (cums.NullOrEmpty()) return 0;
            float totalleak = 0;
            List<string> filthlabels = new List<string>();
            float outcum = 0;
            float cumd = TotalCumPercent;
            List<Cum> removecums = new List<Cum>();
            foreach (Cum cum in cums)
            {
                float vd = cum.DismishForce(portion);
                if (cum.Equals(targetcum)) outcum = vd;
                //MakeCumFilth(cum, vd - cum.volume);
                string tmp = "FilthLabelWithSource".Translate(cum.FilthDef.label, cum.pawn?.LabelShort ?? "Unknown", 1.ToString());
                filthlabels.Add(tmp.Replace(" x1", ""));
                totalleak += vd;
                if (cum.ShouldRemove()) removecums.Add(cum);
            }
            if (cums.Count > 1) MakeCumFilthMixture(totalleak, filthlabels);
            else if (cums.Count == 1) MakeCumFilth(cums.First(), totalleak);
            foreach (Cum cum in removecums)
            {
                cums.Remove(cum);
            }
            removecums.Clear();
            cumd = TotalCumPercent - cumd;
            AfterFluidOut(cumd);
            return outcum;
        }

        /// <summary>
        /// Force excrete cums in womb and get mixture of cum.
        /// </summary>
        /// <param name="mixtureDef"></param>
        /// <param name="portion"></param>
        /// <returns></returns>
        public CumMixture MixtureOut(ThingDef mixtureDef ,float portion = 0.1f)
        {
            if (cums.NullOrEmpty()) return null;
            Color color = GetCumMixtureColor;
            float totalleak = 0;
            List<string> cumlabels = new List<string>();
            float cumd = TotalCumPercent;
            List<Cum> removecums = new List<Cum>();
            bool pure = true;
            foreach (Cum cum in cums)
            {
                float vd = cum.DismishForce(portion);
                string tmp = "FilthLabelWithSource".Translate(cum.FilthDef.label, cum.pawn?.LabelShort ?? "Unknown", 1.ToString());
                cumlabels.Add(tmp.Replace(" x1", ""));
                totalleak += vd;
                if (cum.ShouldRemove()) removecums.Add(cum);
                if (cum.notcum) pure = false;
            }
            foreach (Cum cum in removecums)
            {
                cums.Remove(cum);
            }
            removecums.Clear();
            return new CumMixture(parent.pawn, totalleak, cumlabels, color, mixtureDef, pure);
        }

        
        /// <summary>
        /// Fertilize eggs and return the result
        /// </summary>
        /// <returns></returns>
        protected bool FertilizationCheck()
        {
            if (!eggs.NullOrEmpty())
            {
                bool onefertilized = false;
                foreach (Egg egg in eggs)
                {
                    if (!egg.fertilized) egg.fertilizer = Fertilize();
                    if (egg.fertilizer != null)
                    {
                        egg.fertilized = true;
                        egg.lifespanhrs += 240;
                        onefertilized = true;
                    }
                }
                return onefertilized;
            }
            else return false;
        }

        public void Initialize()
        {
            Props = (CompProperties_Menstruation)props;

            if (!Props.infertile)
            {
                if (follicularIntervalhours < 0)
                {
                    follicularIntervalhours = PeriodRandomizer(Props.folicularIntervalDays * 24, Props.deviationFactor);
                    curStage = RandomStage();
                }

                if (lutealIntervalhours < 0) lutealIntervalhours = PeriodRandomizer(Props.lutealIntervalDays * 24, Props.deviationFactor);
                if (bleedingIntervalhours < 0) bleedingIntervalhours = PeriodRandomizer(Props.bleedingIntervalDays * 24, Props.deviationFactor);
                if (recoveryIntervalhours < 0) recoveryIntervalhours = PeriodRandomizer(Props.recoveryIntervalDays * 24, Props.deviationFactor);
                if (crampPain < 0) crampPain = PainRandomizer();
                if (cums == null) cums = new List<Cum>();
                if (eggs == null) eggs = new List<Egg>();


                InitOvary(parent.pawn.ageTracker.AgeBiologicalYears);

                Hediff_BasePregnancy pregnancy = parent.pawn.GetRJWPregnancy();
                if (pregnancy != null)
                {
                    Hediff hediff = PregnancyHelper.GetPregnancy(parent.pawn);
                    if (hediff != null)
                    {
                        if (hediff is Hediff_BasePregnancy)
                        {
                            Hediff_BasePregnancy preg = (Hediff_BasePregnancy)hediff;
                            currentIntervalhours = (int)(preg.GestationHours());
                            curStage = Stage.Pregnant;
                        }
                    }
                }

                if (parent.pawn.IsAnimal())
                {
                    if (Configurations.EnableAnimalCycle)
                    {
                        HugsLibController.Instance.TickDelayScheduler.ScheduleCallback(PeriodSimulator(curStage), tickInterval, parent.pawn, false);
                    }
                }
                else
                {
                    if (pregnancy == null && parent.pawn.health.capacities.GetLevel(xxx.reproduction) <= 0) HugsLibController.Instance.TickDelayScheduler.ScheduleCallback(PeriodSimulator(Stage.Young), tickInterval, parent.pawn, false);
                    else HugsLibController.Instance.TickDelayScheduler.ScheduleCallback(PeriodSimulator(curStage), tickInterval, parent.pawn, false);
                }
            }
            else
            {
                if (cums == null) cums = new List<Cum>();
                curStage = Stage.None;
                HugsLibController.Instance.TickDelayScheduler.ScheduleCallback(PeriodSimulator(curStage), tickInterval, parent.pawn, false);
            }
            //Log.Message(parent.pawn.Label + " - Initialized menstruation comp");
            loaded = true;
        }

        protected void InitOvary(int ageYear)
        {
            if (!Configurations.EnableMenopause)
            {
                RemoveClimactericEffect();
            }
            else if (ovarypower < -50000)
            {
                if (Props.ovaryPower > 10000000) ovarypower = Props.ovaryPower;
                else
                {
                    float avglittersize;
                    try
                    {
                        avglittersize = Rand.ByCurveAverage(parent.pawn.def.race.litterSizeCurve);
                    }
                    catch (NullReferenceException)
                    {
                        avglittersize = 1;
                    }

                    //Old one. Sex minimum age based.
                    //ovarypower = (int)(((Props.ovaryPower * Utility.RandGaussianLike(0.70f, 1.30f) * parent.pawn.def.race.lifeExpectancy / ThingDefOf.Human.race.lifeExpectancy)
                    //    - (Math.Max(0, ageYear - RJWSettings.sex_minimum_age * parent.pawn.def.race.lifeExpectancy / ThingDefOf.Human.race.lifeExpectancy))
                    //    * (60 / (Props.folicularIntervalDays + Props.lutealIntervalDays) * Configurations.CycleAcceleration)) * avglittersize);

                    //New one. 
                    float fertendage, lifenormalized;
                    if (parent.pawn.IsAnimal()) fertendage = RJWPregnancySettings.fertility_endage_female_animal * 100f;
                    else fertendage = RJWPregnancySettings.fertility_endage_female_humanlike * 80f;
                    lifenormalized = parent.pawn.def.race.lifeExpectancy / ThingDefOf.Human.race.lifeExpectancy;
                    fertendage *= lifenormalized;
                    ovarypower = (int)((fertendage - parent.pawn.ageTracker.AgeBiologicalYearsFloat) * (60f / (Props.folicularIntervalDays + Props.lutealIntervalDays) * Configurations.CycleAcceleration) * avglittersize);
                    ovarypower = (int)Mathf.Max(0, Mathf.Min(Props.ovaryPower * Utility.RandGaussianLike(0.70f,1.30f,5) * lifenormalized,ovarypower));

                    if (ovarypower < 1)
                    {
                        Hediff hediff = HediffMaker.MakeHediff(VariousDefOf.Hediff_Menopause, parent.pawn);
                        hediff.Severity = 0.2f;
                        parent.pawn.health.AddHediff(hediff, Genital_Helper.get_genitalsBPR(parent.pawn));
                        curStage = Stage.Young;
                    }
                    else if (ovarypower < ovarypowerthreshold)
                    {
                        Hediff hediff = HediffMaker.MakeHediff(VariousDefOf.Hediff_Climacteric, parent.pawn);
                        hediff.Severity = 0.008f * (ovarypowerthreshold - ovarypower);
                        parent.pawn.health.AddHediff(hediff, Genital_Helper.get_genitalsBPR(parent.pawn));
                    }
                }
            }
        }

        public void RecoverOvary(float multiply = 1.2f)
        {
            ovarypower = Math.Max(0, (int)(ovarypower * multiply));
            if (ovarypower >= ovarypowerthreshold)
            {
                RemoveClimactericEffect();
            }
        }


        protected void AfterSimulator()
        {
            if (Configurations.EnableMenopause && ovarypower < ovarypowerthreshold)
            {
                if (sexNeed == null) sexNeed = parent.pawn.needs.TryGetNeed(VariousDefOf.SexNeed);
                else
                {
                    if (sexNeed.CurLevel < 0.5) sexNeed.CurLevel += 0.01f;
                }
            }
        }

        public void SetEstrus(int days)
        {
            HediffDef estrusdef;
            if (Props.consealedEstrus) estrusdef = VariousDefOf.Hediff_Estrus_Consealed;
            else estrusdef = VariousDefOf.Hediff_Estrus;

            HediffWithComps hediff = (HediffWithComps)parent.pawn.health.hediffSet.GetFirstHediffOfDef(estrusdef);
            if (hediff != null)
            {
                hediff.Severity = (float)days / Configurations.CycleAcceleration + 0.2f;
            }
            else
            {
                hediff = (HediffWithComps)HediffMaker.MakeHediff(estrusdef, parent.pawn);
                hediff.Severity = (float)days / Configurations.CycleAcceleration + 0.2f;
                parent.pawn.health.AddHediff(hediff);
            }
        }

        public bool IsBreedingSeason()
        {
            if (Props.breedingSeason == SeasonalBreed.Always) return true;
            switch (GenLocalDate.Season(parent.pawn.Map))
            {
                case Season.Spring:
                    if ((Props.breedingSeason & SeasonalBreed.Spring) != 0) return true;
                    break;
                case Season.Summer:
                case Season.PermanentSummer:
                    if ((Props.breedingSeason & SeasonalBreed.Summer) != 0) return true;
                    break;
                case Season.Fall:
                    if ((Props.breedingSeason & SeasonalBreed.Fall) != 0) return true;
                    break;
                case Season.Winter:
                case Season.PermanentWinter:
                    if ((Props.breedingSeason & SeasonalBreed.Winter) != 0) return true;
                    break;
                default:
                    return false;
            }
            return false;
        }

        protected Pawn Fertilize()
        {
            if (cums.NullOrEmpty()) return null;
            foreach (Cum cum in cums)
            {
                float rand = Rand.Range(0.0f, 1.0f);
                if (cum.pawn != null && !cum.notcum && rand < cum.FertVolume * cum.fertFactor * Configurations.FertilizeChance * Props.basefertilizationChanceFactor)
                {
                    if (!RJWPregnancySettings.bestial_pregnancy_enabled && (xxx.is_animal(parent.pawn) ^ xxx.is_animal(cum.pawn))) continue;
                    parent.pawn.records.AddTo(VariousDefOf.AmountofFertilizedEggs, 1);
                    return cum.pawn;
                }
            }
            return null;
        }

        
        protected bool Implant()
        {
            if (!eggs.NullOrEmpty())
            {
                List<Egg> deadeggs = new List<Egg>();
                bool pregnant = false;
                foreach (Egg egg in eggs)
                {
                    if (!egg.fertilized || egg.fertstage < 168) continue;
                    else if (Rand.Range(0.0f, 1.0f) <= Configurations.ImplantationChance * Props.baseImplantationChanceFactor * ImplantFactor * InterspeciesImplantFactor(egg.fertilizer))
                    {
                        Hediff_BasePregnancy pregnancy = parent.pawn.GetRJWPregnancy();
                        if (pregnancy != null)
                        {
                            if (Configurations.UseMultiplePregnancy && Configurations.EnableHeteroOvularTwins)
                            {
                                if (pregnancy is Hediff_MultiplePregnancy)
                                {
                                    Hediff_MultiplePregnancy h = (Hediff_MultiplePregnancy)pregnancy;
                                    h.AddNewBaby(parent.pawn, egg.fertilizer);
                                }
                                pregnant = true;
                                deadeggs.Add(egg);
                            }
                            else
                            {
                                pregnant = true;
                                break;
                            }


                        }
                        else
                        {
                            if (!Configurations.UseMultiplePregnancy)
                            {
                                PregnancyHelper.PregnancyDecider(parent.pawn, egg.fertilizer);
                                Hediff_BasePregnancy hediff = (Hediff_BasePregnancy)PregnancyHelper.GetPregnancy(parent.pawn);
                                currentIntervalhours = (int)hediff?.GestationHours();
                                pregnant = true;
                                break;
                            }
                            else
                            {
                                Hediff_BasePregnancy.Create<Hediff_MultiplePregnancy>(parent.pawn, egg.fertilizer);
                                Hediff_BasePregnancy hediff = (Hediff_BasePregnancy)PregnancyHelper.GetPregnancy(parent.pawn);
                                currentIntervalhours = (int)hediff?.GestationHours();

                                pregnant = true;
                                deadeggs.Add(egg);
                            }
                        }

                    }
                    else deadeggs.Add(egg);
                }

                if (pregnant && (!Configurations.UseMultiplePregnancy || !Configurations.EnableHeteroOvularTwins))
                {
                    eggs.Clear();
                    deadeggs.Clear();
                    return true;
                }
                else if (!deadeggs.NullOrEmpty())
                {
                    foreach (Egg egg in deadeggs)
                    {
                        eggs.Remove(egg);
                    }
                    deadeggs.Clear();
                }
                if (pregnant) return true;
            }
            return false;
        }

        protected void BleedOut()
        {
            //FilthMaker.TryMakeFilth(parent.pawn.Position, parent.pawn.Map, ThingDefOf.Filth_Blood,parent.pawn.Label);
            CumIn(parent.pawn, Rand.Range(0.02f * Configurations.BleedingAmount, 0.04f * Configurations.BleedingAmount), Translations.Menstrual_Blood, -5.0f, parent.pawn.def.race?.BloodDef ?? ThingDefOf.Filth_Blood);
            GetNotCum(Translations.Menstrual_Blood).color = BloodColor;
        }

        /// <summary>
        /// Make filth ignoring absorber
        /// </summary>
        /// <param name="cum"></param>
        /// <param name="amount"></param>
        protected void MakeCumFilth(Cum cum, float amount)
        {
            if (amount >= minmakefilthvalue) FilthMaker.TryMakeFilth(parent.pawn.Position, parent.pawn.Map, cum.FilthDef, cum.pawn?.LabelShort ?? "Unknown");
        }

        /// <summary>
        /// Absorb cum and return leaked amount
        /// </summary>
        /// <param name="cum"></param>
        /// <param name="amount"></param>
        /// <param name="absorber"></param>
        /// <returns></returns>
        protected float AbsorbCum(Cum cum, float amount, Absorber absorber)
        {

            if (absorber != null)
            {
                float absorbable = absorber.GetStatValue(VariousDefOf.MaxAbsorbable);
                absorber.SetColor(Colors.CMYKLerp(GetCumMixtureColor, absorber.DrawColor, 1f - amount / absorbable));
                if (!absorber.dirty)
                {
                    absorber.absorbedfluids += amount;
                    if (absorber.absorbedfluids > absorbable)
                    {
                        absorber.def = absorber.DirtyDef;
                        //absorber.fluidColor = GetCumMixtureColor;
                        absorber.dirty = true;
                    }
                }
                else
                {

                    //if (absorber.LeakAfterDirty) FilthMaker.TryMakeFilth(parent.pawn.Position, parent.pawn.Map, cum.FilthDef, cum.pawn.LabelShort);
                    return amount;
                }
            }
            else
            {
                //if (amount >= minmakefilthvalue) FilthMaker.TryMakeFilth(parent.pawn.Position, parent.pawn.Map, cum.FilthDef, cum.pawn.LabelShort);
                return amount;
            }
            return 0;
        }

        protected float MakeCumFilthMixture(float amount, List<string> cumlabels)
        {

            if (amount >= minmakefilthvalue)
            {
                FilthMaker_Colored.TryMakeFilth(parent.pawn.Position, parent.pawn.Map, VariousDefOf.FilthMixture, cumlabels, GetCumMixtureColor, false);
            }
            return amount;
        }




        protected void EggDecay()
        {
            List<Egg> deadeggs = new List<Egg>();
            foreach (Egg egg in eggs)
            {
                egg.lifespanhrs -= Configurations.CycleAcceleration;
                egg.position += Configurations.CycleAcceleration;
                if (egg.lifespanhrs < 0) deadeggs.Add(egg);
                if (egg.fertilized) egg.fertstage += Configurations.CycleAcceleration;
            }
            if (!deadeggs.NullOrEmpty())
            {
                foreach (Egg egg in deadeggs)
                {
                    eggs.Remove(egg);
                }
                deadeggs.Clear();
            }
        }

        protected void AddCrampPain()
        {
            Hediff hediff = HediffMaker.MakeHediff(VariousDefOf.Hediff_MenstrualCramp, parent.pawn);
            hediff.Severity = crampPain * Rand.Range(0.9f, 1.1f);
            HediffCompProperties_SeverityPerDay Prop = (HediffCompProperties_SeverityPerDay)hediff.TryGetComp<HediffComp_SeverityPerDay>().props;
            Prop.severityPerDay = -hediff.Severity / (bleedingIntervalhours / 24) * Configurations.CycleAcceleration;
            parent.pawn.health.AddHediff(hediff, Genital_Helper.get_genitalsBPR(parent.pawn));
        }

        protected virtual void FollicularAction()
        {
            if (!IsBreedingSeason())
            {
                GoNextStage(Stage.Anestrus);
                return;
            }
            if (curStageHrs >= FollicularIntervalHours)
            {
                GoNextStage(Stage.Ovulatory);
            }
            else
            {
                curStageHrs += Configurations.CycleAcceleration;
                if (!estrusflag && curStageHrs > FollicularIntervalHours - Props.estrusDaysBeforeOvulation * 24)
                {
                    estrusflag = true;
                    SetEstrus(Props.eggLifespanDays + Props.estrusDaysBeforeOvulation);
                }
                StayCurrentStage();
            }
        }

        protected virtual void OvulatoryAction()
        {
            estrusflag = false;
            int i = 0;
            float eggnum;
            try
            {
                eggnum = Rand.ByCurve(parent.pawn.RaceProps.litterSizeCurve) + eggstack;
            }
            catch(NullReferenceException)
            {
                eggnum = 1 + eggstack;
            }

            do
            {
                ovarypower--;
                eggs.Add(new Egg((int)(Props.eggLifespanDays * 24 / CycleFactor)));
                i++;
            } while (i < (int)eggnum);
            eggstack = 0;
            if (Configurations.EnableMenopause && ovarypower < 1)
            {
                eggs.Clear();
                Hediff hediff = parent.pawn.health.hediffSet.GetFirstHediffOfDef(VariousDefOf.Hediff_Climacteric);
                if (hediff != null) parent.pawn.health.RemoveHediff(hediff);
                hediff = HediffMaker.MakeHediff(VariousDefOf.Hediff_Menopause, parent.pawn);
                hediff.Severity = 0.2f;
                parent.pawn.health.AddHediff(hediff, Genital_Helper.get_genitalsBPR(parent.pawn));
                ovarypower = 0;
                GoNextStage(Stage.Young);
            }
            else if (Configurations.EnableMenopause && ovarypower < ovarypowerthreshold)
            {
                Hediff hediff = HediffMaker.MakeHediff(VariousDefOf.Hediff_Climacteric, parent.pawn);
                hediff.Severity = 0.008f * i;
                parent.pawn.health.AddHediff(hediff, Genital_Helper.get_genitalsBPR(parent.pawn));
                lutealIntervalhours = PeriodRandomizer(lutealIntervalhours, Props.deviationFactor * 6);
                GoNextStage(Stage.ClimactericLuteal);
            }
            else
            {
                lutealIntervalhours = PeriodRandomizer(lutealIntervalhours, Props.deviationFactor);
                GoNextStage(Stage.Luteal);
            }
        }

        protected virtual void LutealAction()
        {
            if (!eggs.NullOrEmpty())
            {
                FertilizationCheck();
                EggDecay();
                if (Implant())
                {
                    if (Breast != null)
                    {
                        Breast.PregnancyTransition();
                    }
                    GoNextStage(Stage.Pregnant);
                }
                else
                {
                    curStageHrs += Configurations.CycleAcceleration;
                    StayCurrentStage();
                }
            }
            else if (curStageHrs <= lutealIntervalhours)
            {
                curStageHrs += Configurations.CycleAcceleration;
                StayCurrentStage();
            }
            else
            {
                eggs.Clear();
                if (Props.bleedingIntervalDays == 0)
                {
                    follicularIntervalhours = PeriodRandomizer(follicularIntervalhours, Props.deviationFactor);
                    GoNextStage(Stage.Follicular);
                }
                else
                {
                    bleedingIntervalhours = PeriodRandomizer(bleedingIntervalhours, Props.deviationFactor);
                    if (crampPain >= 0.05f)
                    {
                        AddCrampPain();
                    }
                    GoNextStage(Stage.Bleeding);
                }
            }
        }

        protected virtual void BleedingAction()
        {
            if (curStageHrs >= bleedingIntervalhours)
            {
                follicularIntervalhours = PeriodRandomizer(follicularIntervalhours, Props.deviationFactor);
                Hediff hediff = parent.pawn.health.hediffSet.GetFirstHediffOfDef(VariousDefOf.Hediff_MenstrualCramp);
                if (hediff != null) parent.pawn.health.RemoveHediff(hediff);
                GoNextStage(Stage.Follicular);
            }
            else
            {
                if (curStageHrs < bleedingIntervalhours / 4) for (int i = 0; i < Configurations.CycleAcceleration; i++) BleedOut();
                curStageHrs += Configurations.CycleAcceleration;
                StayCurrentStage();
            }
        }

        protected virtual void PregnantAction()
        {
            if (!eggs.NullOrEmpty())
            {
                FertilizationCheck();
                EggDecay();
                Implant();
            }

            if (parent.pawn.GetRJWPregnancy() != null)
            {
                curStageHrs += 1;
                StayCurrentStageConst(Stage.Pregnant);
            }
            else
            {
                if (Breast != null)
                {
                    Breast.BirthTransition();
                }
                GoNextStage(Stage.Recover);
            } 
        }

        protected virtual void YoungAction()
        {
            if (!Configurations.EnableMenopause && ovarypower < 0 && ovarypower > -10000)
            {
                RemoveClimactericEffect();
            }
            if (parent.pawn.health.capacities.GetLevel(xxx.reproduction) <= 0)
            {
                StayCurrentStageConst(Stage.Young);
            }
            else GoNextStage(Stage.Follicular);
        }

        protected virtual void AnestrusAction()
        {
            if (IsBreedingSeason())
            {
                GoFollicularOrBleeding();
            }
            else
            {
                StayCurrentStage();
            }
        }

        protected virtual void ThoughtCumInside(Pawn cummer)
        {
            if (xxx.is_human(parent.pawn) && xxx.is_human(cummer))
            {
                if (parent.pawn.GetStatValue(StatDefOf.PawnBeauty) >= 0 || cummer.Has(Quirk.ImpregnationFetish) || cummer.Has(Quirk.Breeder))
                {
                    if (cummer.relations.OpinionOf(parent.pawn) <= -25)
                    {
                        cummer.needs.mood.thoughts.memories.TryGainMemory(VariousDefOf.HaterCameInsideM, parent.pawn);
                    }
                    else
                    {
                        cummer.needs.mood.thoughts.memories.TryGainMemory(VariousDefOf.CameInsideM, parent.pawn);
                    }
                }

                if (IsDangerDay)
                {
                    if (parent.pawn.Has(Quirk.Breeder) || parent.pawn.Has(Quirk.ImpregnationFetish))
                    {
                        parent.pawn.needs.mood.thoughts.memories.TryGainMemory(VariousDefOf.CameInsideFFetish, cummer);
                    }
                    else if (!parent.pawn.relations.DirectRelationExists(PawnRelationDefOf.Spouse, cummer) && !parent.pawn.relations.DirectRelationExists(PawnRelationDefOf.Fiance, cummer))
                    {
                        if (parent.pawn.health.capacities.GetLevel(xxx.reproduction) < 0.50f) parent.pawn.needs.mood.thoughts.memories.TryGainMemory(VariousDefOf.CameInsideFLowFert, cummer);
                        else parent.pawn.needs.mood.thoughts.memories.TryGainMemory(VariousDefOf.CameInsideF, cummer);
                    }
                    else if (parent.pawn.relations.OpinionOf(cummer) <= -5)
                    {
                        parent.pawn.needs.mood.thoughts.memories.TryGainMemory(VariousDefOf.HaterCameInsideF, cummer);
                    }
                }
                else
                {
                    if (parent.pawn.Has(Quirk.Breeder) || parent.pawn.Has(Quirk.ImpregnationFetish))
                    {
                        parent.pawn.needs.mood.thoughts.memories.TryGainMemory(VariousDefOf.CameInsideFFetishSafe, cummer);
                    }
                    else if (parent.pawn.relations.OpinionOf(cummer) <= -5)
                    {
                        parent.pawn.needs.mood.thoughts.memories.TryGainMemory(VariousDefOf.HaterCameInsideFSafe, cummer);
                    }
                }
            }
        }



        private Action PeriodSimulator(Stage targetstage)
        {
            Action action = null;
            switch (targetstage)
            {
                case Stage.Follicular:
                    action = FollicularAction;
                    break;
                case Stage.Ovulatory:
                    action = OvulatoryAction;
                    break;
                case Stage.Luteal:
                    action = LutealAction;
                    break;
                case Stage.Bleeding:
                    action = BleedingAction;
                    break;
                case Stage.Fertilized:  //Obsoleted stage. merged in luteal stage
                    action = delegate
                    {
                        ModLog.Message("Obsoleted stage. skipping...");
                        GoNextStage(Stage.Luteal);
                    };
                    break;
                case Stage.Pregnant:
                    action = PregnantAction;
                    break;
                case Stage.Recover:
                    action = delegate
                    {
                        if (curStageHrs >= recoveryIntervalhours)
                        {
                            if (Configurations.EnableMenopause && ovarypower < ovarypowerthreshold)
                            {
                                GoNextStage(Stage.ClimactericFollicular);
                            }
                            else if (parent.pawn.health.capacities.GetLevel(xxx.reproduction) == 0)
                            {
                                GoNextStage(Stage.Young);
                            }
                            else
                            {
                                follicularIntervalhours = PeriodRandomizer(follicularIntervalhours, Props.deviationFactor);
                                GoNextStage(Stage.Follicular);
                            }
                        }
                        else
                        {
                            curStageHrs += Configurations.CycleAcceleration;
                            StayCurrentStage();
                        }
                    };
                    break;
                case Stage.None:
                    action = delegate
                    {
                        StayCurrentStageConst(Stage.None);
                    };
                    break;
                case Stage.Young:
                    action = YoungAction;
                    break;
                case Stage.ClimactericFollicular:
                    action = delegate
                    {
                        if (!Configurations.EnableMenopause)
                        {
                            RemoveClimactericEffect();
                            StayCurrentStage();
                        }
                        else if (curStageHrs >= (follicularIntervalhours - bleedingIntervalhours) * CycleFactor)
                        {
                            GoNextStage(Stage.Ovulatory);
                        }
                        else if (ovarypower < ovarypowerthreshold / 3 && Rand.Range(0.0f, 1.0f) < 0.2f) //skips ovulatory
                        {
                            follicularIntervalhours = PeriodRandomizer(follicularIntervalhours, Props.deviationFactor * 6);
                            GoNextStage(Stage.ClimactericFollicular);
                        }
                        else
                        {
                            curStageHrs += Configurations.CycleAcceleration;
                            StayCurrentStage();
                        }
                    };
                    break;
                case Stage.ClimactericLuteal:
                    action = delegate
                    {
                        if (!Configurations.EnableMenopause)
                        {
                            RemoveClimactericEffect();
                            StayCurrentStage();
                        }
                        else if (!eggs.NullOrEmpty())
                        {
                            FertilizationCheck();
                            EggDecay();
                            if (Implant()) GoNextStage(Stage.Pregnant);
                            else
                            {
                                curStageHrs += Configurations.CycleAcceleration;
                                StayCurrentStage();
                            }
                        }
                        else if (curStageHrs <= lutealIntervalhours)
                        {
                            curStageHrs += Configurations.CycleAcceleration;
                            StayCurrentStage();
                        }
                        else
                        {
                            eggs.Clear();
                            if (Props.bleedingIntervalDays == 0)
                            {
                                follicularIntervalhours = PeriodRandomizer(follicularIntervalhours, Props.deviationFactor * 6);
                                GoNextStage(Stage.ClimactericFollicular);
                            }
                            else if (ovarypower < ovarypowerthreshold / 4 || (ovarypower < ovarypowerthreshold / 3 && Rand.Range(0.0f, 1.0f) < 0.3f)) //skips bleeding
                            {
                                follicularIntervalhours = PeriodRandomizer(follicularIntervalhours, Props.deviationFactor * 6);
                                GoNextStage(Stage.ClimactericFollicular);
                            }
                            else
                            {
                                bleedingIntervalhours = PeriodRandomizer(bleedingIntervalhours, Props.deviationFactor);
                                if (crampPain >= 0.05f)
                                {
                                    AddCrampPain();
                                }
                                GoNextStage(Stage.ClimactericBleeding);
                            }
                        }

                    };
                    break;
                case Stage.ClimactericBleeding:
                    action = delegate
                    {
                        if (!Configurations.EnableMenopause)
                        {
                            RemoveClimactericEffect();
                            StayCurrentStage();
                        }
                        else if (curStageHrs >= bleedingIntervalhours)
                        {
                            follicularIntervalhours = PeriodRandomizer(follicularIntervalhours, Props.deviationFactor * 6);
                            GoNextStage(Stage.ClimactericFollicular);
                        }
                        else
                        {
                            if (curStageHrs < bleedingIntervalhours / 6) for (int i = 0; i < Configurations.CycleAcceleration; i++) BleedOut();
                            curStageHrs += Configurations.CycleAcceleration;
                            StayCurrentStage();
                        }
                    };
                    break;
                case Stage.Anestrus:
                    action = AnestrusAction;
                    break;
                default:
                    curStage = Stage.Follicular;
                    curStageHrs = 0;
                    if (follicularIntervalhours < 0) follicularIntervalhours = PeriodRandomizer(Props.folicularIntervalDays * 24, Props.deviationFactor);
                    HugsLibController.Instance.TickDelayScheduler.ScheduleCallback(PeriodSimulator(Stage.Follicular), tickInterval, parent.pawn, false);
                    break;
            }
            action += delegate
            {
                if (parent.pawn.health.capacities.GetLevel(xxx.reproduction) <= 0) curStage = Stage.Young;
                //CumOut();
                AfterSimulator();
            };
            action = CumOut + action;

            actionref = action;
            return actionref;

            


        }

        protected void GoNextStage(Stage nextstage, float factor = 1.0f)
        {
            curStageHrs = 0;
            curStage = nextstage;
            HugsLibController.Instance.TickDelayScheduler.ScheduleCallback(PeriodSimulator(nextstage), (int)(tickInterval * factor), parent.pawn, false);
        }


        protected void GoNextStageSetHour(Stage nextstage, int hour, float factor = 1.0f)
        {
            curStageHrs = hour;
            curStage = nextstage;
            HugsLibController.Instance.TickDelayScheduler.ScheduleCallback(PeriodSimulator(nextstage), (int)(tickInterval * factor), parent.pawn, false);
        }

        //stage can be interrupted in other reasons
        protected void StayCurrentStage(float factor = 1.0f)
        {
            HugsLibController.Instance.TickDelayScheduler.ScheduleCallback(PeriodSimulator(curStage), (int)(tickInterval * factor), parent.pawn, false);
        }

        //stage never changes
        protected void StayCurrentStageConst(Stage curstage, float factor = 1.0f)
        {
            HugsLibController.Instance.TickDelayScheduler.ScheduleCallback(PeriodSimulator(curstage), (int)(tickInterval * factor), parent.pawn, false);
        }

        protected void GoFollicularOrBleeding()
        {
            if (Props.bleedingIntervalDays == 0)
            {
                follicularIntervalhours = PeriodRandomizer(follicularIntervalhours, Props.deviationFactor);
                GoNextStage(Stage.Follicular);
            }
            else
            {
                bleedingIntervalhours = PeriodRandomizer(bleedingIntervalhours, Props.deviationFactor);
                GoNextStage(Stage.Bleeding);
            }
        }

        protected void RemoveClimactericEffect()
        {
            Hediff hediff = parent.pawn.health.hediffSet.GetFirstHediffOfDef(VariousDefOf.Hediff_Climacteric);
            if (hediff != null) parent.pawn.health.RemoveHediff(hediff);
            hediff = parent.pawn.health.hediffSet.GetFirstHediffOfDef(VariousDefOf.Hediff_Menopause);
            if (hediff != null) parent.pawn.health.RemoveHediff(hediff);
            if (curStage == Stage.ClimactericBleeding) curStage = Stage.Bleeding;
            else if (curStage == Stage.ClimactericFollicular) curStage = Stage.Follicular;
            else if (curStage == Stage.ClimactericLuteal) curStage = Stage.Luteal;
        }

        protected int PeriodRandomizer(int intervalhours, float deviation)
        {
            return intervalhours + (int)(intervalhours * Rand.Range(-deviation, deviation));
        }

        protected float InterspeciesImplantFactor(Pawn fertilizer)
        {
            if (fertilizer.def.defName == parent.pawn.def.defName) return 1.0f;
            else
            {
                if (RJWPregnancySettings.complex_interspecies) return SexUtility.BodySimilarity(parent.pawn, fertilizer);
                else return RJWPregnancySettings.interspecies_impregnation_modifier;
            }
        }

        protected float PainRandomizer()
        {
            float rand = Rand.Range(0.0f, 1.0f);
            if (rand < 0.01f) return Rand.Range(0.0f, 0.2f);
            else if (rand < 0.2f) return Rand.Range(0.1f, 0.2f);
            else if (rand < 0.8f) return Rand.Range(0.2f, 0.4f);
            else if (rand < 0.95f) return Rand.Range(0.4f, 0.6f);
            else return Rand.Range(0.6f, 1.0f);
        }

        protected Stage RandomStage()
        {
            int rand = Rand.Range(0, 2);

            switch (rand)
            {
                case 0:
                    curStageHrs = Rand.Range(0, (Props.folicularIntervalDays - Props.bleedingIntervalDays) * 24);
                    return Stage.Follicular;
                case 1:
                    curStageHrs = Rand.Range(0, Props.eggLifespanDays * 24);
                    return Stage.Luteal;
                case 2:
                    curStageHrs = Rand.Range(0, Props.bleedingIntervalDays * 24);
                    return Stage.Bleeding;
                default: return Stage.Follicular;
            }


        }





        public class Egg : IExposable
        {
            public bool fertilized;
            public int lifespanhrs;
            public Pawn fertilizer;
            public int position;
            public int fertstage = 0;

            public Egg()
            {
                fertilized = false;
                lifespanhrs = (int)(96 * Configurations.EggLifespanMultiplier);
                fertilizer = null;
                position = 0;
            }

            public Egg(int lifespanhrs)
            {
                fertilized = false;
                this.lifespanhrs = (int)(lifespanhrs * Configurations.EggLifespanMultiplier);
                fertilizer = null;
                position = 0;
            }

            public void ExposeData()
            {
                Scribe_References.Look(ref fertilizer, "fertilizer", true);
                Scribe_Values.Look(ref fertilized, "fertilized", fertilized, true);
                Scribe_Values.Look(ref lifespanhrs, "lifespanhrs", lifespanhrs, true);
                Scribe_Values.Look(ref position, "position", position, true);
                Scribe_Values.Look(ref fertstage, "fertstage", fertstage, true);
            }
        }


    }

    public class HediffComp_Anus : HediffComp
    {
        protected float? originanussize;

        public float OriginAnusSize
        {
            get
            {
                if (originanussize == null)
                {
                    originanussize = parent.Severity;
                }
                return originanussize ?? 0.1f;
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref originanussize, "originanussize", originanussize, true);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
        }
    }








}
