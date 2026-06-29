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

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Dragonfolly
{
    internal sealed class DragonfollyAI : IUMWBossAI
    {
        #region Constants & Configurations
        public override int NPCType => ModContent.Find<ModNPC>("CalamityMod/Bumblebirb").Type;
        public override string BossName => "Dragonfolly";
        public override Color DebugColor => new(255, 100, 100);

        public override int MaxPhaseCount => 3;
        public override float[] PhaseLifeRatios => new[] { 0.70f, 0.30f };
        public override int AttackCycleLength => 120;
        public override float MotionIntensity => 1.3f;
        #endregion

        #region Attack States
        public enum AttackState
        {
            FeatherBurst = 0,
            ThunderCross = 1,
            EggDrop = 2,
            BoltStorm = 3,
            TalonSweep = 4,
            FeatherTornado = 5,
            Transition = 6
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
                state = AttackState.FeatherBurst;
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

            // Tesla Cage boundaries (1400px in P1/P2, 1000px in P3)
            float borderSize = currentPhase <= 2 ? 1400f : 1000f;
            Vector2 dist = target.Center - npc.Center;
            if (dist.Length() > borderSize / 2f)
            {
                target.AddBuff(BuffID.Electrified, 180);
                target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(npc.whoAmI), 11, 0);
            }

            // Wing Armor Shield Management
            UpdateWingArmor(npc, currentPhase);

            // Visual oscillations and swaying
            npc.rotation = npc.velocity.X * 0.05f;
            npc.scale = 1f + (float)Math.Sin(ticksRunning * 0.05f) * 0.02f;

            // Execute state machine
            switch (state)
            {
                case AttackState.FeatherBurst:
                    ExecuteFeatherBurst(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.ThunderCross:
                    ExecuteThunderCross(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.EggDrop:
                    ExecuteEggDrop(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.BoltStorm:
                    ExecuteBoltStorm(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.TalonSweep:
                    ExecuteTalonSweep(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.FeatherTornado:
                    ExecuteFeatherTornado(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.Transition:
                    ExecuteTransition(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
            }

            return false;
        }

        public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers)
        {
            if (shieldActive)
            {
                modifiers.FinalDamage *= 0.15f; // 85% DR
            }
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            if (shieldActive)
            {
                modifiers.FinalDamage *= 0.15f;
            }
        }
        #endregion

        #region Shield Management
        private void UpdateWingArmor(NPC npc, int currentPhase)
        {
            if (currentPhase > 2)
            {
                shieldActive = false;
                return;
            }

            int swarmType = ModContent.Find<ModNPC>("CalamityMod/DraconicSwarm").Type;

            if (shieldActive)
            {
                bool alive = false;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i].active && Main.npc[i].type == swarmType && Main.npc[i].ai[0] == npc.whoAmI)
                    {
                        alive = true;
                        break;
                    }
                }

                if (!alive)
                {
                    shieldActive = false;
                    shieldStunTimer = 360; // 6s stun
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
                        shieldRegenTimer = 900; // 15s regen
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
                                int minion = NPC.NewNPC(npc.GetSource_FromAI(), (int)npc.Center.X + Main.rand.Next(-100, 100), (int)npc.Center.Y + Main.rand.Next(-50, 50), swarmType);
                                if (minion >= 0 && minion < Main.maxNPCs)
                                {
                                    Main.npc[minion].ai[0] = npc.whoAmI;
                                    Main.npc[minion].netUpdate = true;
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
            if (currentPhase <= 2)
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
                        AttackState.FeatherBurst => AttackState.ThunderCross,
                        AttackState.ThunderCross => AttackState.EggDrop,
                        _ => AttackState.FeatherBurst
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
                    AttackState.BoltStorm => AttackState.TalonSweep,
                    AttackState.TalonSweep => AttackState.FeatherTornado,
                    _ => AttackState.BoltStorm
                };
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
            }
            npc.netUpdate = true;
        }
        #endregion

        #region State Machine Implementations
        private void ExecuteFeatherBurst(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -280f), timer < 40 ? 14f : 3f, 18f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 6; i++)
                {
                    float angle = MathHelper.Lerp(-0.4f, 0.4f, i / 5f);
                    Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY).RotatedBy(angle) * 8f;
                    int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/BumblebirbFeather", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 50; // delay acceleration trigger
                        Main.projectile[idx].timeLeft = 240;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.FeatherBurst);
            }
        }

        private void ExecuteThunderCross(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            if (timer < 40)
            {
                HoverToward(npc, target.Center + new Vector2(-400f, -300f), 12f, 15f);
            }
            else if (timer == 50)
            {
                // Zig-zag charge across player
                Vector2 dir = SafeNormalize(target.Center - npc.Center, Vector2.UnitY);
                npc.velocity = dir * 24f;
                // leave warning lightning tracks
                int dmg = npc.damage / 3;
                SpawnHostile(npc, npc.Center, dir * 8f, "Projectiles/Boss/BumblebirbLightningTrack", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.ThunderCross);
            }
        }

        private void ExecuteEggDrop(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -340f), 13f, 10f);

            if (timer == 50 || timer == 90 || timer == 130)
            {
                int dmg = npc.damage / 3;
                int idx = SpawnHostile(npc, npc.Center, new Vector2(Main.rand.NextFloat(-4f, 4f), 8f), "Projectiles/Boss/BumblebirbEgg", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // hatches chick trigger
                    Main.projectile[idx].timeLeft = 300;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.EggDrop);
            }
        }

        private void ExecuteBoltStorm(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(-280f, -240f), 10f, 22f);

            if (timer == 50 || timer == 100 || timer == 150)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 6; i++)
                {
                    Vector2 spawn = target.Center + new Vector2(i * 180f - 450f, -400f);
                    int idx = SpawnHostile(npc, spawn, new Vector2(0f, 12f), "Projectiles/Boss/BumblebirbLightning", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 60; // 1s warning grid trigger
                        Main.projectile[idx].timeLeft = 240;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.BoltStorm);
            }
        }

        private void ExecuteTalonSweep(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            if (timer < 45)
            {
                HoverToward(npc, target.Center + new Vector2(target.Center.X > npc.Center.X ? -350f : 350f, -120f), 14f, 12f);
            }
            else if (timer == 50)
            {
                // horizontal lunge
                npc.velocity = new Vector2(Math.Sign(target.Center.X - npc.Center.X) * 26f, 0f);
                int dmg = npc.damage / 3;
                for (int i = -1; i <= 1; i++)
                {
                    SpawnHostile(npc, npc.Center, new Vector2(npc.velocity.X * 0.5f, i * 6f), "Projectiles/Boss/BumblebirbClaw", dmg);
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.TalonSweep);
            }
        }

        private void ExecuteFeatherTornado(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -260f), 9f, 24f);

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * MathHelper.TwoPi / 12f;
                    int idx = SpawnHostile(npc, npc.Center, angle.ToRotationVector2() * 5f, "Projectiles/Boss/BumblebirbFeather", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[1] = 1f; // circular expanding spin path trigger
                        Main.projectile[idx].timeLeft = 200;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.FeatherTornado);
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
                // kill remaining swarms
                int swarmType = ModContent.Find<ModNPC>("CalamityMod/DraconicSwarm").Type;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i].active && Main.npc[i].type == swarmType && Main.npc[i].ai[0] == npc.whoAmI)
                    {
                        Main.npc[i].active = false;
                    }
                }
            }

            if (timer >= 90)
            {
                AttackState next = AttackState.BoltStorm;
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
                Color trailColor = new Color(255, 160, 40, 0) * alpha;
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

            Color glowColor = new Color(255, 160, 40, 0) * 0.35f;
            spriteBatch.Draw(tex, npc.Center - screenPos, frame, glowColor, npc.rotation, origin, npc.scale * 1.08f, SpriteEffects.None, 0f);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
        }
        #endregion
    }
}
