using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using CalamityPlaguebringerGoliath = CalamityMod.NPCs.PlaguebringerGoliath.PlaguebringerGoliath;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.PlaguebringerGoliath
{
    internal sealed class PlaguebringerGoliathIUMWAI : IUMWBossAI
    {
        public override int NPCType => ModContent.NPCType<CalamityPlaguebringerGoliath>();

        public override string BossName => "The Plaguebringer Goliath";

        public override float[] PhaseLifeRatios => new[] { 0.70f, 0.44f, 0.20f };

        public override int AttackCycleLength => 104;

        public override float MotionIntensity => 1.18f;

        public override Color DebugColor => new(176, 255, 76);
    }
}
