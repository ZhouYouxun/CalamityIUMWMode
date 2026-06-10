using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using CalamityHiveMind = CalamityMod.NPCs.HiveMind.HiveMind;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.HiveMind
{
    internal sealed class HiveMindIUMWAI : IUMWBossAI
    {
        public override int NPCType => ModContent.NPCType<CalamityHiveMind>();

        public override string BossName => "The Hive Mind";

        public override float[] PhaseLifeRatios => new[] { 0.68f, 0.42f, 0.16f };

        public override int AttackCycleLength => 128;

        public override float MotionIntensity => 1f;

        public override Color DebugColor => new(146, 255, 150);
    }
}
