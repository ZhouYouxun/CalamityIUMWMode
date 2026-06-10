using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common
{
    internal abstract class IUMWBossAI
    {
        public abstract int NPCType { get; }

        public abstract string BossName { get; }

        public virtual float[] PhaseLifeRatios => new[] { 0.75f, 0.5f, 0.25f };

        public virtual int AttackCycleLength => 150;

        public virtual float MotionIntensity => 1f;

        public virtual Color DebugColor => new(88, 255, 211);

        public void Update(NPC npc, IUMWGlobalNPC data)
        {
            Player target = Main.player[npc.target];
            if (npc.target < 0 || npc.target >= Main.maxPlayers || !target.active || target.dead)
            {
                npc.TargetClosest();
                target = Main.player[npc.target];
            }

            int nextPhase = CalculatePhase(npc);
            if (data.CurrentPhase != nextPhase)
            {
                data.CurrentPhase = nextPhase;
                data.AttackTimer = 0;
                data.TransitionTimer = 45;
                data.AttackState = IUMWAttackState.PhaseShift;
                npc.netUpdate = true;
            }
            else
            {
                data.AttackTimer++;
            }

            if (data.TransitionTimer > 0)
            {
                data.TransitionTimer--;
                data.AttackState = IUMWAttackState.PhaseShift;
            }
            else
            {
                int cycleLength = Math.Max(60, AttackCycleLength - data.CurrentPhase * 12);
                int stateIndex = data.AttackTimer / cycleLength % 4;
                IUMWAttackState nextState = (IUMWAttackState)stateIndex;
                if (data.AttackState != nextState)
                {
                    data.AttackState = nextState;
                    npc.netUpdate = true;
                }
            }

            ApplyMotionOverlay(npc, target, data);
            AddDebugVisuals(npc, data);
        }

        public virtual string PhaseName(int phase) => phase switch
        {
            1 => "Matrix Warmup",
            2 => "Vector Split",
            3 => "Overclock",
            4 => "Critical Kernel",
            _ => "Terminal Loop"
        };

        public virtual string StateName(IUMWAttackState state) => state switch
        {
            IUMWAttackState.MatrixHover => "Matrix Hover",
            IUMWAttackState.VectorDash => "Vector Dash",
            IUMWAttackState.OrbitLock => "Orbit Lock",
            IUMWAttackState.PhasePressure => "Phase Pressure",
            IUMWAttackState.PhaseShift => "Phase Shift",
            _ => state.ToString()
        };

        private int CalculatePhase(NPC npc)
        {
            float lifeRatio = npc.lifeMax <= 0 ? 1f : npc.life / (float)npc.lifeMax;
            int phase = 1;

            foreach (float threshold in PhaseLifeRatios)
            {
                if (lifeRatio <= threshold)
                    phase++;
            }

            return phase;
        }

        private void ApplyMotionOverlay(NPC npc, Player target, IUMWGlobalNPC data)
        {
            if (!target.active || target.dead)
                return;

            float phaseFactor = 1f + data.CurrentPhase * 0.12f;
            float intensity = MotionIntensity * phaseFactor;
            Vector2 toTarget = target.Center - npc.Center;
            Vector2 directionToTarget = SafeNormalize(toTarget, Vector2.UnitY);
            Vector2 perpendicular = new(-directionToTarget.Y, directionToTarget.X);

            switch (data.AttackState)
            {
                case IUMWAttackState.MatrixHover:
                    Vector2 hoverPoint = target.Center + new Vector2((float)Math.Sin(Main.GlobalTimeWrappedHourly * 2.2f + npc.whoAmI) * 360f, -260f - data.CurrentPhase * 28f);
                    Vector2 hoverDirection = SafeNormalize(hoverPoint - npc.Center, Vector2.Zero);
                    npc.velocity = Vector2.Lerp(npc.velocity, npc.velocity + hoverDirection * (0.55f * intensity), 0.08f);
                    break;

                case IUMWAttackState.VectorDash:
                    int dashRate = Math.Max(42, 92 - data.CurrentPhase * 8);
                    if (data.AttackTimer % dashRate == 0)
                        npc.velocity += directionToTarget * (2.2f + data.CurrentPhase * 0.55f) * MotionIntensity;
                    break;

                case IUMWAttackState.OrbitLock:
                    npc.velocity += perpendicular * (0.15f + data.CurrentPhase * 0.025f) * MotionIntensity;
                    npc.velocity += directionToTarget * (toTarget.Length() > 520f ? 0.28f : -0.08f) * MotionIntensity;
                    break;

                case IUMWAttackState.PhasePressure:
                    npc.velocity += directionToTarget * (0.34f + data.CurrentPhase * 0.04f) * MotionIntensity;
                    npc.velocity *= 0.985f;
                    break;

                case IUMWAttackState.PhaseShift:
                    npc.velocity *= 0.94f;
                    npc.alpha = Math.Max(0, npc.alpha - 10);
                    break;
            }

            float maxSpeed = 28f + data.CurrentPhase * 3f;
            if (npc.velocity.Length() > maxSpeed)
                npc.velocity = SafeNormalize(npc.velocity, Vector2.Zero) * maxSpeed;
        }

        private void AddDebugVisuals(NPC npc, IUMWGlobalNPC data)
        {
            if (Main.netMode == NetmodeID.Server)
                return;

            Lighting.AddLight(npc.Center, DebugColor.ToVector3() * (0.18f + data.CurrentPhase * 0.025f));

            if (data.AttackState == IUMWAttackState.PhaseShift && Main.rand.NextBool(5))
            {
                Dust dust = Dust.NewDustPerfect(npc.Center + Main.rand.NextVector2Circular(npc.width * 0.45f, npc.height * 0.45f), DustID.Electric, Main.rand.NextVector2Circular(2f, 2f), 80, DebugColor, 0.8f);
                dust.noGravity = true;
            }
        }

        private static Vector2 SafeNormalize(Vector2 vector, Vector2 fallback)
        {
            if (vector.LengthSquared() < 0.0001f)
                return fallback;

            vector.Normalize();
            return vector;
        }
    }
}
