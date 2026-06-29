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

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.AquaticScourge
{
    internal sealed class AquaticScourgeAI : IUMWBossAI
    {
        #region Constants & Configurations
        public override int NPCType => ModContent.Find<ModNPC>("CalamityMod/AquaticScourgeHead").Type;
        public override string BossName => "Aquatic Scourge";
        public override Color DebugColor => new(100, 200, 150);

        public override int MaxPhaseCount => 2;
        public override float[] PhaseLifeRatios => new[] { 0.50f };
        public override int AttackCycleLength => 120;
        public override float MotionIntensity => 1.2f;
        #endregion

        #region Attack States
        public enum AttackState
        {
            AcidSpray = 0,
            SulphurTorpedo = 1,
            AcidGun = 2,
            SulphuricAcidCannon = 3,
            CorrosiveSpit = 4,
            Transition = 5
        }
        #endregion

        #region Fields
        private int ticksRunning = 0;
        private int currentRepetition = 0;
        private readonly Vector2[] oldPositions = new Vector2[14];
        private int oldPositionsIndex;

        // Head stun / Pustules
        private int headStunTimer = 0;
        #endregion

        #region Core AI Hooks
        public override bool PreAI(NPC npc, IUMWGlobalNPC data)
        {
            ticksRunning++;

            int headType = ModContent.Find<ModNPC>("CalamityMod/AquaticScourgeHead").Type;
            int bodyType = ModContent.Find<ModNPC>("CalamityMod/AquaticScourgeBody").Type;
            int tailType = ModContent.Find<ModNPC>("CalamityMod/AquaticScourgeTail").Type;

            // Return true for body/tail segments to let vanilla link them
            if (npc.type != headType)
            {
                if (npc.localAI[0] == 0f && npc.localAI[1] == 0f)
                {
                    npc.localAI[0] = 150f; // pustule HP
                }
                return true;
            }

            oldPositions[oldPositionsIndex] = npc.Center;
            oldPositionsIndex = (oldPositionsIndex + 1) % oldPositions.Length;

            if (!TryGetTarget(npc, out Player target))
            {
                npc.velocity.Y += 0.5f;
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
                state = AttackState.AcidSpray;
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

            // Sulphur Mist Current Sinusoidal Gaps
            UpdateSulphurMist(target);

            // Pustule broken checks to trigger head stun
            UpdatePustules(npc, bodyType);

            // Worm Head walking movement profile
            float speed = 12f + (1f - lifeRatio) * 6f;
            float turnSpeed = 0.04f + (1f - lifeRatio) * 0.03f;
            if (headStunTimer > 0)
            {
                npc.velocity *= 0.9f;
            }
            else
            {
                Vector2 desiredVel = SafeNormalize(target.Center - npc.Center, Vector2.Zero) * speed;
                npc.velocity = Vector2.Lerp(npc.velocity, desiredVel, turnSpeed);
            }
            npc.rotation = npc.velocity.ToRotation() + MathHelper.PiOver2;

            // Execute state machine
            if (headStunTimer == 0)
            {
                switch (state)
                {
                    case AttackState.AcidSpray:
                        ExecuteAcidSpray(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.SulphurTorpedo:
                        ExecuteSulphurTorpedo(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.AcidGun:
                        ExecuteAcidGun(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.SulphuricAcidCannon:
                        ExecuteSulphuricAcidCannon(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.CorrosiveSpit:
                        ExecuteCorrosiveSpit(npc, target, ref timer, ref stateTracker, currentPhase);
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
            int headType = ModContent.Find<ModNPC>("CalamityMod/AquaticScourgeHead").Type;
            int bodyType = ModContent.Find<ModNPC>("CalamityMod/AquaticScourgeBody").Type;

            if (npc.type == bodyType)
            {
                if (npc.localAI[1] == 1f)
                {
                    modifiers.FinalDamage *= 1.5f; // node broken takes 150% damage
                }
                else
                {
                    modifiers.FinalDamage *= 0.20f; // node alive = 80% DR
                    npc.localAI[0] -= 25f;
                    if (npc.localAI[0] <= 0f)
                    {
                        npc.localAI[1] = 1f;
                        SoundEngine.PlaySound(SoundID.NPCHit53, npc.Center);
                        // Release acid gas cloud
                        int dmg = npc.damage / 3;
                        if (Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            SpawnHostile(npc, npc.Center, Vector2.Zero, "Projectiles/Boss/SulphurGasCloud", dmg);
                        }
                    }
                }
            }

            if (npc.type == headType && headStunTimer > 0)
            {
                modifiers.FinalDamage *= 1.3f;
            }
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            int headType = ModContent.Find<ModNPC>("CalamityMod/AquaticScourgeHead").Type;
            int bodyType = ModContent.Find<ModNPC>("CalamityMod/AquaticScourgeBody").Type;

            if (npc.type == bodyType)
            {
                if (npc.localAI[1] == 1f)
                {
                    modifiers.FinalDamage *= 1.5f;
                }
                else
                {
                    modifiers.FinalDamage *= 0.20f;
                    npc.localAI[0] -= 25f;
                    if (npc.localAI[0] <= 0f)
                    {
                        npc.localAI[1] = 1f;
                        SoundEngine.PlaySound(SoundID.NPCHit53, npc.Center);
                        int dmg = npc.damage / 3;
                        if (Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            SpawnHostile(npc, npc.Center, Vector2.Zero, "Projectiles/Boss/SulphurGasCloud", dmg);
                        }
                    }
                }
            }

            if (npc.type == headType && headStunTimer > 0)
            {
                modifiers.FinalDamage *= 1.3f;
            }
        }
        #endregion

        #region Mist & Pustules Helpers
        private void UpdateSulphurMist(Player player)
        {
            float gapCenter = player.Center.Y + (float)Math.Sin(player.Center.X * 0.004f + ticksRunning * 0.04f) * 140f;
            if (Math.Abs(player.Center.Y - gapCenter) > 90f)
            {
                // apply Sulphuric Poisoning debuff
                int debuff = ModContent.TryFind("CalamityMod", "SulphuricPoisoning", out ModBuff b) ? b.Type : BuffID.Venom;
                player.AddBuff(debuff, 60);
                player.velocity.Y *= 0.85f; // slow down vertical speed
                // spawn toxic green bubbles
                Dust.NewDust(player.position, player.width, player.height, DustID.GreenFairy, 0f, 0f, 100, default, 1f);
            }
        }

        private void UpdatePustules(NPC npc, int bodyType)
        {
            if (headStunTimer > 0)
            {
                headStunTimer--;
                npc.defense = 0;
                return;
            }

            int brokenCount = 0;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                if (Main.npc[i].active && Main.npc[i].type == bodyType && Main.npc[i].localAI[1] == 1f)
                {
                    brokenCount++;
                }
            }

            if (brokenCount >= 6)
            {
                // Reset pustules
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i].active && Main.npc[i].type == bodyType)
                    {
                        Main.npc[i].localAI[0] = 150f;
                        Main.npc[i].localAI[1] = 0f;
                    }
                }
                headStunTimer = 360; // 6s stun
                SoundEngine.PlaySound(SoundID.NPCHit53, npc.Center);
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
                        AttackState.AcidSpray => AttackState.SulphurTorpedo,
                        _ => AttackState.AcidSpray
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
                    AttackState.AcidGun => AttackState.SulphuricAcidCannon,
                    AttackState.SulphuricAcidCannon => AttackState.CorrosiveSpit,
                    _ => AttackState.AcidGun
                };
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
            }
            npc.netUpdate = true;
        }
        #endregion

        #region Attack State Machine
        private void ExecuteAcidSpray(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer >= 50 && timer <= 170 && timer % 6 == 0)
            {
                int dmg = npc.damage / 3;
                float angle = MathHelper.Lerp(-0.3f, 0.3f, (float)Math.Sin(timer * 0.1f));
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY).RotatedBy(angle) * 11f;
                SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/ScourgeAcidDrop", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.AcidSpray);
            }
        }

        private void ExecuteSulphurTorpedo(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50 || timer == 90 || timer == 130)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 9f;
                int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/SulphurTorpedo", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // detonate on player X alignment
                    Main.projectile[idx].timeLeft = 180;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.SulphurTorpedo);
            }
        }

        private void ExecuteAcidGun(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer >= 50 && timer <= 160 && timer % 8 == 0)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 15f;
                SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/AcidGunBullet", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.AcidGun);
            }
        }

        private void ExecuteSulphuricAcidCannon(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                int idx = SpawnHostile(npc, npc.Center, SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 8f, "Projectiles/Boss/SulphuricCannonBlob", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // splits on hit
                    Main.projectile[idx].timeLeft = 150;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.SulphuricAcidCannon);
            }
        }

        private void ExecuteCorrosiveSpit(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 5; i++)
                {
                    float angle = MathHelper.Lerp(-0.4f, 0.4f, i / 4f);
                    Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY).RotatedBy(angle) * 10f;
                    SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/CorrosiveSpitBlob", dmg);
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.CorrosiveSpit);
            }
        }

        private void ExecuteTransition(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            npc.velocity *= 0.9f;

            if (timer == 45)
            {
                SoundEngine.PlaySound(SoundID.NPCDeath10, npc.Center);
                target.Calamity().GeneralScreenShakePower = 8f;
            }

            if (timer >= 90)
            {
                AttackState next = AttackState.AcidGun;
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
                Color trailColor = new Color(100, 200, 120, 0) * alpha;
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

            Color glowColor = new Color(100, 200, 120, 0) * 0.35f;
            spriteBatch.Draw(tex, npc.Center - screenPos, frame, glowColor, npc.rotation, origin, npc.scale * 1.08f, SpriteEffects.None, 0f);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
        }
        #endregion
    }
}
