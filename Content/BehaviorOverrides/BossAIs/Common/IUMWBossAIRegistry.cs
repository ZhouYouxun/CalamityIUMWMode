using System.Collections.Generic;
using Terraria.ModLoader;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.AquaticScourge;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.AstrumAureus;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.AstrumDeus;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.CeaselessVoid;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.BStage2.Cryogen;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.HiveMind;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.OldDuke;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Perforators;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.PlaguebringerGoliath;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Polterghast;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Providence;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Signus;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.StormWeaver;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.WeaponAttacks;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Yharon;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.CalamitasClone;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.LeviathanAnahita;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Ravager;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Dragonfolly;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common
{
    internal static class IUMWBossAIRegistry
    {
        private static Dictionary<int, IUMWBossAI> aiByNPCType = new();

        public static void Load()
        {
            aiByNPCType = new Dictionary<int, IUMWBossAI>();

            Register(new YharonIUMWAI());
            Register(new OldDukeAI());
            Register(new PolterghastAI());
            Register(new StormWeaverAI());
            Register(new CeaselessVoidAI());
            Register(new SignusAI());
            Register(new ProvidenceAI());
            var astrumDeus = new AstrumDeusAI();
            Register(astrumDeus);
            aiByNPCType[ModContent.NPCType<CalamityMod.NPCs.AstrumDeus.AstrumDeusBody>()] = astrumDeus;
            aiByNPCType[ModContent.NPCType<CalamityMod.NPCs.AstrumDeus.AstrumDeusTail>()] = astrumDeus;
            Register(new PlaguebringerGoliathAI());
            Register(new AstrumAureusAI());
            Register(new CryogenIUMWAI());
            Register(new AquaticScourgeAI());
            Register(new HiveMindIUMWAI());
            Register(new PerforatorsIUMWAI());
            Register(new CalamitasCloneAI());
            var leviathanAnahita = new LeviathanAnahitaAI();
            Register(leviathanAnahita);
            if (ModContent.TryFind("CalamityMod/Anahita", out ModNPC anahita))
                aiByNPCType[anahita.Type] = leviathanAnahita;
            Register(new RavagerAI());
            Register(new DragonfollyAI());

            IUMWWeaponBossRegistry.Load();
        }

        public static void Unload()
        {
            IUMWWeaponBossRegistry.Unload();
            aiByNPCType = null;
        }

        public static bool TryGetAI(int npcType, out IUMWBossAI ai)
        {
            ai = null;
            return aiByNPCType?.TryGetValue(npcType, out ai) == true;
        }

        private static void Register(IUMWBossAI ai)
        {
            aiByNPCType[ai.NPCType] = ai;
        }
    }
}
