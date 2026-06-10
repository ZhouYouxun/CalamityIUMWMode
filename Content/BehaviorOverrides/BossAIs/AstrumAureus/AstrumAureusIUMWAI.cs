using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using CalamityAstrumAureus = CalamityMod.NPCs.AstrumAureus.AstrumAureus;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.AstrumAureus
{
    internal sealed class AstrumAureusIUMWAI : IUMWBossAI
    {
        public override int NPCType => ModContent.NPCType<CalamityAstrumAureus>();

        public override string BossName => "Astrum Aureus";

        public override float[] PhaseLifeRatios => new[] { 0.72f, 0.46f, 0.18f };

        public override int AttackCycleLength => 126;

        public override float MotionIntensity => 0.9f;

        public override Color DebugColor => new(255, 196, 72);
    }
}
