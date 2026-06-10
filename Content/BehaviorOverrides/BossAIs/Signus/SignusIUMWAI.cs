using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using CalamitySignus = CalamityMod.NPCs.Signus.Signus;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Signus
{
    internal sealed class SignusIUMWAI : IUMWBossAI
    {
        public override int NPCType => ModContent.NPCType<CalamitySignus>();

        public override string BossName => "Signus, Envoy of the Devourer";

        public override float[] PhaseLifeRatios => new[] { 0.78f, 0.50f, 0.24f };

        public override int AttackCycleLength => 96;

        public override float MotionIntensity => 1.22f;

        public override Color DebugColor => new(202, 122, 255);
    }
}
