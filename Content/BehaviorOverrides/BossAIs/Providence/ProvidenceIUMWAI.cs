using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using CalamityProvidence = CalamityMod.NPCs.Providence.Providence;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Providence
{
    internal sealed class ProvidenceIUMWAI : IUMWBossAI
    {
        public override int NPCType => ModContent.NPCType<CalamityProvidence>();

        public override string BossName => "Providence, the Profaned Goddess";

        public override float[] PhaseLifeRatios => new[] { 0.84f, 0.60f, 0.36f, 0.16f };

        public override int AttackCycleLength => 140;

        public override float MotionIntensity => 0.9f;

        public override Color DebugColor => new(255, 216, 112);
    }
}
