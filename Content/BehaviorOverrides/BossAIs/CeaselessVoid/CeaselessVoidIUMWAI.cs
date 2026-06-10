using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using CalamityCeaselessVoid = CalamityMod.NPCs.CeaselessVoid.CeaselessVoid;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.CeaselessVoid
{
    internal sealed class CeaselessVoidIUMWAI : IUMWBossAI
    {
        public override int NPCType => ModContent.NPCType<CalamityCeaselessVoid>();

        public override string BossName => "Ceaseless Void";

        public override float[] PhaseLifeRatios => new[] { 0.75f, 0.52f, 0.30f, 0.14f };

        public override int AttackCycleLength => 150;

        public override float MotionIntensity => 0.82f;

        public override Color DebugColor => new(178, 132, 255);
    }
}
