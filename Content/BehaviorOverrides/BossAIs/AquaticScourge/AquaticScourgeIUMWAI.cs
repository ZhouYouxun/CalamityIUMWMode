using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using CalamityAquaticScourge = CalamityMod.NPCs.AquaticScourge.AquaticScourgeHead;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.AquaticScourge
{
    internal sealed class AquaticScourgeIUMWAI : IUMWBossAI
    {
        public override int NPCType => ModContent.NPCType<CalamityAquaticScourge>();

        public override string BossName => "Aquatic Scourge";

        public override float[] PhaseLifeRatios => new[] { 0.80f, 0.55f, 0.28f };

        public override int AttackCycleLength => 122;

        public override float MotionIntensity => 0.92f;

        public override Color DebugColor => new(78, 240, 185);
    }
}
