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

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.LeviathanAnahita
{
    internal sealed class LeviathanAnahitaAI : IUMWBossAI
    {
        #region Constants & Configurations
        public override int NPCType => ModContent.Find<ModNPC>("CalamityMod/Leviathan").Type;
        public override string BossName => "Leviathan & Anahita";
        public override Color DebugColor => new(60, 160, 255);

        public override int MaxPhaseCount => 2;
        public override float[] PhaseLifeRatios => new[] { 0.40f };
        public override int AttackCycleLength => 120;
        public override float MotionIntensity => 1.0f;
        #endregion

        #region Attack States
        public enum AttackState
        {
            Greentide = 0,
            Leviatitan = 1,
            AnahitaArpeggio = 2,
            Atlantis = 3,
            GastricBelcher = 4,
            LeviathanTeeth = 5,
            DolphinJump = 6,
            AtlantisNet = 7,
            OceanStormTransition = 8
        }
        #endregion

        #region Fields
        private int ticksRunning = 0;
        private int currentRepetition = 0;
        private readonly Vector2[] oldPositionsL = new Vector2[14];
        private int oldPositionsIndexL;
        private readonly Vector2[] oldPositionsA = new Vector2[14];
        private int oldPositionsIndexA;
        private bool shieldActive = true;
        private int shieldStunTimer = 0;
        private int shieldRegenTimer = 0;

        // Tidal currents
        private float bottomTideY = 1200f;
        private int tideTimer = 0;
        #endregion

        #region Core AI Hooks
        public override bool PreAI(NPC npc, IUMWGlobalNPC data)
        {
            ticksRunning++;

            int anahitaType = ModContent.Find<ModNPC>("CalamityMod/Anahita").Type;
            int leviathanType = ModContent.Find<ModNPC>("CalamityMod/Leviathan").Type;
            bool isAnahita = npc.type == anahitaType;

            if (isAnahita) { oldPositionsA[oldPositionsIndexA] = npc.Center; oldPositionsIndexA = (oldPositionsIndexA + 1) % oldPositionsA.Length; }
            else { oldPositionsL[oldPositionsIndexL] = npc.Center; oldPositionsIndexL = (oldPositionsIndexL + 1) % oldPositionsL.Length; }

            if (!TryGetTarget(npc, out Player target))
            {
                npc.velocity.Y -= 0.5f;
                if (npc.timeLeft > 60) npc.timeLeft = 60;
                return false;
            }

            // Sync shared values using Leviathan as master if both are present
            NPC master = npc;
            if (isAnahita)
            {
                // Find Leviathan
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i].active && Main.npc[i].type == leviathanType)
                    {
                        master = Main.npc[i];
                        break;
                    }
                }
            }

            int currentPhase = (int)master.ai[0];
            AttackState state = (AttackState)(int)master.ai[1];
            ref float timer = ref master.ai[2];
            ref float stateTracker = ref master.ai[3];

            // Initialize Phase
            if (currentPhase == 0)
            {
                currentPhase = 1;
                master.ai[0] = 1f;
                state = AttackState.Greentide;
                master.ai[1] = (float)state;
                currentRepetition = 0;
                master.netUpdate = true;
            }

            // Phase transition checks (based on the lowest life ratio of either boss)
            float lowestLife = npc.lifeMax <= 0 ? 1f : npc.life / (float)npc.lifeMax;
            if (!isAnahita)
            {
                // check if Anahita is lower
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i].active && Main.npc[i].type == anahitaType)
                    {
                        float alife = Main.npc[i].life / (float)Main.npc[i].lifeMax;
                        if (alife < lowestLife) lowestLife = alife;
                        break;
                    }
                }
            }

            int nextPhase = 1;
            foreach (float threshold in PhaseLifeRatios)
            {
                if (lowestLife <= threshold)
                    nextPhase++;
            }

            if (nextPhase > currentPhase)
            {
                currentPhase = nextPhase;
                master.ai[0] = currentPhase;
                state = AttackState.OceanStormTransition;
                master.ai[1] = (float)state;
                timer = 0;
                stateTracker = 0;
                master.netUpdate = true;
            }

            // Tidal Current forcefield (Y=200px to bottomTideY)
            tideTimer++;
            if (tideTimer >= 600) // every 10 seconds
            {
                // rise bottom barrier for 3 seconds
                if (tideTimer < 780)
                {
                    bottomTideY = MathHelper.Lerp(1200f, 1050f, (tideTimer - 600f) / 60f);
                }
                else
                {
                    bottomTideY = MathHelper.Lerp(1050f, 1200f, (tideTimer - 780f) / 60f);
                    if (tideTimer >= 840)
                    {
                        tideTimer = 0;
                        bottomTideY = 1200f;
                    }
                }
            }

            // Player boundaries
            if (target.Center.Y < 200f || target.Center.Y > bottomTideY)
            {
                target.AddBuff(BuffID.Wet, 180);
                target.AddBuff(BuffID.Slow, 180);
                target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(npc.whoAmI), 8, 0);
                if (target.Center.Y < 200f) target.velocity.Y = 4f;
                else target.velocity.Y = -4f;
            }

            // Anahita's Ice Shield Check
            if (isAnahita)
            {
                UpdateIceShield(npc);
            }

            // Custom movement and attack patterns
            if (isAnahita)
            {
                npc.rotation = npc.velocity.X * 0.05f;
                npc.scale = 1f + (float)Math.Sin(ticksRunning * 0.04f) * 0.02f;

                // Anahita's actions
                ExecuteAnahitaAttacks(npc, target, state, timer, stateTracker, currentPhase);
            }
            else
            {
                // Leviathan actions
                npc.rotation = npc.velocity.X * 0.02f;
                npc.scale = 1.1f + (float)Math.Sin(ticksRunning * 0.03f) * 0.02f;

                ExecuteLeviathanAttacks(npc, target, state, ref timer, ref stateTracker, currentPhase);
            }

            return false;
        }

        public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers)
        {
            int anahitaType = ModContent.Find<ModNPC>("CalamityMod/Anahita").Type;
            if (npc.type == anahitaType && shieldActive)
            {
                modifiers.FinalDamage *= 0.20f; // 80% DR
            }
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            int anahitaType = ModContent.Find<ModNPC>("CalamityMod/Anahita").Type;
            if (npc.type == anahitaType && shieldActive)
            {
                modifiers.FinalDamage *= 0.20f; // 80% DR
            }
        }
        #endregion

        #region Shield Management
        private void UpdateIceShield(NPC npc)
        {
            int shieldProj = ModContent.Find<ModProjectile>("CalamityMod/AnahitaShield").Type;
            if (shieldActive)
            {
                bool alive = false;
                for (int i = 0; i < Main.maxProjectiles; i++)
                {
                    if (Main.projectile[i].active && Main.projectile[i].type == shieldProj && Main.projectile[i].owner == Main.myPlayer)
                    {
                        alive = true;
                        break;
                    }
                }

                if (!alive)
                {
                    // Check if we should spawn shields first or if they were destroyed
                    if (ticksRunning > 60)
                    {
                        shieldActive = false;
                        shieldStunTimer = 300; // 5s stun
                        npc.velocity = Vector2.Zero;
                    }
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
                            for (int i = 0; i < 3; i++)
                            {
                                int idx = Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, Vector2.Zero, shieldProj, 0, 0f, Main.myPlayer);
                                if (idx >= 0 && idx < Main.maxProjectiles)
                                {
                                    Main.projectile[idx].ai[0] = npc.whoAmI;
                                    Main.projectile[idx].ai[1] = i * MathHelper.TwoPi / 3f;
                                    Main.projectile[idx].netUpdate = true;
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region Attack Coordination Rotations
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
                        AttackState.Greentide => AttackState.Leviatitan,
                        AttackState.Leviatitan => AttackState.AnahitaArpeggio,
                        AttackState.AnahitaArpeggio => AttackState.Atlantis,
                        AttackState.Atlantis => AttackState.GastricBelcher,
                        AttackState.GastricBelcher => AttackState.LeviathanTeeth,
                        _ => AttackState.Greentide
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
                    AttackState.DolphinJump => AttackState.AtlantisNet,
                    _ => AttackState.DolphinJump
                };
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
            }
            npc.netUpdate = true;
        }
        #endregion

        #region Anahita Attack States
        private void ExecuteAnahitaAttacks(NPC npc, Player target, AttackState state, float timer, float tracker, int phase)
        {
            switch (state)
            {
                case AttackState.Greentide:
                    HoverToward(npc, target.Center + new Vector2(-450f, -200f), 8f, 15f);
                    if (timer == 50)
                    {
                        int dmg = npc.damage / 3;
                        for (int i = 0; i < 6; i++)
                        {
                            Vector2 spawn = target.Center + new Vector2(i * 120f - 300f, -400f);
                            int idx = SpawnHostile(npc, spawn, new Vector2(0f, 1f), "Projectiles/Boss/GreentideWave", dmg);
                            if (idx >= 0 && idx < Main.maxProjectiles)
                            {
                                Main.projectile[idx].ai[0] = i * 15; // sequential drop
                                Main.projectile[idx].timeLeft = 300;
                            }
                        }
                    }
                    break;

                case AttackState.AnahitaArpeggio:
                    HoverToward(npc, target.Center + new Vector2(-500f, -150f), 7f, 20f);
                    if (timer == 40)
                    {
                        int dmg = npc.damage / 3;
                        for (int i = 0; i < 5; i++)
                        {
                            Vector2 spawn = target.Center + new Vector2(i * 140f - 280f, 0f);
                            int idx = SpawnHostile(npc, spawn, Vector2.Zero, "Projectiles/Boss/AnahitaNote", dmg);
                            if (idx >= 0 && idx < Main.maxProjectiles)
                            {
                                Main.projectile[idx].ai[0] = i * 15 + 10; // trigger note sequential
                                Main.projectile[idx].timeLeft = 240;
                            }
                        }
                    }
                    break;

                case AttackState.Atlantis:
                    HoverToward(npc, target.Center + new Vector2(0f, -300f), 9f, 18f);
                    if (timer == 60)
                    {
                        int dmg = npc.damage / 3;
                        Vector2[] offsets = { new(-260f, 260f), new(260f, 260f), new(0f, -360f) };
                        foreach (Vector2 off in offsets)
                        {
                            int idx = SpawnHostile(npc, target.Center + off, Vector2.Zero, "Projectiles/Boss/AtlantisSpear", dmg);
                            if (idx >= 0 && idx < Main.maxProjectiles)
                            {
                                Main.projectile[idx].ai[0] = 50; // lock warning line delay
                                Main.projectile[idx].timeLeft = 180;
                            }
                        }
                    }
                    break;

                case AttackState.AtlantisNet:
                    HoverToward(npc, target.Center + new Vector2(0f, -320f), 10f, 22f);
                    if (timer >= 60 && timer <= 180 && timer % 20 == 0)
                    {
                        int dmg = npc.damage / 3;
                        for (int i = 0; i < 6; i++)
                        {
                            float a = i * MathHelper.TwoPi / 6f + timer * 0.015f;
                            Vector2 spawn = target.Center + a.ToRotationVector2() * 320f;
                            SpawnHostile(npc, spawn, a.ToRotationVector2() * 11f, "Projectiles/Boss/AtlantisSpear", dmg);
                        }
                    }
                    break;

                default:
                    // Passive hovering during Leviathan attacks
                    HoverToward(npc, target.Center + new Vector2(-400f, -220f), 4f, 25f);
                    break;
            }
        }
        #endregion

        #region Leviathan Attack States
        private void ExecuteLeviathanAttacks(NPC npc, Player target, AttackState state, ref float timer, ref float tracker, int phase)
        {
            timer++;

            switch (state)
            {
                case AttackState.Leviatitan:
                    HoverToward(npc, target.Center + new Vector2(480f, -100f), 6f, 24f);
                    if (timer == 60)
                    {
                        int dmg = npc.damage / 3;
                        int idx = SpawnHostile(npc, npc.Center, SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 3f, "Projectiles/Boss/LeviatitanBubble", dmg);
                        if (idx >= 0 && idx < Main.maxProjectiles)
                        {
                            Main.projectile[idx].timeLeft = 150;
                            Main.projectile[idx].ai[0] = 1f; // radial needles split trigger
                        }
                    }
                    if (timer >= 220)
                    {
                        RotateAttack(npc, phase, AttackState.Leviatitan);
                    }
                    break;

                case AttackState.GastricBelcher:
                    HoverToward(npc, target.Center + new Vector2(350f, -280f), 8f, 20f);
                    if (timer == 50 || timer == 90 || timer == 130)
                    {
                        int dmg = npc.damage / 3;
                        Vector2 vel = new(Main.rand.NextFloat(-5f, 5f), 10f);
                        SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/GastricBelcherAcid", dmg);
                    }
                    if (timer >= 220)
                    {
                        RotateAttack(npc, phase, AttackState.GastricBelcher);
                    }
                    break;

                case AttackState.LeviathanTeeth:
                    HoverToward(npc, target.Center + new Vector2(400f, 0f), 9f, 18f);
                    if (timer == 60)
                    {
                        int dmg = npc.damage / 3;
                        for (int i = 0; i < 8; i++)
                        {
                            float angle = MathHelper.Lerp(-0.5f, 0.5f, i / 7f);
                            Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY).RotatedBy(angle) * 14f;
                            int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/LeviathanTooth", dmg);
                            if (idx >= 0 && idx < Main.maxProjectiles)
                            {
                                Main.projectile[idx].ai[0] = 1f; // boomerang return trigger
                                Main.projectile[idx].timeLeft = 200;
                            }
                        }
                    }
                    if (timer >= 220)
                    {
                        RotateAttack(npc, phase, AttackState.LeviathanTeeth);
                    }
                    break;

                case AttackState.DolphinJump:
                    // Lunging jump across the ocean Y levels
                    if (timer < 40)
                    {
                        HoverToward(npc, target.Center + new Vector2(500f, 300f), 12f, 14f);
                    }
                    else if (timer == 45)
                    {
                        Vector2 dir = SafeNormalize(target.Center - npc.Center, Vector2.UnitY);
                        npc.velocity = dir * 26f;
                        SoundEngine.PlaySound(SoundID.NPCDeath10, npc.Center);
                        target.Calamity().GeneralScreenShakePower = 12f;
                    }
                    else if (timer > 45 && timer < 100)
                    {
                        npc.velocity.Y += 0.45f; // gravity pull
                    }
                    else if (timer == 100)
                    {
                        // hit sea level and generate massive water waves
                        int dmg = npc.damage / 3;
                        SpawnHostile(npc, npc.Center + new Vector2(-150f, 0f), new Vector2(-12f, 0f), "Projectiles/Boss/OceanWave", dmg);
                        SpawnHostile(npc, npc.Center + new Vector2(150f, 0f), new Vector2(12f, 0f), "Projectiles/Boss/OceanWave", dmg);
                    }

                    if (timer >= 220)
                    {
                        RotateAttack(npc, phase, AttackState.DolphinJump);
                    }
                    break;

                case AttackState.OceanStormTransition:
                    npc.velocity *= 0.9f;
                    if (timer == 1)
                    {
                        SoundEngine.PlaySound(SoundID.NPCDeath10, npc.Center);
                        target.Calamity().GeneralScreenShakePower = 8f;
                        // Trigger storm particles/effects
                    }
                    if (timer >= 90)
                    {
                        AttackState next = AttackState.DolphinJump;
                        npc.ai[1] = (float)next;
                        timer = 0;
                        tracker = 0;
                        npc.netUpdate = true;
                    }
                    break;

                default:
                    // Default passive action when Anahita is attacking
                    HoverToward(npc, target.Center + new Vector2(450f, -120f), 5f, 22f);
                    if (timer >= 220)
                    {
                        RotateAttack(npc, phase, state);
                    }
                    break;
            }
        }
        #endregion
        #region Drawing
        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            int anahitaType = ModContent.Find<ModNPC>("CalamityMod/Anahita").Type;
            bool isAnahita = npc.type == anahitaType;

            Texture2D tex = TextureAssets.Npc[npc.type].Value;
            Rectangle frame = npc.frame;
            Vector2 origin = frame.Size() / 2f;

            Vector2[] positions = isAnahita ? oldPositionsA : oldPositionsL;
            int posIdx = isAnahita ? oldPositionsIndexA : oldPositionsIndexL;
            Color trailBase = isAnahita ? new Color(120, 220, 255, 0) : new Color(60, 160, 255, 0);

            for (int i = 0; i < positions.Length; i++)
            {
                int idx = (posIdx - i - 1 + positions.Length) % positions.Length;
                if (positions[idx] == Vector2.Zero) continue;
                float alpha = (1f - i / (float)positions.Length) * 0.55f;
                spriteBatch.Draw(tex, positions[idx] - screenPos, frame, trailBase * alpha, npc.rotation, origin, npc.scale, SpriteEffects.None, 0f);
            }

            return true;
        }

        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            int anahitaType = ModContent.Find<ModNPC>("CalamityMod/Anahita").Type;
            bool isAnahita = npc.type == anahitaType;
            Color glowColor = isAnahita ? new Color(120, 220, 255, 0) * 0.35f : new Color(60, 160, 255, 0) * 0.35f;

            Texture2D tex = TextureAssets.Npc[npc.type].Value;
            Rectangle frame = npc.frame;
            Vector2 origin = frame.Size() / 2f;

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);

            spriteBatch.Draw(tex, npc.Center - screenPos, frame, glowColor, npc.rotation, origin, npc.scale * 1.08f, SpriteEffects.None, 0f);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
        }
        #endregion
    }
}
