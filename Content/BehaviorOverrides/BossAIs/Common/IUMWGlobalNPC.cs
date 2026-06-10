using CalamityIUMWMode.Core.Systems;
using Terraria;
using Terraria.ModLoader;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common
{
    public class IUMWGlobalNPC : GlobalNPC
    {
        public override bool InstancePerEntity => true;

        internal int CurrentPhase = 1;

        internal int AttackTimer;

        internal int TransitionTimer;

        internal IUMWAttackState AttackState;

        public override void SetDefaults(NPC npc)
        {
            CurrentPhase = 1;
            AttackTimer = 0;
            TransitionTimer = 0;
            AttackState = IUMWAttackState.MatrixHover;
        }

        public override void PostAI(NPC npc)
        {
            if (!IUMWWorldSystem.IUMWModeEnabled)
                return;

            if (!IUMWBossAIRegistry.TryGetAI(npc.type, out IUMWBossAI ai))
                return;

            ai.Update(npc, this);
            IUMWDebugSystem.Report(npc, ai, this);
        }
    }
}
