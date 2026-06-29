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

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.AstrumAureus
{
    internal sealed class AstrumAureusAI : IUMWBossAI
    {
        #region Constants & Configurations
        public override int NPCType => ModContent.Find<ModNPC>("CalamityMod/AstrumAureus").Type;
        public override string BossName => "Astrum Aureus";
        public override Color DebugColor => new(230, 200, 60);

        public override int MaxPhaseCount => 3;
        public override float[] PhaseLifeRatios => new[] { 0.70f, 0.35f };
        public override int AttackCycleLength => 120;
        public override float MotionIntensity => 1.0f;
        #endregion

        #region Attack States
        public enum AttackState
        {
            Nebulash = 0,
            AuroraBlazer = 1,
            AlulaAustralis = 2,
            BorealisBomber = 3,
            AuroradicalThrow = 4,
            AstralScythe = 5,
            StellarCannon = 6,
            AstralachneaWeb = 7,
            StateTransition = 8
        }
        #endregion

        #region Fields
        private int ticksRunning = 0;
        private int currentRepetition = 0;
        private readonly Vector2[] oldPositions = new Vector2[14];
        private int oldPositionsIndex;

        // Shield status
        private bool shieldActive = true;
        private int shieldStunTimer = 0;
        private int shieldRegenTimer = 0;

        // Gravity anomaly variables
        private int gravityCycleTimer = 0;
        private bool superGravity = true;
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
                state = AttackState.Nebulash;
                npc.ai[1] = (float)state;
                currentRepetition = 0;
                npc.netUpdate = true;
            }

            // Phase transitions
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
                state = AttackState.StateTransition;
                npc.ai[1] = (float)state;
                timer = 0;
                stateTracker = 0;
                npc.netUpdate = true;
            }

            // Gravity anomaly cycles
            UpdateGravityAnomaly(npc, target);

            // Bounding Arena (1400px in P1/P2, 1000px in P3)
            float borderSize = currentPhase <= 2 ? 1400f : 1000f;
            Vector2 dist = target.Center - npc.Center;
            if (dist.Length() > borderSize / 2f)
            {
                target.velocity.Y += 1.0f; // heavy gravity pull
                target.AddBuff(BuffID.Cursed, 60);
                target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(npc.whoAmI), 10, 0);
            }

            // Emitter Shield Management in P1
            UpdateEmitterShield(npc, currentPhase);

            // Visual oscillations
            npc.rotation = npc.velocity.X * 0.03f;
            npc.scale = 1.05f + (float)Math.Sin(ticksRunning * 0.05f) * 0.02f;

            // Execute state machine
            switch (state)
            {
                case AttackState.Nebulash:
                    ExecuteNebulash(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.AuroraBlazer:
                    ExecuteAuroraBlazer(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.AlulaAustralis:
                    ExecuteAlulaAustralis(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.BorealisBomber:
                    ExecuteBorealisBomber(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.AuroradicalThrow:
                    ExecuteAuroradicalThrow(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.AstralScythe:
                    ExecuteAstralScythe(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.StellarCannon:
                    ExecuteStellarCannon(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.AstralachneaWeb:
                    ExecuteAstralachneaWeb(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.StateTransition:
                    ExecuteTransition(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
            }

            return false;
        }

        public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers)
        {
            if (shieldActive && npc.ai[0] == 1)
            {
                modifiers.FinalDamage *= 0f; // completely immune
            }
            if (shieldStunTimer > 0)
            {
                modifiers.FinalDamage *= 1.5f; // takes 150% damage
            }
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            if (shieldActive && npc.ai[0] == 1)
            {
                modifiers.FinalDamage *= 0f; // completely immune
            }
            if (shieldStunTimer > 0)
            {
                modifiers.FinalDamage *= 1.5f; // takes 150% damage
            }
        }
        #endregion

        #region Systems Helpers
        private void UpdateGravityAnomaly(NPC npc, Player target)
        {
            gravityCycleTimer++;
            if (superGravity)
            {
                // Super gravity for 5s (300 frames)
                target.gravity *= 2f;
                target.maxFallSpeed *= 1.5f;

                if (gravityCycleTimer >= 300)
                {
                    superGravity = false;
                    gravityCycleTimer = 0;
                    SoundEngine.PlaySound(SoundID.Item8, target.Center); // electric warning
                }
            }
            else
            {
                // Anti-gravity for 3s (180 frames)
                target.gravity = -0.5f;

                if (gravityCycleTimer >= 180)
                {
                    superGravity = true;
                    gravityCycleTimer = 0;
                    SoundEngine.PlaySound(SoundID.Item8, target.Center);
                }
            }
        }

        private void UpdateEmitterShield(NPC npc, int currentPhase)
        {
            if (currentPhase > 1)
            {
                shieldActive = false;
                return;
            }

            int emitterType = ModContent.Find<ModNPC>("CalamityMod/AureusSpawn").Type;

            if (shieldActive)
            {
                bool alive = false;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i].active && Main.npc[i].type == emitterType && Main.npc[i].ai[0] == npc.whoAmI)
                    {
                        alive = true;
                        break;
                    }
                }

                if (!alive)
                {
                    shieldActive = false;
                    shieldStunTimer = 420; // 7s stun
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
                        shieldRegenTimer = 1200; // 20s Weak period before regen
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
                            for (int i = 0; i < 4; i++)
                            {
                                int emitter = NPC.NewNPC(npc.GetSource_FromAI(), (int)npc.Center.X + Main.rand.Next(-80, 80), (int)npc.Center.Y + Main.rand.Next(-40, 40), emitterType);
                                if (emitter >= 0 && emitter < Main.maxNPCs)
                                {
                                    Main.npc[emitter].ai[0] = npc.whoAmI;
                                    Main.npc[emitter].netUpdate = true;
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
                        AttackState.Nebulash => AttackState.AuroraBlazer,
                        AttackState.AuroraBlazer => AttackState.AlulaAustralis,
                        AttackState.AlulaAustralis => AttackState.BorealisBomber,
                        AttackState.BorealisBomber => AttackState.AuroradicalThrow,
                        _ => AttackState.Nebulash
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
                    AttackState.AstralScythe => AttackState.StellarCannon,
                    AttackState.StellarCannon => AttackState.AstralachneaWeb,
                    _ => AttackState.AstralScythe
                };
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
            }
            npc.netUpdate = true;
        }
        #endregion

        #region State Machine Implementations
        private void ExecuteNebulash(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -240f), timer < 40 ? 12f : 3f, 18f);

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                Vector2 dir = SafeNormalize(target.Center - npc.Center, Vector2.UnitY);
                int idx = SpawnHostile(npc, npc.Center, dir * 8f, "Projectiles/Boss/NebulashScythe", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // chain explosion trigger
                    Main.projectile[idx].timeLeft = 120;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.Nebulash);
            }
        }

        private void ExecuteAuroraBlazer(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(-280f, -220f), 10f, 15f);

            if (timer >= 50 && timer <= 170 && timer % 6 == 0)
            {
                int dmg = npc.damage / 3;
                // Alternate slow blue and fast pink
                if (timer % 12 == 0)
                {
                    Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 3f; // slow blue
                    SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/AureusBoltBlue", dmg);
                }
                else
                {
                    Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 14f; // fast pink
                    SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/AureusBoltPink", dmg);
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.AuroraBlazer);
            }
        }

        private void ExecuteAlulaAustralis(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(300f, -200f), 9f, 20f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 10; i++)
                {
                    Vector2 spawn = target.Center + new Vector2(Main.rand.NextFloat(-240f, 240f), -400f);
                    int idx = SpawnHostile(npc, spawn, new Vector2(0f, 1f), "Projectiles/Boss/AlulaAustralisStar", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = i * 12; // sequential drop delay
                        Main.projectile[idx].timeLeft = 300;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.AlulaAustralis);
            }
        }

        private void ExecuteBorealisBomber(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            if (timer < 40)
            {
                npc.velocity.Y = -24f; // jump high off-screen
            }
            else if (timer == 50)
            {
                // drop 4 target indicators and bombs
                int dmg = npc.damage / 3;
                for (int i = 0; i < 4; i++)
                {
                    Vector2 bombPos = target.Center + new Vector2(i * 200f - 300f, 0f);
                    int idx = SpawnHostile(npc, bombPos + new Vector2(0f, -500f), new Vector2(0f, 10f), "Projectiles/Boss/BorealisBomb", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 1f; // sweep scan line trigger
                        Main.projectile[idx].timeLeft = 120;
                    }
                }
            }
            else if (timer >= 120)
            {
                // descend back
                HoverToward(npc, target.Center + new Vector2(0f, -240f), 12f, 16f);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.BorealisBomber);
            }
        }

        private void ExecuteAuroradicalThrow(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(-300f, -200f), 10f, 22f);

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 18f;
                int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/AuroradicalBoomerang", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // enhanced tracking back trigger
                    Main.projectile[idx].timeLeft = 240;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.AuroradicalThrow);
            }
        }

        private void ExecuteAstralScythe(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -280f), 9f, 20f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // 2 crossing scythes
                SpawnHostile(npc, target.Center + new Vector2(-400f, -400f), new Vector2(8f, 8f), "Projectiles/Boss/AstralScythe", dmg);
                SpawnHostile(npc, target.Center + new Vector2(400f, -400f), new Vector2(-8f, 8f), "Projectiles/Boss/AstralScythe", dmg);

                // Punching fist from ground
                SpawnHostile(npc, target.Center + new Vector2(0f, 400f), new Vector2(0f, -14f), "Projectiles/Boss/AureusFist", dmg + 10);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.AstralScythe);
            }
        }

        private void ExecuteStellarCannon(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -320f), 8f, 24f);

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                Vector2 dir = SafeNormalize(target.Center - npc.Center, Vector2.UnitY);
                int idx = SpawnHostile(npc, npc.Center, dir * 5f, "Projectiles/Boss/StellarLaser", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 120f; // 120px wide laser beam
                    Main.projectile[idx].timeLeft = 180;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.StellarCannon);
            }
        }

        private void ExecuteAstralachneaWeb(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(240f, -240f), 10f, 15f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // Spiderweb anchors on walls
                SpawnHostile(npc, target.Center + new Vector2(-400f, 0f), Vector2.Zero, "Projectiles/Boss/AstralWeb", dmg);
                SpawnHostile(npc, target.Center + new Vector2(400f, 0f), Vector2.Zero, "Projectiles/Boss/AstralWeb", dmg);

                // Hive Pod throwing target tracking star bees
                SpawnHostile(npc, npc.Center, new Vector2(0f, 8f), "Projectiles/Boss/AureusHivePod", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.AstralachneaWeb);
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
                // kill remaining spawns
                int emitterType = ModContent.Find<ModNPC>("CalamityMod/AureusSpawn").Type;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i].active && Main.npc[i].type == emitterType && Main.npc[i].ai[0] == npc.whoAmI)
                    {
                        Main.npc[i].active = false;
                    }
                }
            }

            if (timer >= 90)
            {
                AttackState next = AttackState.AstralScythe;
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
                Color trailColor = new Color(230, 200, 60, 0) * alpha;
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

            Color glowColor = new Color(230, 200, 60, 0) * 0.35f;
            spriteBatch.Draw(tex, npc.Center - screenPos, frame, glowColor, npc.rotation, origin, npc.scale * 1.08f, SpriteEffects.None, 0f);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
        }
        #endregion
    }
}
