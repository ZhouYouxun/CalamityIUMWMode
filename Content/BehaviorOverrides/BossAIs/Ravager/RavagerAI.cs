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

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Ravager
{
    internal sealed class RavagerAI : IUMWBossAI
    {
        #region Constants & Configurations
        public override int NPCType => ModContent.Find<ModNPC>("CalamityMod/RavagerBody").Type;
        public override string BossName => "Ravager";
        public override Color DebugColor => new(180, 50, 50);

        public override int MaxPhaseCount => 2;
        public override float[] PhaseLifeRatios => new[] { 0.50f };
        public override int AttackCycleLength => 120;
        public override float MotionIntensity => 0.8f;
        #endregion

        #region Attack States
        public enum AttackState
        {
            UltimusCleaver = 0,
            RealmRavager = 1,
            Hematemesis = 2,
            CraniumSmasher = 3,
            Vesuvius = 4,
            CorpusAvertor = 5,
            Mutilator = 6,
            ClaretCannon = 7,
            BloodBoiler = 8,
            Viscera = 9,
            BloodsoakedCrasher = 10,
            ReactorOverloadTransition = 11
        }
        #endregion

        #region Fields
        private int ticksRunning = 0;
        private int currentRepetition = 0;
        private readonly Vector2[] oldPositions = new Vector2[14];
        private int oldPositionsIndex;

        // Flesh Totem tracking
        private int totemSpawnTimer = 0;
        private int totemNPCIndex = -1;
        private Vector2 totemCenter = Vector2.Zero;

        // Limb flags
        private bool legsAlive = true;
        private bool clawsAlive = true;
        private bool limbsActive = true;
        #endregion

        #region Core AI Hooks
        public override bool PreAI(NPC npc, IUMWGlobalNPC data)
        {
            ticksRunning++;
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
                state = AttackState.UltimusCleaver;
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
                state = AttackState.ReactorOverloadTransition;
                npc.ai[1] = (float)state;
                timer = 0;
                stateTracker = 0;
                npc.netUpdate = true;
            }

            // Limb Redirection status check
            CheckLimbStatus(npc);

            // Flesh Totem Boundary Cage (700px in P1, full screen in P2)
            UpdateFleshTotem(npc, target, currentPhase);

            // Visual oscillations and walking physics
            npc.rotation = npc.velocity.X * 0.02f;
            npc.scale = 1.0f + (float)Math.Sin(ticksRunning * 0.05f) * 0.02f;

            // Execute state machine
            switch (state)
            {
                case AttackState.UltimusCleaver:
                    ExecuteUltimusCleaver(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.RealmRavager:
                    ExecuteRealmRavager(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.Hematemesis:
                    ExecuteHematemesis(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.CraniumSmasher:
                    ExecuteCraniumSmasher(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.Vesuvius:
                    ExecuteVesuvius(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.CorpusAvertor:
                    ExecuteCorpusAvertor(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.Mutilator:
                    ExecuteMutilator(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.ClaretCannon:
                    ExecuteClaretCannon(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.BloodBoiler:
                    ExecuteBloodBoiler(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.Viscera:
                    ExecuteViscera(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.BloodsoakedCrasher:
                    ExecuteBloodsoakedCrasher(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.ReactorOverloadTransition:
                    ExecuteTransition(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
            }

            return false;
        }

        public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers)
        {
            if (limbsActive)
            {
                modifiers.FinalDamage *= 0.10f; // 90% DR while any limb is active
            }
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            if (limbsActive)
            {
                modifiers.FinalDamage *= 0.10f;
            }
        }
        #endregion

        #region Segment/Shield Helper Systems
        private void CheckLimbStatus(NPC npc)
        {
            int clawL = ModContent.Find<ModNPC>("CalamityMod/RavagerClawLeft").Type;
            int clawR = ModContent.Find<ModNPC>("CalamityMod/RavagerClawRight").Type;
            int legL = ModContent.Find<ModNPC>("CalamityMod/RavagerLegLeft").Type;
            int legR = ModContent.Find<ModNPC>("CalamityMod/RavagerLegRight").Type;

            legsAlive = false;
            clawsAlive = false;
            limbsActive = false;

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC n = Main.npc[i];
                if (n.active)
                {
                    if (n.type == clawL || n.type == clawR)
                    {
                        clawsAlive = true;
                        limbsActive = true;
                    }
                    if (n.type == legL || n.type == legR)
                    {
                        legsAlive = true;
                        limbsActive = true;
                    }
                }
            }
        }

        private void UpdateFleshTotem(NPC npc, Player target, int currentPhase)
        {
            if (currentPhase >= 2)
            {
                // Cage is disabled/full screen in P2
                return;
            }

            int totemType = ModContent.Find<ModNPC>("CalamityMod/FleshTotem").Type;

            // Spawn Totem if not active
            bool totemAlive = false;
            if (totemNPCIndex >= 0 && totemNPCIndex < Main.maxNPCs)
            {
                NPC t = Main.npc[totemNPCIndex];
                if (t.active && t.type == totemType)
                {
                    totemAlive = true;
                }
            }

            if (!totemAlive)
            {
                totemSpawnTimer++;
                if (totemSpawnTimer >= 1500) // 25s regen window
                {
                    totemSpawnTimer = 0;
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        int idx = NPC.NewNPC(npc.GetSource_FromAI(), (int)npc.Center.X, (int)npc.Center.Y, totemType);
                        if (idx >= 0 && idx < Main.maxNPCs)
                        {
                            Main.npc[idx].netUpdate = true;
                            totemNPCIndex = idx;
                            totemCenter = Main.npc[idx].Center;
                        }
                    }
                }
            }
            else
            {
                // Check player boundary relative to totem
                Vector2 dist = target.Center - totemCenter;
                if (dist.Length() > 700f)
                {
                    target.AddBuff(BuffID.Poisoned, 180);
                    target.AddBuff(BuffID.Slow, 180);
                    target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(npc.whoAmI), 15, 0);
                    // pull back
                    target.velocity += SafeNormalize(totemCenter - target.Center, Vector2.Zero) * 2f;
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
                        AttackState.UltimusCleaver => AttackState.RealmRavager,
                        AttackState.RealmRavager => AttackState.Hematemesis,
                        AttackState.Hematemesis => AttackState.CraniumSmasher,
                        AttackState.CraniumSmasher => AttackState.Vesuvius,
                        AttackState.Vesuvius => AttackState.CorpusAvertor,
                        _ => AttackState.UltimusCleaver
                    };

                    // Skip legs/claws dependent attacks if they are destroyed
                    if (next == AttackState.UltimusCleaver && !legsAlive)
                    {
                        next = AttackState.RealmRavager;
                    }
                    if (next == AttackState.CraniumSmasher && !clawsAlive)
                    {
                        next = AttackState.Vesuvius;
                    }

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
                    AttackState.Mutilator => AttackState.ClaretCannon,
                    AttackState.ClaretCannon => AttackState.BloodBoiler,
                    AttackState.BloodBoiler => AttackState.Viscera,
                    AttackState.Viscera => AttackState.BloodsoakedCrasher,
                    _ => AttackState.Mutilator
                };
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
            }
            npc.netUpdate = true;
        }
        #endregion

        #region P1 Attack States
        private void ExecuteUltimusCleaver(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            // leap and heavy slam Y
            if (timer < 40)
            {
                HoverToward(npc, target.Center + new Vector2(0f, -320f), 14f, 12f);
            }
            else if (timer == 50)
            {
                npc.velocity = new Vector2(0f, 22f);
            }
            else if (timer > 50 && npc.velocity.Y == 0f && tracker == 0)
            {
                tracker = 1;
                // erupt rock spires on ground hit
                int dmg = npc.damage / 3;
                SoundEngine.PlaySound(SoundID.Item14, npc.Center);
                for (int i = 0; i < 8; i++)
                {
                    Vector2 spawn = npc.Center + new Vector2(i * 80f - 280f, 0f);
                    SpawnHostile(npc, spawn, new Vector2(0f, -8f), "Projectiles/Boss/SpikecragSpire", dmg);
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.UltimusCleaver);
            }
        }

        private void ExecuteRealmRavager(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(target.Center.X > npc.Center.X ? -350f : 350f, -100f), 10f, 15f);

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                int idx = SpawnHostile(npc, npc.Center, new Vector2(Math.Sign(target.Center.X - npc.Center.X) * 12f, 0f), "Projectiles/Boss/RealmRavagerAxe", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // space rifts expand trigger
                    Main.projectile[idx].timeLeft = 180;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.RealmRavager);
            }
        }

        private void ExecuteHematemesis(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -280f), 8f, 22f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 3; i++)
                {
                    Vector2 vel = new(Main.rand.NextFloat(-4f, 4f), -12f + i * 2f);
                    int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/HematemesisBlood", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 1f; // secondary splits on floor hit
                        Main.projectile[idx].timeLeft = 240;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.Hematemesis);
            }
        }

        private void ExecuteCraniumSmasher(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(280f, -240f), 11f, 16f);

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 16f;
                int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/CraniumSmasherFlail", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // circular bones scatter on return
                    Main.projectile[idx].timeLeft = 160;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.CraniumSmasher);
            }
        }

        private void ExecuteVesuvius(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -340f), 12f, 15f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 12; i++)
                {
                    Vector2 vel = new(Main.rand.NextFloat(-6f, 6f), -14f + Main.rand.NextFloat(-3f, 3f));
                    int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/VesuviusSpelt", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 1f; // leaves vertical lines
                        Main.projectile[idx].timeLeft = 300;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.Vesuvius);
            }
        }

        private void ExecuteCorpusAvertor(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(-300f, -200f), 10f, 22f);

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                // Left and right side daggers
                int d1 = SpawnHostile(npc, target.Center + new Vector2(-400f, -100f), new Vector2(10f, 0f), "Projectiles/Boss/CorpusAvertorDagger", dmg);
                int d2 = SpawnHostile(npc, target.Center + new Vector2(400f, -100f), new Vector2(-10f, 0f), "Projectiles/Boss/CorpusAvertorDagger", dmg);
                if (d1 >= 0 && d1 < Main.maxProjectiles)
                {
                    Main.projectile[d1].ai[0] = 1f; // 90 deg turn trigger
                    Main.projectile[d1].timeLeft = 180;
                }
                if (d2 >= 0 && d2 < Main.maxProjectiles)
                {
                    Main.projectile[d2].ai[0] = 1f; // 90 deg turn trigger
                    Main.projectile[d2].timeLeft = 180;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.CorpusAvertor);
            }
        }
        #endregion

        #region P2 Attack States
        private void ExecuteMutilator(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(-280f, -220f), 11f, 15f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // Double slash waves
                SpawnHostile(npc, npc.Center, new Vector2(12f, 4f), "Projectiles/Boss/MutilatorWave", dmg);
                SpawnHostile(npc, npc.Center, new Vector2(12f, -4f), "Projectiles/Boss/MutilatorWave", dmg);

                // Lacerator yoyo
                int idx = SpawnHostile(npc, npc.Center, new Vector2(-8f, 0f), "Projectiles/Boss/LaceratorYoyo", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].timeLeft = 160;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.Mutilator);
            }
        }

        private void ExecuteClaretCannon(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -320f), 9f, 22f);

            if (timer >= 50 && timer <= 170 && timer % 8 == 0)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 16f;
                SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/ClaretBullet", dmg);
            }

            if (timer == 100)
            {
                int dmg = npc.damage / 3;
                // Arterial Assault moving vertical columns
                for (int i = 0; i < 8; i++)
                {
                    Vector2 spawn = target.Center + new Vector2(i * 120f - 420f, -450f);
                    SpawnHostile(npc, spawn, new Vector2(0f, 12f), "Projectiles/Boss/ArterialColumn", dmg);
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.ClaretCannon);
            }
        }

        private void ExecuteBloodBoiler(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(target.Center.X > npc.Center.X ? -250f : 250f, -220f), 10f, 18f);

            if (timer >= 50 && timer <= 160 && timer % 4 == 0)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY).RotatedBy(Main.rand.NextFloat(-0.25f, 0.25f)) * 12f;
                SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/BloodBoilerFlame", dmg);
            }

            if (timer == 90)
            {
                int dmg = npc.damage / 3;
                // Sanguine Flare cross explosions
                SpawnHostile(npc, target.Center, Vector2.Zero, "Projectiles/Boss/SanguineFlare", dmg + 10);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.BloodBoiler);
            }
        }

        private void ExecuteViscera(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(300f, -240f), 8f, 24f);

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * MathHelper.TwoPi / 6f;
                    int idx = SpawnHostile(npc, npc.Center, angle.ToRotationVector2() * 8f, "Projectiles/Boss/VisceraSpire", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 1f; // life-leach trigger
                        Main.projectile[idx].timeLeft = 240;
                    }
                }

                // Dragonblood Disgorger ground lava
                SpawnHostile(npc, npc.Center, new Vector2(0f, 8f), "Projectiles/Boss/DragonbloodLava", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.Viscera);
            }
        }

        private void ExecuteBloodsoakedCrasher(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            // jump high and heavy crash
            if (timer < 45)
            {
                HoverToward(npc, target.Center + new Vector2(0f, -360f), 15f, 10f);
            }
            else if (timer == 50)
            {
                npc.velocity = new Vector2(0f, 26f);
            }
            else if (timer > 50 && npc.velocity.Y == 0f && tracker == 0)
            {
                tracker = 1;
                int dmg = npc.damage / 3;
                SoundEngine.PlaySound(SoundID.Item14, npc.Center);
                // shatter ground shockwaves
                SpawnHostile(npc, npc.Center, new Vector2(-14f, 0f), "Projectiles/Boss/BloodsoakedWave", dmg);
                SpawnHostile(npc, npc.Center, new Vector2(14f, 0f), "Projectiles/Boss/BloodsoakedWave", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.BloodsoakedCrasher);
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
                // Force destroy totem
                int totemType = ModContent.Find<ModNPC>("CalamityMod/FleshTotem").Type;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i].active && Main.npc[i].type == totemType)
                    {
                        Main.npc[i].active = false;
                    }
                }
            }

            if (timer >= 90)
            {
                AttackState next = AttackState.Mutilator;
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
                Color trailColor = new Color(200, 50, 50, 0) * alpha;
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

            Color glowColor = new Color(200, 50, 50, 0) * 0.35f;
            spriteBatch.Draw(tex, npc.Center - screenPos, frame, glowColor, npc.rotation, origin, npc.scale * 1.08f, SpriteEffects.None, 0f);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
        }
        #endregion
    }
}
