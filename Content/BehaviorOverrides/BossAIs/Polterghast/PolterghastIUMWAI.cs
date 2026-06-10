using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using CalamityPolterghast = CalamityMod.NPCs.Polterghast.Polterghast;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Polterghast
{
    internal sealed class PolterghastIUMWAI : IUMWBossAI
    {
        public override int NPCType => ModContent.NPCType<CalamityPolterghast>();

        public override string BossName => "Polterghast";

        public override float[] PhaseLifeRatios => new[] { 0.70f, 0.40f, 0.18f };

        public override int AttackCycleLength => 125;

        public override float MotionIntensity => 1.05f;

        public override Color DebugColor => new(116, 215, 255);
    }
}
