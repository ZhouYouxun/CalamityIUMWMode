using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using CalamityOldDuke = CalamityMod.NPCs.OldDuke.OldDuke;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.OldDuke
{
    internal sealed class OldDukeIUMWAI : IUMWBossAI
    {
        public override int NPCType => ModContent.NPCType<CalamityOldDuke>();

        public override string BossName => "The Old Duke";

        public override float[] PhaseLifeRatios => new[] { 0.76f, 0.48f, 0.22f };

        public override int AttackCycleLength => 118;

        public override float MotionIntensity => 1.15f;

        public override Color DebugColor => new(122, 255, 82);
    }
}
