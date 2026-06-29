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

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.CeaselessVoid
{
    internal sealed class CeaselessVoidAI : IUMWBossAI
    {
        #region Constants & Configurations
        public override int NPCType => ModContent.Find<ModNPC>("CalamityMod/CeaselessVoid").Type;
        public override string BossName => "Ceaseless Void";
        public override Color DebugColor => new(180, 100, 255);

        public override int MaxPhaseCount => 3;
        public override float[] PhaseLifeRatios => new[] { 0.70f, 0.35f };
        public override int AttackCycleLength => 120;
        public override float MotionIntensity => 0.8f;
        #endregion

        #region Attack States
        public enum AttackState
        {
            MirrorBlade = 0,
            VoidConcentration = 1,
            DarkSpark = 2,
            EventHorizon = 3,
            Mistlestorm = 4,
            OntologicalDespoiler = 5,
            SealedSingularity = 6,
            TacticiansTrump = 7,
            Eternity = 8,
            Transition = 9
        }
        #endregion

        #region Fields
        private int ticksRunning = 0;
        private int currentRepetition = 0;
        private readonly Vector2[] oldPositions = new Vector2[14];
        private int oldPositionsIndex;
        private bool shieldActive = true;
        private int shieldStunTimer = 0;
        private int shieldRegenTimer = 0;
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
                state = AttackState.MirrorBlade;
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

            // Event Horizon Gravity Breathing Cycle (7s / 420 frames)
            UpdateGravityBreathing(npc, target, currentPhase);

            // Bounding Arena (1300px in P1, 900px in P2/P3)
            float borderSize = currentPhase == 1 ? 1300f : 900f;
            Vector2 dist = target.Center - npc.Center;
            if (Math.Abs(dist.X) > borderSize / 2f || Math.Abs(dist.Y) > borderSize / 2f)
            {
                target.AddBuff(BuffID.Blackout, 180);
                target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(npc.whoAmI), 12, 0);
            }

            // Dark Energy Amplifiers Management
            UpdateDarkEnergy(npc, currentPhase);

            // Movement: slowly float in orbit
            if (shieldStunTimer > 0)
            {
                npc.velocity *= 0.9f;
            }
            else
            {
                Vector2 desiredPos = target.Center + new Vector2((float)Math.Cos(ticksRunning * 0.02f) * 200f, -150f);
                Vector2 desiredVel = (desiredPos - npc.Center) * 0.03f;
                npc.velocity = Vector2.Lerp(npc.velocity, desiredVel, 0.1f);
            }
            npc.rotation = ticksRunning * 0.05f;

            // Execute state machine
            if (shieldStunTimer == 0)
            {
                switch (state)
                {
                    case AttackState.MirrorBlade:
                        ExecuteMirrorBlade(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.VoidConcentration:
                        ExecuteVoidConcentration(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.DarkSpark:
                        ExecuteDarkSpark(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.EventHorizon:
                        ExecuteEventHorizon(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.Mistlestorm:
                        ExecuteMistlestorm(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.OntologicalDespoiler:
                        ExecuteOntological(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.SealedSingularity:
                        ExecuteSealedSingularity(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.TacticiansTrump:
                        ExecuteTacticiansTrump(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.Eternity:
                        ExecuteEternity(npc, target, ref timer, ref stateTracker, currentPhase);
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
            if (shieldActive)
            {
                modifiers.FinalDamage *= 0.05f; // 95% DR
            }
            if (shieldStunTimer > 0)
            {
                modifiers.FinalDamage *= 1.5f; // takes 150% damage during stun
            }
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            if (shieldActive)
            {
                modifiers.FinalDamage *= 0.05f;
            }
            if (shieldStunTimer > 0)
            {
                modifiers.FinalDamage *= 1.5f;
            }
        }
        #endregion

        #region Gravity & Shield Helpers
        private void UpdateGravityBreathing(NPC npc, Player target, int currentPhase)
        {
            // P2/P3 compresses cycle from 7s (420f) to 4s (240f)
            int cycle = currentPhase == 1 ? 420 : 240;
            int siphonEnd = currentPhase == 1 ? 240 : 120;
            int holdEnd = currentPhase == 1 ? 300 : 160;

            int timer = ticksRunning % cycle;

            if (timer < siphonEnd)
            {
                // Siphon Pull to center
                Vector2 pullDir = SafeNormalize(npc.Center - target.Center, Vector2.Zero);
                float dist = Vector2.Distance(npc.Center, target.Center);
                target.velocity += pullDir * (50000f / (dist * dist + 1200f));
            }
            else if (timer < holdEnd)
            {
                // Hold Speed zeroed
                target.velocity = Vector2.Zero;
            }
            else
            {
                // Disintegration Blast Push away
                Vector2 pushDir = SafeNormalize(target.Center - npc.Center, Vector2.Zero);
                target.velocity += pushDir * 6f;

                if (timer == holdEnd)
                {
                    int dmg = npc.damage / 3;
                    SoundEngine.PlaySound(SoundID.Item62, npc.Center);
                    // 24 directional blast
                    for (int i = 0; i < 24; i++)
                    {
                        Vector2 vel = (i * MathHelper.TwoPi / 24f).ToRotationVector2() * 7f;
                        SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/CeaselessVoidOrb", dmg);
                    }
                }
            }
        }

        private void UpdateDarkEnergy(NPC npc, int currentPhase)
        {
            if (currentPhase > 1)
            {
                shieldActive = false;
                return;
            }

            int energyType = ModContent.Find<ModNPC>("CalamityMod/DarkEnergy").Type;

            if (shieldActive)
            {
                bool alive = false;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i].active && Main.npc[i].type == energyType && Main.npc[i].ai[0] == npc.whoAmI)
                    {
                        alive = true;
                        // Projectile amplification: check if void orbs overlap with orbiter
                        for (int p = 0; p < Main.maxProjectiles; p++)
                        {
                            Projectile proj = Main.projectile[p];
                            if (proj.active && proj.hostile && proj.ModProjectile?.Name == "CeaselessVoidOrb")
                            {
                                if (Vector2.Distance(proj.Center, Main.npc[i].Center) < 45f)
                                {
                                    proj.Kill();
                                    // Split into 3 tracking lasers
                                    if (Main.netMode != NetmodeID.MultiplayerClient)
                                    {
                                        Vector2 dir = SafeNormalize(Main.player[npc.target].Center - Main.npc[i].Center, Vector2.UnitY);
                                        for (int s = -1; s <= 1; s++)
                                        {
                                            SpawnHostile(npc, Main.npc[i].Center, dir.RotatedBy(s * 0.25f) * 12f, "Projectiles/Boss/VoidLaser", npc.damage / 3);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (!alive)
                {
                    shieldActive = false;
                    shieldStunTimer = 480; // 8s stun
                    npc.velocity = Vector2.Zero;
                    SoundEngine.PlaySound(SoundID.NPCHit53, npc.Center);
                }
            }
            else
            {
                if (shieldStunTimer > 0)
                {
                    shieldStunTimer--;
                    npc.defense = 0;
                    if (shieldStunTimer == 0)
                    {
                        shieldRegenTimer = 1500; // 25s regen
                    }
                }
                else if (shieldRegenTimer > 0)
                {
                    shieldRegenTimer--;
                    if (shieldRegenTimer == 0)
                    {
                        shieldActive = true;
                        if (Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            for (int i = 0; i < 6; i++)
                            {
                                int spawn = NPC.NewNPC(npc.GetSource_FromAI(), (int)npc.Center.X + Main.rand.Next(-100, 100), (int)npc.Center.Y + Main.rand.Next(-50, 50), energyType);
                                if (spawn >= 0 && spawn < Main.maxNPCs)
                                {
                                    Main.npc[spawn].ai[0] = npc.whoAmI;
                                    Main.npc[spawn].netUpdate = true;
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region Attack Rotations
        private void RotateAttack(NPC npc, int currentPhase, AttackState current)
        {
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
                        AttackState.MirrorBlade => AttackState.VoidConcentration,
                        _ => AttackState.MirrorBlade
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
                    AttackState.DarkSpark => AttackState.EventHorizon,
                    AttackState.EventHorizon => AttackState.Mistlestorm,
                    AttackState.Mistlestorm => AttackState.OntologicalDespoiler,
                    AttackState.OntologicalDespoiler => AttackState.SealedSingularity,
                    AttackState.SealedSingularity => AttackState.TacticiansTrump,
                    AttackState.TacticiansTrump => AttackState.Eternity,
                    _ => AttackState.DarkSpark
                };
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
            }
            npc.netUpdate = true;
        }
        #endregion

        #region Attack State Machine
        private void ExecuteMirrorBlade(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                int idx = SpawnHostile(npc, npc.Center, SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 12f, "Projectiles/Boss/MirrorLaserSword", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // border bounce check (splits behind player)
                    Main.projectile[idx].timeLeft = 180;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.MirrorBlade);
            }
        }

        private void ExecuteVoidConcentration(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // 3 black holes that absorb bullets
                for (int i = 0; i < 3; i++)
                {
                    Vector2 spawn = target.Center + new Vector2(i * 180f - 180f, -220f);
                    int idx = SpawnHostile(npc, spawn, Vector2.Zero, "Projectiles/Boss/VoidAbsorbHole", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 1f; // absorb & explode split trigger
                        Main.projectile[idx].timeLeft = 180;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.VoidConcentration);
            }
        }

        private void ExecuteDarkSpark(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                int idx = SpawnHostile(npc, npc.Center, SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 8f, "Projectiles/Boss/DarkSparkCore", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // rotating laser cross trigger
                    Main.projectile[idx].timeLeft = 180;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.DarkSpark);
            }
        }

        private void ExecuteEventHorizon(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // Shrinking space rings
                int idx = SpawnHostile(npc, target.Center, Vector2.Zero, "Projectiles/Boss/VoidShrinkRing", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].timeLeft = 150;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.EventHorizon);
            }
        }

        private void ExecuteMistlestorm(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // Double helix leaves
                int idx = SpawnHostile(npc, npc.Center, SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 11f, "Projectiles/Boss/MistlestormHelix", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].timeLeft = 180;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.Mistlestorm);
            }
        }

        private void ExecuteOntological(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer >= 50 && timer <= 170 && timer % 6 == 0)
            {
                int dmg = npc.damage / 3;
                float angle = (float)Math.Sin(timer * 0.08f) * 0.4f;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY).RotatedBy(angle) * 12f;
                SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/OntologicalBullet", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.OntologicalDespoiler);
            }
        }

        private void ExecuteSealedSingularity(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                // Centered black hole pulling player
                int idx = SpawnHostile(npc, npc.Center, Vector2.Zero, "Projectiles/Boss/SealedSingularity", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // pull + vertical deathray beams trigger
                    Main.projectile[idx].timeLeft = 150;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.SealedSingularity);
            }
        }

        private void ExecuteTacticiansTrump(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // 4 tarot cards shooting vertical columns
                for (int i = 0; i < 4; i++)
                {
                    Vector2 cardPos = target.Center + new Vector2(i * 200f - 300f, -400f);
                    SpawnHostile(npc, cardPos, new Vector2(0f, 12f), "Projectiles/Boss/TacticianCardLaser", dmg);
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.TacticiansTrump);
            }
        }

        private void ExecuteEternity(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                // 100px wide laser rotating 120 deg
                int idx = SpawnHostile(npc, npc.Center, SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 6f, "Projectiles/Boss/EternityLaser", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 120f; // rotate angle range
                    Main.projectile[idx].timeLeft = 180;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.Eternity);
            }
        }

        private void ExecuteTransition(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            npc.velocity *= 0.9f;

            if (timer == 1)
            {
                SoundEngine.PlaySound(SoundID.NPCDeath10, npc.Center);
                target.Calamity().GeneralScreenShakePower = 8f;
                // kill remaining orbiters
                int energyType = ModContent.Find<ModNPC>("CalamityMod/DarkEnergy").Type;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i].active && Main.npc[i].type == energyType && Main.npc[i].ai[0] == npc.whoAmI)
                    {
                        Main.npc[i].active = false;
                    }
                }
            }

            if (timer >= 90)
            {
                AttackState next = AttackState.DarkSpark;
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
                Color trailColor = new Color(90, 40, 170, 0) * alpha;
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

            Color glowColor = new Color(90, 40, 170, 0) * 0.35f;
            spriteBatch.Draw(tex, npc.Center - screenPos, frame, glowColor, npc.rotation, origin, npc.scale * 1.08f, SpriteEffects.None, 0f);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
        }
        #endregion
    }
}
