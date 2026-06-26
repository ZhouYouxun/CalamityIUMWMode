using System.Collections.Generic;
using Terraria.ModLoader;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.AquaticScourge;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.AstrumAureus;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.AstrumDeus;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.CeaselessVoid;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Cryogen;
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

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common
{
    internal static class IUMWBossAIRegistry
    {
        private static Dictionary<int, IUMWBossAI> aiByNPCType = new();

        public static void Load()
        {
            aiByNPCType = new Dictionary<int, IUMWBossAI>();

            Register(new YharonIUMWAI());
            Register(new OldDukeIUMWAI());
            Register(new PolterghastIUMWAI());
            Register(new StormWeaverIUMWAI());
            Register(new CeaselessVoidIUMWAI());
            Register(new SignusIUMWAI());
            Register(new ProvidenceIUMWAI());
            var astrumDeus = new AstrumDeusIUMWAI();
            Register(astrumDeus);
            aiByNPCType[ModContent.NPCType<CalamityMod.NPCs.AstrumDeus.AstrumDeusBody>()] = astrumDeus;
            aiByNPCType[ModContent.NPCType<CalamityMod.NPCs.AstrumDeus.AstrumDeusTail>()] = astrumDeus;
            Register(new PlaguebringerGoliathIUMWAI());
            Register(new AstrumAureusIUMWAI());
            Register(new CryogenIUMWAI());
            Register(new AquaticScourgeIUMWAI());
            Register(new HiveMindIUMWAI());
            Register(new PerforatorsIUMWAI());

            IUMWWeaponBossRegistry.Load();
            RegisterWeaponIfAbsent("CalamitasClone");
            RegisterWeaponIfAbsent("Leviathan");
            RegisterWeaponIfAbsent("Anahita");
            RegisterWeaponIfAbsent("RavagerBody");
            RegisterWeaponIfAbsent("Dragonfolly");
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

        private static void RegisterWeaponIfAbsent(string npcName)
        {
            if (!ModContent.TryFind("CalamityMod/" + npcName, out ModNPC npc))
                return;

            if (aiByNPCType.ContainsKey(npc.Type))
                return;

            if (IUMWWeaponBossRegistry.TryCreateAI(npcName, out IUMWBossAI ai))
                aiByNPCType[ai.NPCType] = ai;
        }
    }
}
