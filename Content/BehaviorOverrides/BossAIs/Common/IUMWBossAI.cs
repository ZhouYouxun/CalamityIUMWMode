using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Chat;
using Terraria.Localization;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common
{
    internal abstract class IUMWBossAI
    {
        public virtual bool PreAI(NPC npc, IUMWGlobalNPC data)
        {
            return true;
        }

        public virtual void PostAI(NPC npc, IUMWGlobalNPC data)
        {
            Update(npc, data);
        }

        public virtual bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            return true;
        }

        public virtual void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
        }

        public virtual void FindFrame(NPC npc, int frameHeight)
        {
        }

        private IUMWAttackProfile[][] phaseAttackCycles;

        public abstract int NPCType { get; }

        public abstract string BossName { get; }

        protected IUMWAttackProfile[][] PhaseAttackCycles => phaseAttackCycles ??= IUMWThemeCatalog.For(BossName);

        public virtual float[] PhaseLifeRatios => new[] { 0.75f, 0.5f, 0.25f };

        public virtual int AttackCycleLength => 150;

        public virtual float MotionIntensity => 1f;

        public virtual Color DebugColor => new(88, 255, 211);

        public int MaxPhaseCount => PhaseLifeRatios.Length + 1;

        public void Update(NPC npc, IUMWGlobalNPC data)
        {
            if (npc.target < 0 || npc.target >= Main.maxPlayers || !Main.player[npc.target].active || Main.player[npc.target].dead)
                npc.TargetClosest();

            if (npc.target < 0 || npc.target >= Main.maxPlayers)
                return;

            Player target = Main.player[npc.target];
            if (!target.active || target.dead)
                return;

            int nextPhase = CalculatePhase(npc);
            if (data.CurrentPhase != nextPhase)
            {
                data.CurrentPhase = nextPhase;
                data.AttackTimer = 0;
                data.PatternTimer = 0;
                data.AttackIndex = 0;
                data.TransitionTimer = 45;
                data.AttackState = IUMWAttackState.PhaseShift;
                data.BroadcastedAttackIndex = -1;
                npc.netUpdate = true;
                AnnouncePhase(npc, data);
            }
            else
            {
                data.AttackTimer++;
            }

            if (data.BroadcastedPhase != data.CurrentPhase)
                AnnouncePhase(npc, data);

            if (data.TransitionTimer > 0)
            {
                data.TransitionTimer--;
                data.AttackState = IUMWAttackState.PhaseShift;
                ApplyTransitionMotion(npc, target, data);
                AddDebugVisuals(npc, data, null);
                return;
            }

            IUMWAttackProfile[] cycle = GetCycle(data.CurrentPhase);
            if (cycle.Length <= 0)
                return;

            if (data.AttackIndex < 0 || data.AttackIndex >= cycle.Length)
                data.AttackIndex = 0;

            IUMWAttackProfile profile = cycle[data.AttackIndex];
            int duration = GetScaledDuration(profile, data.CurrentPhase);

            if (data.PatternTimer == 0 && data.BroadcastedAttackIndex != data.AttackIndex)
                BeginAttack(npc, data, profile);

            data.PatternTimer++;
            if (data.PatternTimer > duration)
            {
                data.AttackIndex = (data.AttackIndex + 1) % cycle.Length;
                data.PatternTimer = 0;
                data.BroadcastedAttackIndex = -1;
                profile = cycle[data.AttackIndex];
                BeginAttack(npc, data, profile);
                data.PatternTimer = 1;
                npc.netUpdate = true;
            }

            ApplyMotionOverlay(npc, target, data, profile);
            ExecuteAttackProfile(npc, target, data, profile);
            AddDebugVisuals(npc, data, profile);
        }

        public virtual string PhaseName(int phase) => phase switch
        {
            1 => "Opening Weave",
            2 => "Expanded Pattern",
            3 => "Terminal Design",
            4 => "Critical Compression",
            _ => "Terminal Loop"
        };

        public virtual string StateName(IUMWGlobalNPC data)
        {
            if (data.AttackState == IUMWAttackState.PhaseShift)
                return "Phase Shift";

            return CurrentProfile(data)?.Name ?? data.AttackState.ToString();
        }

        public virtual string StateName(IUMWAttackState state) => state switch
        {
            IUMWAttackState.MatrixHover => "Matrix Hover",
            IUMWAttackState.VectorDash => "Vector Dash",
            IUMWAttackState.OrbitLock => "Orbit Lock",
            IUMWAttackState.PhasePressure => "Phase Pressure",
            IUMWAttackState.PhaseShift => "Phase Shift",
            _ => state.ToString()
        };

        private IUMWAttackProfile CurrentProfile(IUMWGlobalNPC data)
        {
            IUMWAttackProfile[] cycle = GetCycle(data.CurrentPhase);
            if (cycle.Length <= 0)
                return null;

            int index = Math.Clamp(data.AttackIndex, 0, cycle.Length - 1);
            return cycle[index];
        }

        private IUMWAttackProfile[] GetCycle(int phase)
        {
            IUMWAttackProfile[][] cycles = PhaseAttackCycles;
            if (cycles.Length <= 0)
                return Array.Empty<IUMWAttackProfile>();

            int index = Math.Clamp(phase - 1, 0, cycles.Length - 1);
            return cycles[index];
        }

        private int GetScaledDuration(IUMWAttackProfile profile, int phase)
        {
            int baseline = profile.Duration > 0 ? profile.Duration : AttackCycleLength;
            return Math.Max(54, baseline - phase * 8);
        }

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

        private void BeginAttack(NPC npc, IUMWGlobalNPC data, IUMWAttackProfile profile)
        {
            data.AttackState = (IUMWAttackState)(data.AttackIndex % 4);
            data.BroadcastedAttackIndex = data.AttackIndex;

            if (Main.netMode != NetmodeID.Server)
            {
                for (int i = 0; i < 24; i++)
                {
                    Vector2 velocity = (MathHelper.TwoPi * i / 24f).ToRotationVector2() * Main.rand.NextFloat(2.4f, 5.2f);
                    Dust dust = Dust.NewDustPerfect(npc.Center, profile.DustType, velocity, 90, profile.Color, Main.rand.NextFloat(0.9f, 1.45f));
                    dust.noGravity = true;
                }
            }

            AnnounceAttack(npc, data, profile);
        }

        private void ApplyTransitionMotion(NPC npc, Player target, IUMWGlobalNPC data)
        {
            npc.velocity *= 0.93f;

            if (target.active && !target.dead)
            {
                Vector2 away = SafeNormalize(npc.Center - target.Center, Vector2.UnitY);
                npc.velocity += away * (0.16f + data.CurrentPhase * 0.035f);
            }

            if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(3))
            {
                Dust dust = Dust.NewDustPerfect(npc.Center + Main.rand.NextVector2Circular(npc.width * 0.55f, npc.height * 0.55f), DustID.Electric, Main.rand.NextVector2Circular(3f, 3f), 70, DebugColor, 1.1f);
                dust.noGravity = true;
            }
        }

        private void ApplyMotionOverlay(NPC npc, Player target, IUMWGlobalNPC data, IUMWAttackProfile profile)
        {
            if (!target.active || target.dead)
                return;

            float phaseFactor = 1f + data.CurrentPhase * 0.12f;
            float intensity = MotionIntensity * phaseFactor;
            Vector2 toTarget = target.Center - npc.Center;
            Vector2 directionToTarget = SafeNormalize(toTarget, Vector2.UnitY);
            Vector2 perpendicular = new(-directionToTarget.Y, directionToTarget.X);
            IUMWPatternKind kind = profile?.Kind ?? IUMWPatternKind.OrbitingCrossfire;

            switch (kind)
            {
                case IUMWPatternKind.OrbitingCrossfire:
                case IUMWPatternKind.SpiralBloom:
                    Vector2 hoverPoint = target.Center + new Vector2((float)Math.Sin(Main.GlobalTimeWrappedHourly * 2.2f + npc.whoAmI) * 360f, -260f - data.CurrentPhase * 30f);
                    Vector2 hoverDirection = SafeNormalize(hoverPoint - npc.Center, Vector2.Zero);
                    npc.velocity = Vector2.Lerp(npc.velocity, npc.velocity + hoverDirection * (0.55f * intensity), 0.08f);
                    break;

                case IUMWPatternKind.LateralDashBarrage:
                case IUMWPatternKind.SniperGrid:
                    int dashRate = Math.Max(42, 92 - data.CurrentPhase * 8);
                    if (data.PatternTimer % dashRate == 1)
                        npc.velocity += directionToTarget * (2.7f + data.CurrentPhase * 0.65f) * MotionIntensity;
                    break;

                case IUMWPatternKind.VortexPressure:
                case IUMWPatternKind.MinefieldPulse:
                    npc.velocity += perpendicular * (0.15f + data.CurrentPhase * 0.025f) * MotionIntensity;
                    npc.velocity += directionToTarget * (toTarget.Length() > 520f ? 0.28f : -0.08f) * MotionIntensity;
                    break;

                case IUMWPatternKind.FallingCurtain:
                case IUMWPatternKind.ConvergingFan:
                    npc.velocity += directionToTarget * (0.34f + data.CurrentPhase * 0.04f) * MotionIntensity;
                    npc.velocity *= 0.985f;
                    break;
            }

            float maxSpeed = 28f + data.CurrentPhase * 3f;
            if (npc.velocity.Length() > maxSpeed)
                npc.velocity = SafeNormalize(npc.velocity, Vector2.Zero) * maxSpeed;
        }

        private void ExecuteAttackProfile(NPC npc, Player target, IUMWGlobalNPC data, IUMWAttackProfile profile)
        {
            if (!target.active || target.dead)
                return;

            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            int fireRate = Math.Max(8, profile.FireRate - data.CurrentPhase * 2);
            int timer = data.PatternTimer;
            if (timer % fireRate != 1)
                return;

            int count = Math.Max(1, profile.Count + Math.Max(0, data.CurrentPhase - 1));
            float speed = profile.Speed + data.CurrentPhase * 0.55f;
            float phaseAngle = timer * 0.047f + npc.whoAmI * 0.37f;

            switch (profile.Kind)
            {
                case IUMWPatternKind.LateralDashBarrage:
                    FireFan(npc, profile, npc.Center, SafeNormalize(target.Center - npc.Center, Vector2.UnitY), count, speed, profile.Spread);
                    break;

                case IUMWPatternKind.OrbitingCrossfire:
                    FireOrbitingCrossfire(npc, target, profile, count, speed, phaseAngle);
                    break;

                case IUMWPatternKind.FallingCurtain:
                    FireFallingCurtain(npc, target, profile, count, speed);
                    break;

                case IUMWPatternKind.ConvergingFan:
                    FireConvergingFan(npc, target, profile, count, speed, phaseAngle);
                    break;

                case IUMWPatternKind.SpiralBloom:
                    FireSpiralBloom(npc, profile, count, speed, phaseAngle);
                    break;

                case IUMWPatternKind.MinefieldPulse:
                    FireMinefieldPulse(npc, target, profile, count, speed, phaseAngle);
                    break;

                case IUMWPatternKind.VortexPressure:
                    FireVortexPressure(npc, target, profile, count, speed, phaseAngle);
                    break;

                case IUMWPatternKind.SniperGrid:
                    FireSniperGrid(npc, target, profile, count, speed, phaseAngle);
                    break;
            }
        }

        private void FireFan(NPC npc, IUMWAttackProfile profile, Vector2 origin, Vector2 direction, int count, float speed, float spread)
        {
            if (count <= 1)
            {
                SpawnCalamityProjectile(npc, origin, direction * speed, profile, false);
                return;
            }

            float start = -spread * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float rotation = start + spread * i / (count - 1f);
                SpawnCalamityProjectile(npc, origin + direction.RotatedBy(rotation) * 18f, direction.RotatedBy(rotation) * speed, profile, i % 2 == 1);
            }
        }

        private void FireOrbitingCrossfire(NPC npc, Player target, IUMWAttackProfile profile, int count, float speed, float phaseAngle)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = MathHelper.TwoPi * i / count + phaseAngle;
                Vector2 spawn = target.Center + angle.ToRotationVector2() * (520f + 20f * (i % 2));
                Vector2 velocity = SafeNormalize(target.Center - spawn, -angle.ToRotationVector2()) * speed;
                SpawnCalamityProjectile(npc, spawn, velocity, profile, i % 2 == 1);
            }
        }

        private void FireFallingCurtain(NPC npc, Player target, IUMWAttackProfile profile, int count, float speed)
        {
            float width = 760f + count * 24f;
            for (int i = 0; i < count; i++)
            {
                float x = count <= 1 ? 0f : MathHelper.Lerp(-width * 0.5f, width * 0.5f, i / (count - 1f));
                Vector2 spawn = target.Center + new Vector2(x + Main.rand.NextFloat(-28f, 28f), -620f - Main.rand.NextFloat(90f));
                Vector2 velocity = SafeNormalize(target.Center + target.velocity * 18f - spawn, Vector2.UnitY) * (speed + Main.rand.NextFloat(-1.2f, 1.2f));
                SpawnCalamityProjectile(npc, spawn, velocity, profile, i % 3 == 0);
            }
        }

        private void FireConvergingFan(NPC npc, Player target, IUMWAttackProfile profile, int count, float speed, float phaseAngle)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = phaseAngle + MathHelper.TwoPi * i / count;
                Vector2 spawn = target.Center + angle.ToRotationVector2() * Main.rand.NextFloat(500f, 760f);
                Vector2 aim = target.Center + target.velocity * 16f + (angle + MathHelper.PiOver2).ToRotationVector2() * Main.rand.NextFloat(-70f, 70f);
                SpawnCalamityProjectile(npc, spawn, SafeNormalize(aim - spawn, -angle.ToRotationVector2()) * speed, profile, i % 2 == 1);
            }
        }

        private void FireSpiralBloom(NPC npc, IUMWAttackProfile profile, int count, float speed, float phaseAngle)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = phaseAngle + MathHelper.TwoPi * i / count;
                Vector2 direction = angle.ToRotationVector2();
                SpawnCalamityProjectile(npc, npc.Center + direction * 28f, direction * (speed + i % 3), profile, i % 2 == 1);
            }
        }

        private void FireMinefieldPulse(NPC npc, Player target, IUMWAttackProfile profile, int count, float speed, float phaseAngle)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = phaseAngle + MathHelper.TwoPi * i / count;
                Vector2 spawn = target.Center + angle.ToRotationVector2() * Main.rand.NextFloat(220f, 560f);
                Vector2 velocity = (angle + MathHelper.PiOver2 * (i % 2 == 0 ? 1f : -1f)).ToRotationVector2() * (speed * 0.38f);
                SpawnCalamityProjectile(npc, spawn, velocity, profile, i % 2 == 1, 210);
            }
        }

        private void FireVortexPressure(NPC npc, Player target, IUMWAttackProfile profile, int count, float speed, float phaseAngle)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = phaseAngle + MathHelper.TwoPi * i / count;
                Vector2 radial = angle.ToRotationVector2();
                Vector2 tangent = new(-radial.Y, radial.X);
                Vector2 spawn = target.Center + radial * Main.rand.NextFloat(340f, 620f);
                Vector2 velocity = SafeNormalize(tangent * 0.72f - radial * 0.45f, -radial) * speed;
                SpawnCalamityProjectile(npc, spawn, velocity, profile, i % 2 == 1);
            }
        }

        private void FireSniperGrid(NPC npc, Player target, IUMWAttackProfile profile, int count, float speed, float phaseAngle)
        {
            int lanes = Math.Max(4, count);
            for (int i = 0; i < lanes; i++)
            {
                float side = i % 4;
                Vector2 offset = side switch
                {
                    0f => new Vector2(-760f, MathHelper.Lerp(-300f, 300f, (i % 5) / 4f)),
                    1f => new Vector2(760f, MathHelper.Lerp(300f, -300f, (i % 5) / 4f)),
                    2f => new Vector2(MathHelper.Lerp(-420f, 420f, (i % 5) / 4f), -620f),
                    _ => new Vector2(MathHelper.Lerp(420f, -420f, (i % 5) / 4f), 620f)
                };

                Vector2 spawn = target.Center + offset.RotatedBy(phaseAngle * 0.12f);
                Vector2 aim = target.Center + target.velocity * 20f + Main.rand.NextVector2Circular(34f, 34f);
                SpawnCalamityProjectile(npc, spawn, SafeNormalize(aim - spawn, Vector2.UnitY) * speed, profile, i % 2 == 1);
            }
        }

        private void SpawnCalamityProjectile(NPC npc, Vector2 position, Vector2 velocity, IUMWAttackProfile profile, bool secondary, int timeLeftCap = 0)
        {
            string projectileName = secondary && !string.IsNullOrWhiteSpace(profile.SecondaryProjectile) ? profile.SecondaryProjectile : profile.PrimaryProjectile;
            int projectileType = GetCalamityProjectileType(projectileName);
            int damage = Math.Max(1, (int)(npc.damage * (0.22f + dataPhaseDamageScale(npc))));
            int projectileIndex = Projectile.NewProjectile(npc.GetSource_FromAI(), position, velocity, projectileType, damage, 0f, Main.myPlayer);
            if (projectileIndex < 0 || projectileIndex >= Main.maxProjectiles)
                return;

            Projectile projectile = Main.projectile[projectileIndex];
            projectile.friendly = false;
            projectile.hostile = true;
            projectile.netUpdate = true;

            if (timeLeftCap > 0)
                projectile.timeLeft = Math.Min(projectile.timeLeft, timeLeftCap);

            if (projectile.timeLeft <= 0)
                projectile.timeLeft = timeLeftCap > 0 ? timeLeftCap : 240;

            static float dataPhaseDamageScale(NPC source) => MathHelper.Clamp(source.lifeMax <= 0 ? 0.05f : (1f - source.life / (float)source.lifeMax) * 0.08f, 0.04f, 0.13f);
        }

        private int GetCalamityProjectileType(string projectileName)
        {
            if (!string.IsNullOrWhiteSpace(projectileName) && ModContent.TryFind($"CalamityMod/{projectileName}", out ModProjectile projectile))
                return projectile.Type;

            return ProjectileID.CultistBossFireBall;
        }

        private void AddDebugVisuals(NPC npc, IUMWGlobalNPC data, IUMWAttackProfile profile)
        {
            if (Main.netMode == NetmodeID.Server)
                return;

            Color color = profile?.Color ?? DebugColor;
            Lighting.AddLight(npc.Center, color.ToVector3() * (0.18f + data.CurrentPhase * 0.025f));

            if (data.AttackState == IUMWAttackState.PhaseShift && Main.rand.NextBool(5))
            {
                Dust dust = Dust.NewDustPerfect(npc.Center + Main.rand.NextVector2Circular(npc.width * 0.45f, npc.height * 0.45f), DustID.Electric, Main.rand.NextVector2Circular(2f, 2f), 80, DebugColor, 0.8f);
                dust.noGravity = true;
            }
            else if (profile is not null && Main.rand.NextBool(7))
            {
                Dust dust = Dust.NewDustPerfect(npc.Center + Main.rand.NextVector2Circular(npc.width * 0.55f, npc.height * 0.55f), profile.DustType, Main.rand.NextVector2Circular(1.8f, 1.8f), 90, profile.Color, 0.75f);
                dust.noGravity = true;
            }
        }

        private void AnnouncePhase(NPC npc, IUMWGlobalNPC data)
        {
            if (data.BroadcastedPhase == data.CurrentPhase)
                return;

            data.BroadcastedPhase = data.CurrentPhase;
            Broadcast($"[IUMW] {BossName}: Phase {data.CurrentPhase} - {PhaseName(data.CurrentPhase)}", DebugColor);
        }

        private void AnnounceAttack(NPC npc, IUMWGlobalNPC data, IUMWAttackProfile profile)
        {
            Broadcast($"[IUMW] {BossName}: {profile.Name}", profile.Color);
        }

        private void Broadcast(string text, Color color)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            if (Main.netMode == NetmodeID.Server)
                ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(text), color);
            else
                Main.NewText(text, color);
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
