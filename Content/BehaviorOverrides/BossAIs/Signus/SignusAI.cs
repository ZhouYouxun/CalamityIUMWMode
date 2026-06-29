using System;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.WeaponAttacks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using CalamityMod;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Signus
{
    internal sealed class SignusAI : IUMWBossAI
    {
        #region Constants & Configurations
        public override int NPCType => ModContent.Find<ModNPC>("CalamityMod/Signus").Type;
        public override string BossName => "Signus";
        public override Color DebugColor => new(160, 50, 220);

        public override int MaxPhaseCount => 2;
        public override float[] PhaseLifeRatios => new[] { 0.50f };
        public override int AttackCycleLength => 120;
        public override float MotionIntensity => 1.35f;
        #endregion

        #region Attack States
        public enum AttackState
        {
            CosmicKunai = 0,
            Cosmilamp = 1,
            AethersWhisper = 2,
            DeathsAscension = 3,
            EmpyreanKnives = 4,
            KingConstellations = 5,
            MagneticMeltdown = 6,
            SevenStriker = 7,
            Transition = 8
        }
        #endregion

        #region Fields
        private int ticksRunning = 0;
        private int currentRepetition = 0;
        private readonly Vector2[] oldPositions = new Vector2[14];
        private int oldPositionsIndex;

        // Phantom evasion
        private bool coreExposed = false;
        private int stunTimer = 0;

        // Mine grid fields
        private int mineGridTimer = 0;
        private Vector2[] minePositions = new Vector2[6];
        private bool minesActive = false;
        #endregion

        #region Core AI Hooks
        public override bool PreAI(NPC npc, IUMWGlobalNPC data)
        {
            ticksRunning++;
            oldPositions[oldPositionsIndex] = npc.Center;
            oldPositionsIndex = (oldPositionsIndex + 1) % oldPositions.Length;

            if (!TryGetTarget(npc, out Player target))
            {
                npc.velocity.Y -= 0.5f;
                if (npc.timeLeft > 60) npc.timeLeft = 60;
                return false;
            }

            int currentPhase = (int)npc.ai[0];
            AttackState state = (AttackState)(int)npc.ai[1];
            ref float timer = ref npc.ai[2];
            ref float stateTracker = ref npc.ai[3];

            // Re-normalize phase/state
            if (currentPhase == 0)
            {
                currentPhase = 1;
                npc.ai[0] = 1f;
                state = AttackState.CosmicKunai;
                npc.ai[1] = (float)state;
                currentRepetition = 0;
                npc.netUpdate = true;
            }

            // Phase transition checks
            float lifeRatio = npc.lifeMax <= 0 ? 1f : npc.life / (float)npc.lifeMax;
            int nextPhase = 1;
            foreach (float threshold in PhaseLifeRatios)
            {
                if (lifeRatio <= threshold)
                    nextPhase++;
            }

            if (nextPhase > currentPhase)
            {
                currentPhase = nextPhase;
                npc.ai[0] = currentPhase;
                state = AttackState.Transition;
                npc.ai[1] = (float)state;
                timer = 0;
                stateTracker = 0;
                npc.netUpdate = true;
            }

            // Phantom Evasion alpha setting
            if (stunTimer > 0)
            {
                stunTimer--;
                npc.velocity = Vector2.Zero;
                npc.alpha = 0; // fully visible
            }
            else
            {
                npc.alpha = coreExposed ? 0 : 220; // 90% transparent when cloaked
            }

            // Twisting Mine Grid
            UpdateMineGrid(npc, target, currentPhase);

            // Bounding Arena (1400px in P1, 1000px in P2)
            float borderSize = currentPhase == 1 ? 1400f : 1000f;
            Vector2 dist = target.Center - npc.Center;
            if (dist.Length() > borderSize / 2f)
            {
                target.AddBuff(BuffID.ShadowFlame, 180);
                target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(npc.whoAmI), 12, 0);
            }

            // Movement: high-speed glide
            if (stunTimer == 0)
            {
                float speed = 14f + (1f - lifeRatio) * 6f;
                float turnSpeed = 0.05f + (1f - lifeRatio) * 0.03f;
                Vector2 desiredVel = SafeNormalize(target.Center - npc.Center, Vector2.Zero) * speed;
                npc.velocity = Vector2.Lerp(npc.velocity, desiredVel, turnSpeed);
            }
            npc.rotation = npc.velocity.X * 0.05f;
            npc.scale = 1f + MathF.Sin(ticksRunning * 0.08f) * 0.018f;

            // Execute state machine
            if (stunTimer == 0)
            {
                switch (state)
                {
                    case AttackState.CosmicKunai:
                        ExecuteCosmicKunai(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.Cosmilamp:
                        ExecuteCosmilamp(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.AethersWhisper:
                        ExecuteAethersWhisper(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.DeathsAscension:
                        ExecuteDeathsAscension(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.EmpyreanKnives:
                        ExecuteEmpyreanKnives(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.KingConstellations:
                        ExecuteKingConstellations(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.MagneticMeltdown:
                        ExecuteMagneticMeltdown(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.SevenStriker:
                        ExecuteSevenStriker(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.Transition:
                        ExecuteTransition(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                }
            }

            return false;
        }

        public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers)
        {
            if (stunTimer > 0)
                return;

            if (!coreExposed)
            {
                modifiers.FinalDamage *= 0f; // immune when cloaked
            }
            else
            {
                // heavy hit (base damage > 80) stuns Signus
                if (item.damage > 80)
                {
                    stunTimer = 180; // 3s stun
                    SoundEngine.PlaySound(SoundID.NPCHit53, npc.Center);
                }
            }
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            if (stunTimer > 0)
                return;

            if (!coreExposed)
            {
                modifiers.FinalDamage *= 0f;
            }
            else
            {
                // check projectile base damage
                if (projectile.damage > 80)
                {
                    stunTimer = 180;
                    SoundEngine.PlaySound(SoundID.NPCHit53, npc.Center);
                }
            }
        }
        #endregion

        #region Mine Grid Helpers
        private void UpdateMineGrid(NPC npc, Player target, int currentPhase)
        {
            mineGridTimer++;
            if (mineGridTimer >= 600) // every 10s
            {
                mineGridTimer = 0;
                minesActive = true;
                // Spawn 6 mine positions around center
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * MathHelper.TwoPi / 6f;
                    minePositions[i] = target.Center + angle.ToRotationVector2() * Main.rand.NextFloat(200f, 400f);
                    // Spawn mine projectiles
                    int dmg = npc.damage / 3;
                    SpawnHostile(npc, minePositions[i], Vector2.Zero, "Projectiles/Boss/SignusMine", dmg);
                }
            }

            if (minesActive && mineGridTimer < 300) // active for 5s
            {
                // Wire connection pull logic
                for (int i = 0; i < 6; i++)
                {
                    Vector2 m1 = minePositions[i];
                    Vector2 m2 = minePositions[(i + 1) % 6];
                    // check player distance to line segment
                    float abLen = Vector2.Distance(m1, m2);
                    if (abLen > 0f)
                    {
                        Vector2 ab = m2 - m1;
                        Vector2 ac = target.Center - m1;
                        float proj = Vector2.Dot(ac, ab) / abLen;
                        proj = Math.Clamp(proj, 0f, abLen);
                        Vector2 closest = m1 + SafeNormalize(ab, Vector2.Zero) * proj;
                        float dLine = Vector2.Distance(target.Center, closest);

                        if (dLine < 20f)
                        {
                            // pull player to closest mine
                            Vector2 pullDir = SafeNormalize(closest - target.Center, Vector2.Zero);
                            target.velocity += pullDir * 8f;
                        }
                    }
                }
            }
            else
            {
                minesActive = false;
            }
        }
        #endregion

        #region Attack Rotations
        private void RotateAttack(NPC npc, int currentPhase, AttackState current)
        {
            coreExposed = false;
            currentRepetition++;
            if (currentPhase == 1)
            {
                if (currentRepetition < 3)
                {
                    npc.ai[2] = 0;
                    npc.ai[3] = 0;
                }
                else
                {
                    currentRepetition = 0;
                    AttackState next = current switch
                    {
                        AttackState.CosmicKunai => AttackState.Cosmilamp,
                        _ => AttackState.CosmicKunai
                    };
                    npc.ai[1] = (float)next;
                    npc.ai[2] = 0;
                    npc.ai[3] = 0;
                }
            }
            else
            {
                currentRepetition = 0;
                AttackState next = current switch
                {
                    AttackState.AethersWhisper => AttackState.DeathsAscension,
                    AttackState.DeathsAscension => AttackState.EmpyreanKnives,
                    AttackState.EmpyreanKnives => AttackState.KingConstellations,
                    AttackState.KingConstellations => AttackState.MagneticMeltdown,
                    AttackState.MagneticMeltdown => AttackState.SevenStriker,
                    _ => AttackState.AethersWhisper
                };
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
            }
            npc.netUpdate = true;
        }
        #endregion

        #region Attack State Machine
        private void ExecuteCosmicKunai(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            // expose core during frames 50-80
            coreExposed = timer >= 50 && timer <= 80;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                Vector2 dir = SafeNormalize(target.Center - npc.Center, Vector2.UnitY);
                for (int i = 0; i < 5; i++)
                {
                    float angle = MathHelper.Lerp(-0.3f, 0.3f, i / 4f);
                    int idx = SpawnHostile(npc, npc.Center, dir.RotatedBy(angle) * 10f, "Projectiles/Boss/CosmicKunai", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 1f; // 90 deg turn trigger
                        Main.projectile[idx].timeLeft = 180;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.CosmicKunai);
            }
        }

        private void ExecuteCosmilamp(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            coreExposed = timer >= 60 && timer <= 90;

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                // spawn 3 lanterns rotating
                for (int i = 0; i < 3; i++)
                {
                    Vector2 pos = target.Center + new Vector2(i * 180f - 180f, -220f);
                    int idx = SpawnHostile(npc, pos, Vector2.Zero, "Projectiles/Boss/CosmilampLantern", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 1f; // rotating cross laser trigger
                        Main.projectile[idx].timeLeft = 150;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.Cosmilamp);
            }
        }

        private void ExecuteAethersWhisper(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            coreExposed = timer >= 50 && timer <= 80;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 3; i++)
                {
                    Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY).RotatedBy((i - 1) * 0.15f) * 12f;
                    int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/AethersWhisperBullet", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 1f; // border bounce check
                        Main.projectile[idx].timeLeft = 200;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.AethersWhisper);
            }
        }

        private void ExecuteDeathsAscension(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            coreExposed = timer >= 45 && timer <= 75;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 14f;
                int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/DeathsAscensionScythe", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // inflicts wither debuff on player hit
                    Main.projectile[idx].timeLeft = 150;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.DeathsAscension);
            }
        }

        private void ExecuteEmpyreanKnives(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            coreExposed = timer >= 40 && timer <= 120;

            if (timer >= 50 && timer <= 110 && timer % 10 == 0)
            {
                int dmg = npc.damage / 3;
                Vector2 spawn = target.Center + new Vector2(Main.rand.NextFloat(-200f, 200f), -350f);
                Vector2 vel = new(0f, 15f);
                SpawnHostile(npc, spawn, vel, "Projectiles/Boss/EmpyreanKnife", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.EmpyreanKnives);
            }
        }

        private void ExecuteKingConstellations(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            coreExposed = timer >= 50 && timer <= 80;

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                // diagonal purple lightning laser grids
                SpawnHostile(npc, target.Center, Vector2.Zero, "Projectiles/Boss/ConstellationGrid", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.KingConstellations);
            }
        }

        private void ExecuteMagneticMeltdown(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            coreExposed = timer >= 50 && timer <= 90;

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                // Magnet sphere absorbing bullets and releasing needles
                int idx = SpawnHostile(npc, npc.Center, SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 4f, "Projectiles/Boss/MagneticSphere", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].timeLeft = 150;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.MagneticMeltdown);
            }
        }

        private void ExecuteSevenStriker(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            coreExposed = timer >= 40 && timer <= 150;

            if (timer >= 50 && timer <= 140 && timer % 12 == 0)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 18f;
                SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/SevensStrikerBullet", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.SevenStriker);
            }
        }

        private void ExecuteTransition(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            coreExposed = false;
            npc.velocity *= 0.9f;

            if (timer == 45)
            {
                SoundEngine.PlaySound(SoundID.NPCDeath10, npc.Center);
                target.Calamity().GeneralScreenShakePower = 8f;
            }

            if (timer >= 90)
            {
                AttackState next = AttackState.AethersWhisper;
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
                npc.netUpdate = true;
            }
        }
        #endregion
        #region Drawing
        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            Texture2D tex = TextureAssets.Npc[npc.type].Value;
            Rectangle frame = npc.frame;
            Vector2 origin = frame.Size() / 2f;

            for (int i = 0; i < oldPositions.Length; i++)
            {
                int idx = (oldPositionsIndex - i - 1 + oldPositions.Length) % oldPositions.Length;
                if (oldPositions[idx] == Vector2.Zero) continue;
                float alpha = (1f - i / (float)oldPositions.Length) * 0.55f;
                Color trailColor = new Color(160, 50, 220, 0) * alpha;
                spriteBatch.Draw(tex, oldPositions[idx] - screenPos, frame, trailColor, npc.rotation, origin, npc.scale, SpriteEffects.None, 0f);
            }

            return true;
        }

        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            Texture2D tex = TextureAssets.Npc[npc.type].Value;
            Rectangle frame = npc.frame;
            Vector2 origin = frame.Size() / 2f;

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);

            Color glowColor = new Color(160, 50, 220, 0) * 0.35f;
            spriteBatch.Draw(tex, npc.Center - screenPos, frame, glowColor, npc.rotation, origin, npc.scale * 1.08f, SpriteEffects.None, 0f);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
        }
        #endregion
    }
}
