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

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.OldDuke
{
    internal sealed class OldDukeAI : IUMWBossAI
    {
        #region Constants & Configurations
        public override int NPCType => ModContent.Find<ModNPC>("CalamityMod/OldDuke").Type;
        public override string BossName => "Old Duke";
        public override Color DebugColor => new(150, 180, 50);

        public override int MaxPhaseCount => 3;
        public override float[] PhaseLifeRatios => new[] { 0.70f, 0.35f };
        public override int AttackCycleLength => 120;
        public override float MotionIntensity => 1.4f;
        #endregion

        #region Attack States
        public enum AttackState
        {
            Impaler = 0,
            SepticSkewer = 1,
            AcidRound = 2,
            ToxicantScythe = 3,
            ViperDecapitator = 4,
            TruffleStaff = 5,
            CarrionReaper = 6,
            Transition = 7
        }
        #endregion

        #region Fields
        private int ticksRunning = 0;
        private int currentRepetition = 0;
        private readonly Vector2[] oldPositions = new Vector2[14];
        private int oldPositionsIndex;

        // Fat blubber armor
        private int stunTimer = 0;
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
                state = AttackState.Impaler;
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

            // Blubber armor layers init
            if (npc.localAI[0] == 0f && npc.localAI[1] == 0f)
            {
                npc.localAI[0] = 3f; // 3 blubber layers
                npc.localAI[1] = 30f; // 30 hits per layer
            }

            // Acidic Exhaust Cage Boundary (1400px in P1/P2, 1000px in P3)
            float borderSize = currentPhase <= 2 ? 1400f : 1000f;
            Vector2 dist = target.Center - npc.Center;
            if (dist.Length() > borderSize / 2f)
            {
                int debuff = ModContent.TryFind("CalamityMod", "SulphuricPoisoning", out ModBuff b) ? b.Type : BuffID.Venom;
                target.AddBuff(debuff, 180);
                target.AddBuff(BuffID.OnFire, 180);
                target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(npc.whoAmI), 15, 0);
            }

            // Stun handling
            if (stunTimer > 0)
            {
                stunTimer--;
                npc.velocity *= 0.9f;
            }
            else
            {
                // Weight turn rate adjustment (turn-rate increases by 40% per destroyed layer!)
                float blubberLayers = npc.localAI[0];
                float speed = 12f + (1f - lifeRatio) * 6f;
                float baseTurnSpeed = 0.03f;
                float turnSpeed = baseTurnSpeed * (1f + (3f - blubberLayers) * 0.40f);

                Vector2 desiredVel = SafeNormalize(target.Center - npc.Center, Vector2.Zero) * speed;
                npc.velocity = Vector2.Lerp(npc.velocity, desiredVel, turnSpeed);
            }
            npc.rotation = npc.velocity.X * 0.04f + MathF.Sin(ticksRunning * 0.06f) * 0.06f;
            npc.scale = 1f + MathF.Sin(ticksRunning * 0.05f) * 0.025f;

            // Execute state machine
            if (stunTimer == 0)
            {
                switch (state)
                {
                    case AttackState.Impaler:
                        ExecuteImpaler(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.SepticSkewer:
                        ExecuteSepticSkewer(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.AcidRound:
                        ExecuteAcidRound(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.ToxicantScythe:
                        ExecuteToxicantScythe(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.ViperDecapitator:
                        ExecuteViperDecapitator(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.TruffleStaff:
                        ExecuteTruffleStaff(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.CarrionReaper:
                        ExecuteCarrionReaper(npc, target, ref timer, ref stateTracker, currentPhase);
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
            float blubberLayers = npc.localAI[0];
            if (blubberLayers > 0f)
            {
                // 30% damage reduction per layer
                modifiers.FinalDamage *= (1f - 0.30f * blubberLayers);
                npc.localAI[1] -= 1f; // decrement hit counter
                if (npc.localAI[1] <= 0f)
                {
                    npc.localAI[0] -= 1f; // break layer
                    npc.localAI[1] = 30f;
                    stunTimer = 180; // 3s stun
                    SoundEngine.PlaySound(SoundID.NPCHit53, npc.Center);
                }
            }
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            float blubberLayers = npc.localAI[0];
            if (blubberLayers > 0f)
            {
                modifiers.FinalDamage *= (1f - 0.30f * blubberLayers);
                npc.localAI[1] -= 1f;
                if (npc.localAI[1] <= 0f)
                {
                    npc.localAI[0] -= 1f;
                    npc.localAI[1] = 30f;
                    stunTimer = 180;
                    SoundEngine.PlaySound(SoundID.NPCHit53, npc.Center);
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
                        AttackState.Impaler => AttackState.SepticSkewer,
                        _ => AttackState.Impaler
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
                    AttackState.AcidRound => AttackState.ToxicantScythe,
                    AttackState.ToxicantScythe => AttackState.ViperDecapitator,
                    AttackState.ViperDecapitator => AttackState.TruffleStaff,
                    AttackState.TruffleStaff => AttackState.CarrionReaper,
                    _ => AttackState.AcidRound
                };
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
            }
            npc.netUpdate = true;
        }
        #endregion

        #region Attack State Machine
        private void ExecuteImpaler(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                Vector2 dir = SafeNormalize(target.Center - npc.Center, Vector2.UnitY);
                for (int i = 0; i < 6; i++)
                {
                    float angle = MathHelper.Lerp(-0.4f, 0.4f, i / 5f);
                    SpawnHostile(npc, npc.Center, dir.RotatedBy(angle) * 12f, "Projectiles/Boss/OldDukeSpike", dmg);
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.Impaler);
            }
        }

        private void ExecuteSepticSkewer(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer < 40)
            {
                // fly back
                Vector2 retractPos = target.Center + new Vector2(target.Center.X > npc.Center.X ? -400f : 400f, -120f);
                Vector2 desiredVel = (retractPos - npc.Center) * 0.08f;
                npc.velocity = Vector2.Lerp(npc.velocity, desiredVel, 0.12f);
            }
            else if (timer == 50)
            {
                // quick dash leaving trail
                Vector2 dir = SafeNormalize(target.Center - npc.Center, Vector2.UnitY);
                npc.velocity = dir * 26f;
                int dmg = npc.damage / 3;
                SpawnHostile(npc, npc.Center, dir * 6f, "Projectiles/Boss/SepticSpitTrail", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.SepticSkewer);
            }
        }

        private void ExecuteAcidRound(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 14f;
                int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/AcidRoundBullet", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].timeLeft = 120;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.AcidRound);
            }
        }

        private void ExecuteToxicantScythe(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * MathHelper.PiOver2;
                    int idx = SpawnHostile(npc, npc.Center, angle.ToRotationVector2() * 6f, "Projectiles/Boss/ToxicantScythe", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 1f; // rotating radius trigger
                        Main.projectile[idx].timeLeft = 150;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.ToxicantScythe);
            }
        }

        private void ExecuteViperDecapitator(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 18f;
                SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/ViperDecapitatorBlade", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.ViperDecapitator);
            }
        }

        private void ExecuteTruffleStaff(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // falling acid spore clouds
                for (int i = 0; i < 5; i++)
                {
                    Vector2 pos = target.Center + new Vector2(i * 150f - 300f, -400f);
                    SpawnHostile(npc, pos, new Vector2(0f, 10f), "Projectiles/Boss/AcidSporeCloud", dmg);
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.TruffleStaff);
            }
        }

        private void ExecuteCarrionReaper(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                // double cross slash line alert
                SpawnHostile(npc, target.Center, Vector2.Zero, "Projectiles/Boss/ReaperCrossSlash", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.CarrionReaper);
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
                AttackState next = AttackState.AcidRound;
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
                Color trailColor = new Color(140, 190, 50, 0) * alpha;
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

            Color glowColor = new Color(140, 190, 50, 0) * 0.35f;
            spriteBatch.Draw(tex, npc.Center - screenPos, frame, glowColor, npc.rotation, origin, npc.scale * 1.08f, SpriteEffects.None, 0f);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
        }
        #endregion
    }
}
