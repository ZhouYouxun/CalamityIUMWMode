using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using CalamityStormWeaver = CalamityMod.NPCs.StormWeaver.StormWeaverHead;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.StormWeaver
{
    internal sealed class StormWeaverIUMWAI : IUMWBossAI
    {
        public override int NPCType => ModContent.NPCType<CalamityStormWeaver>();

        public override string BossName => "Storm Weaver";

        public override float[] PhaseLifeRatios => new[] { 0.80f, 0.50f, 0.25f };

        public override int AttackCycleLength => 110;

        public override float MotionIntensity => 0.95f;

        public override Color DebugColor => new(146, 238, 255);
    }
}
