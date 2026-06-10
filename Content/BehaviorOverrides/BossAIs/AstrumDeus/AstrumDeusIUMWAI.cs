using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using CalamityAstrumDeus = CalamityMod.NPCs.AstrumDeus.AstrumDeusHead;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.AstrumDeus
{
    internal sealed class AstrumDeusIUMWAI : IUMWBossAI
    {
        public override int NPCType => ModContent.NPCType<CalamityAstrumDeus>();

        public override string BossName => "Astrum Deus";

        public override float[] PhaseLifeRatios => new[] { 0.74f, 0.48f, 0.20f };

        public override int AttackCycleLength => 112;

        public override float MotionIntensity => 0.88f;

        public override Color DebugColor => new(255, 118, 226);
    }
}
