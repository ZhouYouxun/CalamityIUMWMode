using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using CalamityCryogen = CalamityMod.NPCs.Cryogen.Cryogen;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Cryogen
{
    internal sealed class CryogenIUMWAI : IUMWBossAI
    {
        public override int NPCType => ModContent.NPCType<CalamityCryogen>();

        public override string BossName => "Cryogen";

        public override float[] PhaseLifeRatios => new[] { 0.90f, 0.70f, 0.50f, 0.30f, 0.12f };

        public override int AttackCycleLength => 118;

        public override float MotionIntensity => 0.84f;

        public override Color DebugColor => new(118, 226, 255);
    }
}
