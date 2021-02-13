﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimWorld;
using rjw;

namespace RJW_Menstruation
{
    public static class VariousDefOf
    {
        //public static readonly DNADef defaultDNA = new DNADef
        //{
        //    fetusTexPath = "Fetus/Fetus_Default",
        //    cumColor = new ColorInt(255, 255, 255, 255),
        //    cumThickness = 0
        //};

        public static readonly ThingDef CumFilth = DefDatabase<ThingDef>.GetNamed("FilthCum");
        public static readonly ThingDef Tampon = DefDatabase<ThingDef>.GetNamed("Absorber_Tampon");
        public static readonly ThingDef Tampon_Dirty = DefDatabase<ThingDef>.GetNamed("Absorber_Tampon_Dirty");
        public static readonly HediffDef RJW_IUD = DefDatabase<HediffDef>.GetNamed("RJW_IUD");
        public static readonly HediffDef Hediff_MenstrualCramp = DefDatabase<HediffDef>.GetNamed("Hediff_MenstrualCramp");
        public static readonly HediffDef Hediff_Climacteric = DefDatabase<HediffDef>.GetNamed("Hediff_Climacteric");
        public static readonly HediffDef Hediff_Menopause = DefDatabase<HediffDef>.GetNamed("Hediff_Menopause");
        public static readonly DNADef HumanDNA = DefDatabase<DNADef>.GetNamed("Human");
        public static readonly StatDef MaxAbsorbable = DefDatabase<StatDef>.GetNamed("MaxAbsorbable");
        public static readonly PawnRelationDef Relation_birthgiver = DefDatabase<PawnRelationDef>.AllDefs.FirstOrDefault(d => d.defName == "RJW_Sire");
        public static readonly PawnRelationDef Relation_spawn = DefDatabase<PawnRelationDef>.AllDefs.FirstOrDefault(d => d.defName == "RJW_Pup");
        public static readonly NeedDef SexNeed = DefDatabase<NeedDef>.GetNamed("Sex");
        public static readonly JobDef VaginaWashing = DefDatabase<JobDef>.GetNamed("VaginaWashing");

    }
}
