using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using CalamityPerforatorHive = CalamityMod.NPCs.Perforator.PerforatorHive;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Perforators
{
    internal sealed class PerforatorsIUMWAI : IUMWBossAI
    {
        public override int NPCType => ModContent.NPCType<CalamityPerforatorHive>();

        public override string BossName => "The Perforators";

        public override float[] PhaseLifeRatios => new[] { 0.70f, 0.50f, 0.25f };

        public override int AttackCycleLength => 116;

        public override float MotionIntensity => 1.08f;

        public override Color DebugColor => new(255, 92, 104);
    }
}
