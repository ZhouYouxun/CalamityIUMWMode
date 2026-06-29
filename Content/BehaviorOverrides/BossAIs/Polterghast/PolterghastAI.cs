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

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Polterghast
{
    internal sealed class PolterghastAI : IUMWBossAI
    {
        #region Constants & Configurations
        public override int NPCType => ModContent.Find<ModNPC>("CalamityMod/Polterghast").Type;
        public override string BossName => "Polterghast";
        public override Color DebugColor => new(200, 60, 200);

        public override int MaxPhaseCount => 3;
        public override float[] PhaseLifeRatios => new[] { 0.70f, 0.35f };
        public override int AttackCycleLength => 120;
        public override float MotionIntensity => 1.1f;
        #endregion

        #region Attack States
        public enum AttackState
        {
            TerrorBlade = 0,
            BansheeHook = 1,
            DaemonsFlame = 2,
            FatesReveal = 3,
            GhastlyVisage = 4,
            EtherealSubjugator = 5,
            GhoulishGouger = 6,
            GalileoGladius = 7,
            StratusSphere = 8,
            Transition = 9
        }
        #endregion

        #region Fields
        private int ticksRunning = 0;
        private int currentRepetition = 0;
        private readonly Vector2[] oldPositions = new Vector2[14];
        private int oldPositionsIndex;

        // Clone tracking
        private bool clonesActive = true;
        private int cloneStunTimer = 0;
        private int cloneRegenTimer = 0;
        private int clone1Index = -1;
        private int clone2Index = -1;

        // Dungeon cage wall slam variables
        private int slamTimer = 0;
        private int activeSlamSide = -1; // 0: left, 1: right, 2: top, 3: bottom
        private float wallSlamOffset = 0f;
        #endregion

        #region Core AI Hooks
        public override bool PreAI(NPC npc, IUMWGlobalNPC data)
        {
            ticksRunning++;

            int polterType = ModContent.Find<ModNPC>("CalamityMod/Polterghast").Type;

            // Mirror Clone Sub-behavior
            if (npc.ai[3] == 99f)
            {
                ExecuteCloneBehavior(npc);
                return false;
            }

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
                state = AttackState.TerrorBlade;
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

            // Brick Wall Slams (Every 8 seconds / 480 frames)
            UpdateWallSlams(npc, target, currentPhase);

            // Ghostly Twin Mirror Clones Management
            UpdateMirrorClones(npc, currentPhase);

            // Bounding Arena (1400px in P1/P2, 900px in P3)
            float borderSize = currentPhase <= 2 ? 1400f : 900f;
            Vector2 dist = target.Center - npc.Center;
            if (Math.Abs(dist.X) > borderSize / 2f || Math.Abs(dist.Y) > borderSize / 2f)
            {
                target.AddBuff(BuffID.Bleeding, 180);
                target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(npc.whoAmI), 15, 0);
            }

            // Movement: float aggressively
            if (cloneStunTimer > 0)
            {
                npc.velocity *= 0.9f;
            }
            else
            {
                float speed = 10f + (1f - lifeRatio) * 5f;
                Vector2 desiredPos = target.Center + new Vector2((float)Math.Sin(ticksRunning * 0.04f) * 260f, -120f);
                Vector2 desiredVel = (desiredPos - npc.Center) * 0.04f;
                if (desiredVel.Length() > speed) desiredVel = SafeNormalize(desiredVel, Vector2.Zero) * speed;
                npc.velocity = Vector2.Lerp(npc.velocity, desiredVel, 0.1f);
            }
            npc.rotation = npc.velocity.X * 0.04f + MathF.Sin(ticksRunning * 0.07f) * 0.05f;
            npc.scale = 1f + MathF.Sin(ticksRunning * 0.06f) * 0.02f;

            // Execute state machine
            if (cloneStunTimer == 0)
            {
                switch (state)
                {
                    case AttackState.TerrorBlade:
                        ExecuteTerrorBlade(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.BansheeHook:
                        ExecuteBansheeHook(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.DaemonsFlame:
                        ExecuteDaemonsFlame(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.FatesReveal:
                        ExecuteFatesReveal(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.GhastlyVisage:
                        ExecuteGhastlyVisage(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.EtherealSubjugator:
                        ExecuteEtherealSubjugator(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.GhoulishGouger:
                        ExecuteGhoulishGouger(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.GalileoGladius:
                        ExecuteGalileoGladius(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.StratusSphere:
                        ExecuteStratusSphere(npc, target, ref timer, ref stateTracker, currentPhase);
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
            if (clonesActive && npc.ai[3] != 99f)
            {
                modifiers.FinalDamage *= 0.15f; // 85% DR when clones are active
            }
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            if (clonesActive && npc.ai[3] != 99f)
            {
                modifiers.FinalDamage *= 0.15f;
            }
        }
        #endregion

        #region Dungeon Slams & Clone Helpers
        private void UpdateWallSlams(NPC npc, Player target, int currentPhase)
        {
            slamTimer++;
            if (slamTimer >= 480)
            {
                slamTimer = 0;
                activeSlamSide = Main.rand.Next(4); // 0: left, 1: right, 2: top, 3: bottom
                wallSlamOffset = 0f;
            }

            if (activeSlamSide != -1)
            {
                if (slamTimer < 60)
                {
                    // 1s warning grid lines
                    wallSlamOffset = MathHelper.Lerp(0f, 300f, slamTimer / 60f);
                }
                else if (slamTimer < 180)
                {
                    // 2s solid slam
                    wallSlamOffset = 300f;
                    // Check player collision with slammed wall area
                    float borderSize = currentPhase <= 2 ? 1400f : 900f;
                    Vector2 dist = target.Center - npc.Center;
                    bool collided = false;

                    if (activeSlamSide == 0 && dist.X < -borderSize / 2f + 300f) collided = true;
                    if (activeSlamSide == 1 && dist.X > borderSize / 2f - 300f) collided = true;
                    if (activeSlamSide == 2 && dist.Y < -borderSize / 2f + 300f) collided = true;
                    if (activeSlamSide == 3 && dist.Y > borderSize / 2f - 300f) collided = true;

                    if (collided)
                    {
                        target.AddBuff(BuffID.Silenced, 180); // Necro Choke: lock dash / silences
                        target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(npc.whoAmI), 15, 0);
                    }
                }
                else
                {
                    // retreat wall
                    wallSlamOffset = MathHelper.Lerp(300f, 0f, (slamTimer - 180f) / 60f);
                    if (slamTimer >= 240)
                    {
                        activeSlamSide = -1;
                        wallSlamOffset = 0f;
                    }
                }
            }
        }

        private void UpdateMirrorClones(NPC npc, int currentPhase)
        {
            if (currentPhase > 2)
            {
                clonesActive = false;
                return;
            }

            int polterType = ModContent.Find<ModNPC>("CalamityMod/Polterghast").Type;

            if (clonesActive)
            {
                bool clone1Alive = false;
                bool clone2Alive = false;

                if (clone1Index >= 0 && clone1Index < Main.maxNPCs)
                {
                    NPC c1 = Main.npc[clone1Index];
                    if (c1.active && c1.type == polterType && c1.ai[3] == 99f)
                        clone1Alive = true;
                }
                if (clone2Index >= 0 && clone2Index < Main.maxNPCs)
                {
                    NPC c2 = Main.npc[clone2Index];
                    if (c2.active && c2.type == polterType && c2.ai[3] == 99f)
                        clone2Alive = true;
                }

                if (!clone1Alive && !clone2Alive)
                {
                    clonesActive = false;
                    cloneStunTimer = 420; // 7s stun
                    npc.velocity = Vector2.Zero;
                    SoundEngine.PlaySound(SoundID.NPCHit53, npc.Center);
                }
            }
            else
            {
                if (cloneStunTimer > 0)
                {
                    cloneStunTimer--;
                    npc.defense = 0;
                    if (cloneStunTimer == 0)
                    {
                        cloneRegenTimer = 1500; // 25s regen
                    }
                }
                else if (cloneRegenTimer > 0)
                {
                    cloneRegenTimer--;
                    if (cloneRegenTimer == 0)
                    {
                        clonesActive = true;
                        if (Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            // Spawn red (Hate) and blue (Fear) clones
                            int c1 = NPC.NewNPC(npc.GetSource_FromAI(), (int)npc.Center.X - 200, (int)npc.Center.Y, polterType);
                            int c2 = NPC.NewNPC(npc.GetSource_FromAI(), (int)npc.Center.X + 200, (int)npc.Center.Y, polterType);
                            if (c1 >= 0 && c1 < Main.maxNPCs)
                            {
                                Main.npc[c1].ai[0] = npc.whoAmI;
                                Main.npc[c1].ai[1] = 0f; // Hate clone (red color filter)
                                Main.npc[c1].ai[3] = 99f;
                                Main.npc[c1].netUpdate = true;
                                clone1Index = c1;
                            }
                            if (c2 >= 0 && c2 < Main.maxNPCs)
                            {
                                Main.npc[c2].ai[0] = npc.whoAmI;
                                Main.npc[c2].ai[1] = 1f; // Fear clone (blue color filter)
                                Main.npc[c2].ai[3] = 99f;
                                Main.npc[c2].netUpdate = true;
                                clone2Index = c2;
                            }
                        }
                    }
                }
            }
        }

        private void ExecuteCloneBehavior(NPC npc)
        {
            NPC master = Main.npc[(int)npc.ai[0]];
            if (!master.active || master.type != npc.type)
            {
                npc.active = false;
                return;
            }

            Player target = Main.player[master.target];
            // Mirror movement based on target offset from pivot center (master)
            Vector2 targetOffset = target.Center - master.Center;
            npc.Center = master.Center - targetOffset; // perfect reverse mirror
            npc.velocity = -target.velocity;
            npc.rotation = npc.velocity.X * 0.05f;
            npc.dontTakeDamage = false;

            // Spawn mirroring bullet redirects if hit
            // clones have 1500 HP each
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
                        AttackState.TerrorBlade => AttackState.BansheeHook,
                        AttackState.BansheeHook => AttackState.DaemonsFlame,
                        AttackState.DaemonsFlame => AttackState.FatesReveal,
                        AttackState.FatesReveal => AttackState.GhastlyVisage,
                        AttackState.GhastlyVisage => AttackState.EtherealSubjugator,
                        AttackState.EtherealSubjugator => AttackState.GhoulishGouger,
                        _ => AttackState.TerrorBlade
                    };

                    // Skip clone-bound skills if clones are dead
                    bool hateAlive = false;
                    bool fearAlive = false;
                    int polterType = ModContent.Find<ModNPC>("CalamityMod/Polterghast").Type;
                    for (int i = 0; i < Main.maxNPCs; i++)
                    {
                        if (Main.npc[i].active && Main.npc[i].type == polterType && Main.npc[i].ai[3] == 99f)
                        {
                            if (Main.npc[i].ai[1] == 0f) hateAlive = true;
                            if (Main.npc[i].ai[1] == 1f) fearAlive = true;
                        }
                    }

                    if (next == AttackState.BansheeHook && !hateAlive) next = AttackState.DaemonsFlame;
                    if (next == AttackState.FatesReveal && !fearAlive) next = AttackState.GhastlyVisage;

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
                    AttackState.GalileoGladius => AttackState.StratusSphere,
                    _ => AttackState.GalileoGladius
                };
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
            }
            npc.netUpdate = true;
        }
        #endregion

        #region P1 Attack States
        private void ExecuteTerrorBlade(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -240f), 10f, 15f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 3; i++)
                {
                    Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY).RotatedBy((i - 1) * 0.15f) * 12f;
                    int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/TerrorBladeWave", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 1f; // wall bounce trigger
                        Main.projectile[idx].timeLeft = 240;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.TerrorBlade);
            }
        }

        private void ExecuteBansheeHook(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(240f, -220f), 11f, 12f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // Throws 4 chains that pull back
                for (int i = 0; i < 4; i++)
                {
                    Vector2 spawn = target.Center + (i * MathHelper.PiOver2).ToRotationVector2() * 400f;
                    int idx = SpawnHostile(npc, spawn, Vector2.Zero, "Projectiles/Boss/BansheeHookChain", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 30; // pull back trigger delay
                        Main.projectile[idx].timeLeft = 120;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.BansheeHook);
            }
        }

        private void ExecuteDaemonsFlame(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(-240f, -240f), 12f, 18f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // 8 spiral fireballs
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * MathHelper.TwoPi / 8f;
                    int idx = SpawnHostile(npc, npc.Center, angle.ToRotationVector2() * 4f, "Projectiles/Boss/DaemonsFireball", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 1f; // spiral path trigger
                        Main.projectile[idx].timeLeft = 180;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.DaemonsFlame);
            }
        }

        private void ExecuteFatesReveal(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -280f), 9f, 20f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 3; i++)
                {
                    Vector2 pos = target.Center + new Vector2(i * 120f - 120f, -300f);
                    int idx = SpawnHostile(npc, pos, Vector2.Zero, "Projectiles/Boss/FatesRevealSigil", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 45; // trigger sequential tracking
                        Main.projectile[idx].timeLeft = 200;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.FatesReveal);
            }
        }

        private void ExecuteGhastlyVisage(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(-280f, -150f), 10f, 22f);

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                int idx = SpawnHostile(npc, npc.Center, SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 3f, "Projectiles/Boss/GhastlyVisageFace", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // lunge trigger after 1s
                    Main.projectile[idx].timeLeft = 180;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.GhastlyVisage);
            }
        }

        private void ExecuteEtherealSubjugator(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(280f, -200f), 11f, 15f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 3; i++)
                {
                    float angle = i * MathHelper.TwoPi / 3f;
                    int idx = SpawnHostile(npc, target.Center + angle.ToRotationVector2() * 200f, Vector2.Zero, "Projectiles/Boss/EtherealSubjugatorMini", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 200f; // orbit radius
                        Main.projectile[idx].ai[1] = angle;
                        Main.projectile[idx].timeLeft = 160;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.EtherealSubjugator);
            }
        }

        private void ExecuteGhoulishGouger(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -320f), 9f, 24f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                int idx = SpawnHostile(npc, npc.Center, SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 14f, "Projectiles/Boss/GhoulishGougerSpear", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // flat border wall roll
                    Main.projectile[idx].timeLeft = 240;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.GhoulishGouger);
            }
        }
        #endregion

        #region P2 Attack States
        private void ExecuteGalileoGladius(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            // short blinks and slash lines
            if (timer >= 40 && timer <= 160 && (timer - 40) % 20 == 0)
            {
                int dmg = npc.damage / 3;
                Vector2 blinkPos = target.Center + new Vector2(Main.rand.NextFloat(-200f, 200f), Main.rand.NextFloat(-200f, 200f));
                npc.Center = blinkPos;
                SoundEngine.PlaySound(SoundID.Item8, npc.Center);
                SpawnHostile(npc, npc.Center, SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 16f, "Projectiles/Boss/GalileoGladius", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.GalileoGladius);
            }
        }

        private void ExecuteStratusSphere(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -280f), 8f, 20f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // Stratus Sphere water vapor clouds
                for (int i = 0; i < 3; i++)
                {
                    Vector2 pos = target.Center + new Vector2(i * 180f - 180f, -320f);
                    int idx = SpawnHostile(npc, pos, Vector2.Zero, "Projectiles/Boss/StratusSphereCloud", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].timeLeft = 180;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.StratusSphere);
            }
        }

        private void ExecuteTransition(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            npc.dontTakeDamage = true;
            npc.velocity *= 0.9f;

            if (timer == 1)
            {
                SoundEngine.PlaySound(SoundID.NPCDeath10, npc.Center);
                target.Calamity().GeneralScreenShakePower = 8f;
                // kill remaining clones
                int polterType = ModContent.Find<ModNPC>("CalamityMod/Polterghast").Type;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i].active && Main.npc[i].type == polterType && Main.npc[i].ai[3] == 99f)
                    {
                        Main.npc[i].active = false;
                    }
                }
            }

            if (timer >= 90)
            {
                npc.dontTakeDamage = false;
                AttackState next = AttackState.GalileoGladius;
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
            if (npc.ai[3] == 99f)
                return true; // vanilla draws clones

            Texture2D tex = TextureAssets.Npc[npc.type].Value;
            Rectangle frame = npc.frame;
            Vector2 origin = frame.Size() / 2f;

            for (int i = 0; i < oldPositions.Length; i++)
            {
                int idx = (oldPositionsIndex - i - 1 + oldPositions.Length) % oldPositions.Length;
                if (oldPositions[idx] == Vector2.Zero) continue;
                float alpha = (1f - i / (float)oldPositions.Length) * 0.55f;
                Color trailColor = new Color(200, 60, 200, 0) * alpha;
                spriteBatch.Draw(tex, oldPositions[idx] - screenPos, frame, trailColor, npc.rotation, origin, npc.scale, SpriteEffects.None, 0f);
            }

            return true;
        }

        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (npc.ai[3] == 99f)
                return; // skip additive glow for clones

            Texture2D tex = TextureAssets.Npc[npc.type].Value;
            Rectangle frame = npc.frame;
            Vector2 origin = frame.Size() / 2f;

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);

            Color glowColor = new Color(200, 60, 200, 0) * 0.35f;
            spriteBatch.Draw(tex, npc.Center - screenPos, frame, glowColor, npc.rotation, origin, npc.scale * 1.08f, SpriteEffects.None, 0f);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
        }
        #endregion
    }
}
