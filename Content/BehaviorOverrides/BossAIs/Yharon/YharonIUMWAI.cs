using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using CalamityYharon = CalamityMod.NPCs.Yharon.Yharon;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Yharon
{
    internal sealed class YharonIUMWAI : IUMWBossAI
    {
        public override int NPCType => ModContent.NPCType<CalamityYharon>();

        public override string BossName => "Yharon, Dragon of Rebirth";

        public override float[] PhaseLifeRatios => new[] { 0.82f, 0.55f, 0.28f, 0.12f };

        public override int AttackCycleLength => 132;

        public override float MotionIntensity => 1.2f;

        public override Color DebugColor => new(255, 166, 74);
    }
}
