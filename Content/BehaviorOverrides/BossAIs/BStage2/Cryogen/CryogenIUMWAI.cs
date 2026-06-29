using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.GameContent;
using CalamityMod;
using CalamityMod.NPCs;
using CalamityMod.Events;
using CalamityMod.World;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.WeaponAttacks;
using CalamityIUMWMode.Core.Systems;
using Terraria.DataStructures;
using ReLogic.Content;

using CalamityCryogen = CalamityMod.NPCs.Cryogen.Cryogen;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.BStage2.Cryogen
{
    internal sealed class CryogenIUMWAI : IUMWBossAI
    {
        #region Constants & Configuration
        public override int NPCType => ModContent.NPCType<CalamityCryogen>();
        public override string BossName => "Cryogen";

        // 6 distinct subphases (Phase Life Ratios matching Infernum)
        public override int MaxPhaseCount => 6;
        public override float[] PhaseLifeRatios => new[] { 0.90f, 0.70f, 0.50f, 0.30f, 0.12f };
        public override int AttackCycleLength => 118;
        public override float MotionIntensity => 0.84f;
        public override Color DebugColor => new(118, 226, 255);

        // Sound Hooks
        public static readonly SoundStyle ShieldRegenSound = new("CalamityMod/Sounds/Custom/CryogenShieldRegenerate");
        public static readonly SoundStyle HitSound = new("CalamityMod/Sounds/NPCHit/CryogenHit", 3);
        public static readonly SoundStyle TransitionSound = new("CalamityMod/Sounds/NPCHit/CryogenPhaseTransitionCrack");
        public static readonly SoundStyle BlastSound = SoundID.Item27; // Shatter sound

        // Proj Texture key for Calamity shield request
        private const string ProjShield = "CryogenShield";
        #endregion

        #region State Machine Enumeration
        public enum AttackState
        {
            // P1 (Sealed Ice Core)
            HoarfrostBow = 0,             // 白霜弓
            Icebreaker = 1,               // 破冰者
            Avalanche = 2,                // 雪崩
            SnowstormStaff = 3,           // 冰晶风暴
            SoulofCryogen = 4,            // 极寒之魂
            GlacialEmbrace = 5,           // 冰川之拥 (Used in select next attack)

            // P2 (Unsealed Weapon Core)
            DarklightGreatsword = 6,      // 巨剑夜光
            StarnightLance = 7,           // 星夜长枪
            ShadecrystalBarrage = 8,      // 暗晶风暴
            DaedalusGolemStaff = 9,       // 代达罗斯守卫
            DarkechoAndShimmerspark = 10,  // 暗之回响+炽光
            CrystalPiercer = 11,           // 水晶穿刺者

            // Transitions & Special
            FreezeTransition = 12,
            DeathAnimation = 13,
            VictoryDespawn = 14
        }
        #endregion

        #region Local Fields
        private float shieldRotation = 0f;
        private float coreRotation = 0f;
        private int ticksRunning = 0;

        // Transition details
        private int targetSubphase = 1;
        private float transitionFlashAlpha = 0f;

        // Repetition count tracking for P1
        private int currentAttackRepetition = 0;

        // Crystal Piercer Phase 6 loop tracker
        private bool nextCrystalPiercerGoesToDarklight = false;

        // P1 Glacial Embrace 10-Hit Shield variables
        private bool shieldActive = false;
        private int shieldHealth = 10;
        private int shieldRegenTimer = 0;
        private float shieldChargeProgress = 0f;

        // P1 Soul of Cryogen wing visuals
        private float wingDrawScale = 0f;
        #endregion

        #region Core AI Hooks
        public override bool PreAI(NPC npc, IUMWGlobalNPC data)
        {
            ticksRunning++;

            // Target selection
            if (npc.target < 0 || npc.target >= Main.maxPlayers || !Main.player[npc.target].active || Main.player[npc.target].dead)
            {
                npc.TargetClosest(true);
            }

            Player target = Main.player[npc.target];
            if (!target.active || target.dead)
            {
                ExecuteVictoryDespawn(npc);
                return false;
            }

            // Sync phase/state variables
            int currentPhase = (int)npc.ai[0];
            AttackState state = (AttackState)(int)npc.ai[1];
            ref float timer = ref npc.ai[2];
            ref float stateTracker = ref npc.ai[3];

            // Normalize stats
            npc.damage = npc.defDamage;
            npc.defense = npc.defDefense;
            npc.knockBackResist = 0f;
            npc.noGravity = true;
            npc.noTileCollide = true;

            // Initialize Phase 1
            if (currentPhase == 0)
            {
                currentPhase = 1;
                npc.ai[0] = 1f;
                state = AttackState.HoarfrostBow;
                npc.ai[1] = (float)state;
                currentAttackRepetition = 0;

                // Spawn CryoStone barrier immediately on client & server
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Projectile.NewProjectile(
                        npc.GetSource_FromAI(),
                        target.Center + new Vector2(0f, 250f),
                        Vector2.Zero,
                        ModContent.ProjectileType<CryoStoneBarrier>(),
                        0,
                        0f,
                        Main.myPlayer,
                        npc.whoAmI
                    );
                }

                npc.netUpdate = true;
            }

            // Handle P1 10-Hit Shield Logic
            UpdateGlacialEmbraceShield(npc, currentPhase);

            // Phase transition checks
            CheckPhaseTransitions(npc, target, ref currentPhase, ref state, ref timer, ref stateTracker);

            // Update physics/rotations and dynamic swaying/breathing
            UpdateBossVisuals(npc, target);

            // Execute modular state machine
            switch (state)
            {
                // P1
                case AttackState.HoarfrostBow:
                    ExecuteState_HoarfrostBow(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.Icebreaker:
                    ExecuteState_Icebreaker(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.Avalanche:
                    ExecuteState_Avalanche(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.SnowstormStaff:
                    ExecuteState_SnowstormStaff(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.SoulofCryogen:
                    ExecuteState_SoulofCryogen(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;

                // P2
                case AttackState.DarklightGreatsword:
                    ExecuteState_DarklightGreatsword(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.StarnightLance:
                    ExecuteState_StarnightLance(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.ShadecrystalBarrage:
                    ExecuteState_ShadecrystalBarrage(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.DaedalusGolemStaff:
                    ExecuteState_DaedalusGolemStaff(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.DarkechoAndShimmerspark:
                    ExecuteState_DarkechoAndShimmerspark(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.CrystalPiercer:
                    ExecuteState_CrystalPiercer(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;

                // Transitions
                case AttackState.FreezeTransition:
                    ExecuteState_FreezeTransition(npc, ref timer);
                    break;
                case AttackState.DeathAnimation:
                    ExecuteState_DeathAnimation(npc, ref timer);
                    break;
                case AttackState.VictoryDespawn:
                    ExecuteVictoryDespawn(npc);
                    break;
            }

            timer++;

            // Sync structural local variables to GlobalNPC to comply with difficulty UI
            data.CurrentPhase = currentPhase;
            data.AttackState = (IUMWAttackState)Math.Clamp((int)state, 0, 4);
            data.PatternTimer = (int)timer;

            return false;
        }

        public override void PostAI(NPC npc, IUMWGlobalNPC data)
        {
        }
        #endregion

        #region Phase Transition & Core Systems
        private void CheckPhaseTransitions(NPC npc, Player target, ref int phase, ref AttackState state, ref float timer, ref float stateTracker)
        {
            if (state == AttackState.FreezeTransition || state == AttackState.DeathAnimation || state == AttackState.VictoryDespawn)
                return;

            float lifeRatio = npc.lifeMax <= 0 ? 1f : npc.life / (float)npc.lifeMax;
            int nextPhase = 1;

            for (int i = 0; i < PhaseLifeRatios.Length; i++)
            {
                if (lifeRatio <= PhaseLifeRatios[i])
                {
                    nextPhase = i + 2;
                }
            }

            if (nextPhase > phase)
            {
                targetSubphase = nextPhase;
                TransitionToAttack(npc, AttackState.FreezeTransition);
                return;
            }
        }

        private void TransitionToAttack(NPC npc, AttackState newState)
        {
            npc.ai[1] = (float)newState;
            npc.ai[2] = 0f;
            npc.ai[3] = 0f;

            wingDrawScale = 0f;
            npc.netUpdate = true;
        }

        private void SelectNextAttack(NPC npc, int phase)
        {
            AttackState nextState = AttackState.HoarfrostBow;
            AttackState currentAttack = (AttackState)(int)npc.ai[1];

            // If in P1 (Phase 1, 2, 3), implement 3x repetition rule
            if (phase <= 3)
            {
                currentAttackRepetition++;
                if (currentAttackRepetition < 3)
                {
                    // Repeat the same attack
                    TransitionToAttack(npc, currentAttack);
                    return;
                }
                else
                {
                    // Reset repetition counter for next attack
                    currentAttackRepetition = 0;
                }
            }

            // Define attack cycles based on active subphase
            switch (phase)
            {
                case 1: // P1 Phase 1: Bow -> Icebreaker -> Avalanche -> Loop
                    if (currentAttack == AttackState.HoarfrostBow)
                        nextState = AttackState.Icebreaker;
                    else if (currentAttack == AttackState.Icebreaker)
                        nextState = AttackState.Avalanche;
                    else
                        nextState = AttackState.HoarfrostBow;
                    break;

                case 2: // P1 Phase 2: Bow -> Icebreaker -> Avalanche -> SnowstormStaff -> Loop
                    if (currentAttack == AttackState.HoarfrostBow)
                        nextState = AttackState.Icebreaker;
                    else if (currentAttack == AttackState.Icebreaker)
                        nextState = AttackState.Avalanche;
                    else if (currentAttack == AttackState.Avalanche)
                        nextState = AttackState.SnowstormStaff;
                    else
                        nextState = AttackState.HoarfrostBow;
                    break;

                case 3: // P1 Phase 3: Bow -> Icebreaker -> Soul of Cryogen -> Avalanche -> SnowstormStaff -> Loop
                    if (currentAttack == AttackState.HoarfrostBow)
                        nextState = AttackState.Icebreaker;
                    else if (currentAttack == AttackState.Icebreaker)
                        nextState = AttackState.SoulofCryogen;
                    else if (currentAttack == AttackState.SoulofCryogen)
                        nextState = AttackState.Avalanche;
                    else if (currentAttack == AttackState.Avalanche)
                        nextState = AttackState.SnowstormStaff;
                    else
                        nextState = AttackState.HoarfrostBow;
                    break;

                case 4: // P2 Phase 4: Greatsword -> Lance -> Barrage -> Loop
                    if (currentAttack == AttackState.DarklightGreatsword)
                        nextState = AttackState.StarnightLance;
                    else if (currentAttack == AttackState.StarnightLance)
                        nextState = AttackState.ShadecrystalBarrage;
                    else
                        nextState = AttackState.DarklightGreatsword;
                    break;

                case 5: // P2 Phase 5: Greatsword -> GolemStaff -> DarkechoYoyo -> Lance -> Barrage -> Loop
                    if (currentAttack == AttackState.DarklightGreatsword)
                        nextState = AttackState.DaedalusGolemStaff;
                    else if (currentAttack == AttackState.DaedalusGolemStaff)
                        nextState = AttackState.DarkechoAndShimmerspark;
                    else if (currentAttack == AttackState.DarkechoAndShimmerspark)
                        nextState = AttackState.StarnightLance;
                    else if (currentAttack == AttackState.StarnightLance)
                        nextState = AttackState.ShadecrystalBarrage;
                    else
                        nextState = AttackState.DarklightGreatsword;
                    break;

                case 6: // P2 Phase 6: Greatsword -> Lance -> Piercer -> Barrage -> Golem -> Piercer -> Loop
                    if (currentAttack == AttackState.DarklightGreatsword)
                        nextState = AttackState.StarnightLance;
                    else if (currentAttack == AttackState.StarnightLance)
                        nextState = AttackState.CrystalPiercer;
                    else if (currentAttack == AttackState.CrystalPiercer)
                    {
                        if (nextCrystalPiercerGoesToDarklight)
                        {
                            nextCrystalPiercerGoesToDarklight = false;
                            nextState = AttackState.DarklightGreatsword;
                        }
                        else
                        {
                            nextCrystalPiercerGoesToDarklight = true;
                            nextState = AttackState.ShadecrystalBarrage;
                        }
                    }
                    else if (currentAttack == AttackState.ShadecrystalBarrage)
                        nextState = AttackState.DaedalusGolemStaff;
                    else if (currentAttack == AttackState.DaedalusGolemStaff)
                        nextState = AttackState.CrystalPiercer;
                    else
                        nextState = AttackState.DarklightGreatsword;
                    break;

                default:
                    nextState = AttackState.HoarfrostBow;
                    break;
            }

            TransitionToAttack(npc, nextState);
        }

        private void UpdateGlacialEmbraceShield(NPC npc, int currentPhase)
        {
            if (currentPhase >= 4)
            {
                // Disable shield completely in Phase 2 (Unsealed Core Form)
                shieldActive = false;
                shieldChargeProgress = 0f;
                return;
            }

            if (!shieldActive)
            {
                if (shieldRegenTimer > 0)
                {
                    shieldRegenTimer--;
                }
                else
                {
                    // Start regenerating the shield
                    shieldActive = true;
                    shieldHealth = 10;
                    shieldChargeProgress = 0f;
                    SoundEngine.PlaySound(ShieldRegenSound, npc.Center);
                    npc.netUpdate = true;
                }
            }
            else
            {
                if (shieldChargeProgress < 1f)
                {
                    // Regenerating takes 5 seconds (300 frames)
                    shieldChargeProgress += 1f / 300f;
                    if (shieldChargeProgress >= 1f)
                    {
                        shieldChargeProgress = 1f;
                    }
                }
            }
        }

        private void UpdateBossVisuals(NPC npc, Player target)
        {
            // Inner core and outer shield rotations
            float rotationMultiplier = 1f + (1f - npc.life / (float)npc.lifeMax) * 0.5f;
            coreRotation += (0.015f + npc.velocity.Length() * 0.005f) * rotationMultiplier;
            shieldRotation -= (0.025f) * rotationMultiplier;

            // Organic swaying: Tilt based on X velocity
            npc.rotation = npc.velocity.X * 0.04f;
            // Hover wobble
            npc.rotation += (float)Math.Sin(ticksRunning * 0.06f) * 0.08f;

            // Breathing scale pulse
            npc.scale = 1.0f + (float)Math.Sin(ticksRunning * 0.08f) * 0.04f;
        }
        #endregion

        #region Damage Hit Interception (10-Hit Shield)
        public override bool? CanBeHitByItem(NPC npc, Player player, Item item) => null;
        public override bool? CanBeHitByProjectile(NPC npc, Projectile projectile) => null;

        public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers)
        {
            ProcessShieldHit(npc, player, ref modifiers);
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            ProcessShieldHit(npc, Main.player[projectile.owner], ref modifiers);
        }

        private void ProcessShieldHit(NPC npc, Player player, ref NPC.HitModifiers modifiers)
        {
            if (shieldActive && shieldChargeProgress >= 1f)
            {
                // Decrement shield health by 1
                shieldHealth--;

                // Play hit sounds and particles
                SoundEngine.PlaySound(SoundID.Item50 with { Volume = 0.8f, Pitch = 0.1f }, npc.Center);
                EmitCryoDustRing(npc.Center, 100f, 15, 3f);

                // Absorb damage (Modify final damage to 0 or 1 so it triggers visual hit but does no core damage)
                modifiers.FinalDamage *= 0f;
                modifiers.DisableCrit();
                modifiers.DisableKnockback();

                if (shieldHealth <= 0)
                {
                    // Shatter the shield!
                    shieldActive = false;
                    shieldRegenTimer = 600; // 10 seconds vulnerability
                    SoundEngine.PlaySound(TransitionSound, npc.Center);

                    // Screen shake
                    player.Calamity().GeneralScreenShakePower = 10f;

                    // Release 24-directional shards
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        for (int i = 0; i < 24; i++)
                        {
                            Vector2 vel = (MathHelper.TwoPi * i / 24f).ToRotationVector2() * 6f;
                            Projectile.NewProjectile(
                                npc.GetSource_FromAI(),
                                npc.Center,
                                vel,
                                ModContent.ProjectileType<CryogenJavelinShard>(),
                                npc.damage / 3,
                                0f,
                                Main.myPlayer
                            );
                        }
                    }
                    npc.netUpdate = true;
                }
            }
        }
        #endregion

        #region Phase 1 Attack States (3x Repetition)
        // P1 Attack 1: Hoarfrost Bow
        private void ExecuteState_HoarfrostBow(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            int duration = 240;

            // Movement: Elliptical hover
            Vector2 desiredPos = target.Center + new Vector2(MathF.Sin(ticksRunning * 0.018f) * 220f, -360f + MathF.Cos(ticksRunning * 0.024f) * 40f);
            SmoothMove(npc, desiredPos, 0.04f, 14f);

            if (timer == 30)
            {
                // Fire arrow upwards on server
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Projectile.NewProjectile(
                        npc.GetSource_FromAI(),
                        npc.Center + new Vector2(0f, -npc.height * 0.6f),
                        new Vector2(0f, -14f),
                        ModContent.ProjectileType<CryogenMistArrow>(),
                        npc.damage / 3,
                        0f,
                        Main.myPlayer
                    );
                }
                SoundEngine.PlaySound(SoundID.Item5, npc.Center);
            }

            // Sky arrow rain (Frame 40-130)
            if (timer >= 40 && timer <= 130 && timer % 12 == 0)
            {
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    // Spawn 4 sky arrows falling at diagonal angles
                    for (int i = 0; i < 4; i++)
                    {
                        Vector2 spawnPos = target.Center + new Vector2(Main.rand.NextFloat(-350f, 350f), -480f - Main.rand.NextFloat(80f));
                        Vector2 vel = new Vector2(Main.rand.NextFloat(-1.8f, 1.8f), Main.rand.NextFloat(8f, 12f));
                        Projectile.NewProjectile(
                            npc.GetSource_FromAI(),
                            spawnPos,
                            vel,
                            ModContent.ProjectileType<CryogenMistArrow>(),
                            npc.damage / 3,
                            0f,
                            Main.myPlayer
                        );
                    }
                }
                SoundEngine.PlaySound(SoundID.Item5 with { Volume = 0.5f }, npc.Center);
            }

            if (timer >= duration)
            {
                SelectNextAttack(npc, phase);
            }
        }

        // P1 Attack 2: Icebreaker
        private void ExecuteState_Icebreaker(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            int duration = 260;

            if (timer < 30)
            {
                // Slow down and tilt back preparing dash
                npc.velocity *= 0.85f;
            }
            else if (timer == 30)
            {
                // First dash
                Vector2 dashVel = (target.Center - npc.Center).SafeNormalize(Vector2.UnitY) * 16f;
                npc.velocity = dashVel;
                SoundEngine.PlaySound(SoundID.Item30, npc.Center);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    // Throw 3 Icebreaker boomerangs
                    float spread = MathHelper.ToRadians(15f);
                    for (int i = -1; i <= 1; i++)
                    {
                        Vector2 vel = dashVel.RotatedBy(i * spread) * 0.7f;
                        Projectile.NewProjectile(
                            npc.GetSource_FromAI(),
                            npc.Center,
                            vel,
                            ModContent.ProjectileType<CryogenIcebreaker>(),
                            npc.damage / 3,
                            0f,
                            Main.myPlayer
                        );
                    }
                }
            }
            else if (timer > 30 && timer < 100)
            {
                // Decelerate dash
                npc.velocity *= 0.96f;
            }
            else if (timer == 100)
            {
                // Reset position to other side of player
                float side = Math.Sign(target.Center.X - npc.Center.X);
                Vector2 targetPos = target.Center + new Vector2(-side * 360f, -220f);
                npc.Center = targetPos;
                npc.velocity = Vector2.Zero;
                npc.netUpdate = true;
            }
            else if (timer == 130)
            {
                // Second dash
                Vector2 dashVel = (target.Center - npc.Center).SafeNormalize(Vector2.UnitY) * 16f;
                npc.velocity = dashVel;
                SoundEngine.PlaySound(SoundID.Item30, npc.Center);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    // Throw another 3 Icebreaker boomerangs
                    float spread = MathHelper.ToRadians(15f);
                    for (int i = -1; i <= 1; i++)
                    {
                        Vector2 vel = dashVel.RotatedBy(i * spread) * 0.7f;
                        Projectile.NewProjectile(
                            npc.GetSource_FromAI(),
                            npc.Center,
                            vel,
                            ModContent.ProjectileType<CryogenIcebreaker>(),
                            npc.damage / 3,
                            0f,
                            Main.myPlayer
                        );
                    }
                }
            }
            else if (timer > 130 && timer < 200)
            {
                npc.velocity *= 0.96f;
            }
            else if (timer >= 200)
            {
                // Smoothly float back
                Vector2 desiredPos = target.Center + new Vector2(0f, -280f);
                SmoothMove(npc, desiredPos, 0.05f, 12f);
            }

            if (timer >= duration)
            {
                SelectNextAttack(npc, phase);
            }
        }

        // P1 Attack 3: Avalanche
        private void ExecuteState_Avalanche(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            int duration = 280;

            if (timer < 90)
            {
                // Float high and hover above player preparing slam
                Vector2 desiredPos = target.Center + new Vector2(0f, -360f);
                SmoothMove(npc, desiredPos, 0.06f, 12f);
            }
            else if (timer == 90)
            {
                // Vertical slam!
                npc.velocity = new Vector2(0f, 25f);
                SoundEngine.PlaySound(SoundID.Item71, npc.Center);
            }
            else if (timer > 90 && timer < 110)
            {
                // Find active CryoStoneBarrier Y level
                float barrierY = -9999f;
                for (int i = 0; i < Main.maxProjectiles; i++)
                {
                    Projectile p = Main.projectile[i];
                    if (p.active && p.type == ModContent.ProjectileType<CryoStoneBarrier>())
                    {
                        barrierY = p.Center.Y;
                        break;
                    }
                }

                if (barrierY != -9999f && npc.Center.Y >= barrierY - 32f)
                {
                    // Slam impact Y reached!
                    npc.Center = new Vector2(npc.Center.X, barrierY - 32f);
                    npc.velocity = Vector2.Zero;
                    timer = 110; // Jump to impact logic
                    npc.netUpdate = true;
                }
            }
            else if (timer == 110)
            {
                // Axis slam impact
                SoundEngine.PlaySound(SoundID.Item14, npc.Center);
                target.Calamity().GeneralScreenShakePower = 8f;

                // Find active barrier center
                float barrierCenterX = target.Center.X;
                for (int i = 0; i < Main.maxProjectiles; i++)
                {
                    Projectile p = Main.projectile[i];
                    if (p.active && p.type == ModContent.ProjectileType<CryoStoneBarrier>())
                    {
                        barrierCenterX = p.Center.X;
                        break;
                    }
                }

                // Spaced ice bomb eruptions along the floor (every 3 blocks = 48px)
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    float leftBound = barrierCenterX - 800f;
                    float rightBound = barrierCenterX + 800f;
                    int spacing = 48; // 3 blocks

                    for (float x = leftBound + 24; x < rightBound; x += spacing)
                    {
                        // Spawn IceBomb that flies upwards
                        Projectile.NewProjectile(
                            npc.GetSource_FromAI(),
                            new Vector2(x, npc.Center.Y),
                            new Vector2(0f, -4f),
                            ModContent.ProjectileType<CryogenIceBomb>(),
                            npc.damage / 3,
                            0f,
                            Main.myPlayer
                        );
                    }
                }
            }
            else
            {
                // Decay velocity and hover back up
                Vector2 desiredPos = target.Center + new Vector2(0f, -280f);
                SmoothMove(npc, desiredPos, 0.03f, 8f);
            }

            if (timer >= duration)
            {
                SelectNextAttack(npc, phase);
            }
        }

        // P1 Attack 4: Snowstorm Staff
        private void ExecuteState_SnowstormStaff(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            int duration = 300;

            // Normal elliptical hover
            Vector2 desiredPos = target.Center + new Vector2(MathF.Sin(ticksRunning * 0.015f) * 220f, -280f);
            SmoothMove(npc, desiredPos, 0.04f, 12f);

            // Spawn orbital snowflakes sequentially
            if (timer == 30 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, Vector2.Zero, ModContent.ProjectileType<CryogenSnowflake>(), npc.damage / 3, 0f, Main.myPlayer, npc.whoAmI, 0f);
            }
            else if (timer == 70 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, Vector2.Zero, ModContent.ProjectileType<CryogenSnowflake>(), npc.damage / 3, 0f, Main.myPlayer, npc.whoAmI, MathHelper.PiOver2);
            }
            else if (timer == 110 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, Vector2.Zero, ModContent.ProjectileType<CryogenSnowflake>(), npc.damage / 3, 0f, Main.myPlayer, npc.whoAmI, MathHelper.Pi);
            }

            if (timer >= duration)
            {
                SelectNextAttack(npc, phase);
            }
        }

        // P1 Attack 5: Soul of Cryogen
        private void ExecuteState_SoulofCryogen(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            int duration = 320;

            if (timer == 1)
            {
                // Determine flight start (left or right top of player)
                float side = Main.rand.NextBool() ? -1f : 1f;
                Vector2 spawnPos = target.Center + new Vector2(side * 480f, -320f);
                npc.Center = spawnPos;
                npc.velocity = new Vector2(-side * 6f, 0f); // Constant horizontal speed
                npc.netUpdate = true;
            }

            if (timer >= 30 && timer <= 260)
            {
                // Smooth fade in wings
                wingDrawScale = MathHelper.Lerp(wingDrawScale, 1.0f, 0.1f);

                // Continuous horizontal glide
                npc.velocity.Y = 0f;

                // Drop vertical spikes. Spacing: drop every 8 frames, but skip during gaps (every 80px flat has a 70px gap)
                float relativeX = Math.Abs(npc.Center.X - target.Center.X);
                int segment = (int)(relativeX / 80f);
                bool inGap = (relativeX % 80f) <= 70f && segment % 2 == 0;

                if (!inGap && timer % 8 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Projectile.NewProjectile(
                        npc.GetSource_FromAI(),
                        npc.Center,
                        new Vector2(0f, 2f),
                        ModContent.ProjectileType<CryogenSoulShard>(),
                        npc.damage / 3,
                        0f,
                        Main.myPlayer
                    );
                }
            }
            else
            {
                // Fade out wings and decelerate
                wingDrawScale = MathHelper.Lerp(wingDrawScale, 0f, 0.1f);
                npc.velocity *= 0.95f;
            }

            if (timer >= duration)
            {
                SelectNextAttack(npc, phase);
            }
        }
        #endregion

        #region Phase 2 Attack States (Unsealed Core Form)
        // P2 Attack 7: Darklight Greatsword
        private void ExecuteState_DarklightGreatsword(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            int duration = 240;

            // Tight hover movement
            Vector2 desiredPos = target.Center + new Vector2(MathF.Sin(ticksRunning * 0.02f) * 160f, -240f);
            SmoothMove(npc, desiredPos, 0.05f, 15f);

            if (timer == 20 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                Vector2 targetDir = (target.Center - npc.Center).SafeNormalize(Vector2.UnitX);
                Projectile.NewProjectile(
                    npc.GetSource_FromAI(),
                    npc.Center + targetDir * 40f,
                    targetDir,
                    ModContent.ProjectileType<CryogenDarkBeam>(),
                    npc.damage / 2,
                    0f,
                    Main.myPlayer
                );
            }

            // Phase 6 low HP extra fast bolt reward
            if (phase == 6 && timer == 140 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                // Shoot from opposite direction
                float side = Math.Sign(target.Center.X - npc.Center.X);
                Vector2 spawn = target.Center + new Vector2(side * 640f, -100f);
                Vector2 vel = new Vector2(-side * 20f, 0f);
                Projectile.NewProjectile(
                    npc.GetSource_FromAI(),
                    spawn,
                    vel,
                    ModContent.ProjectileType<CryogenLightBeam>(),
                    npc.damage / 2,
                    0f,
                    Main.myPlayer
                );
            }

            if (timer >= duration)
            {
                SelectNextAttack(npc, phase);
            }
        }

        // P2 Attack 8: Starnight Lance
        private void ExecuteState_StarnightLance(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            int duration = 240;

            if (timer < 30)
            {
                // Position side-top
                Vector2 sidePos = target.Center + new Vector2(300f, -200f);
                SmoothMove(npc, sidePos, 0.08f, 18f);
            }
            else if (timer == 30 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                // Setup line 1
                Vector2 dir = (target.Center - npc.Center).SafeNormalize(Vector2.UnitX);
                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, dir, ModContent.ProjectileType<CryogenDaedalusLightning>(), 0, 0f, Main.myPlayer, npc.Center.Y);
            }
            else if (timer == 60 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                // Setup line 2 (angled +25)
                Vector2 dir = (target.Center - npc.Center).SafeNormalize(Vector2.UnitX).RotatedBy(MathHelper.ToRadians(25f));
                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, dir, ModContent.ProjectileType<CryogenDaedalusLightning>(), 0, 0f, Main.myPlayer, npc.Center.Y + 60f);
            }
            else if (timer == 90 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                // Setup line 3 (angled -25)
                Vector2 dir = (target.Center - npc.Center).SafeNormalize(Vector2.UnitX).RotatedBy(MathHelper.ToRadians(-25f));
                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, dir, ModContent.ProjectileType<CryogenDaedalusLightning>(), 0, 0f, Main.myPlayer, npc.Center.Y - 60f);
            }
            else if (timer == 120 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                // Fire beam 1
                Vector2 dir = (target.Center - npc.Center).SafeNormalize(Vector2.UnitX);
                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, dir, ModContent.ProjectileType<CryogenStarnightBeam>(), npc.damage / 2, 0f, Main.myPlayer);
            }
            else if (timer == 145 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                // Fire beam 2
                Vector2 dir = (target.Center - npc.Center).SafeNormalize(Vector2.UnitX).RotatedBy(MathHelper.ToRadians(25f));
                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, dir, ModContent.ProjectileType<CryogenStarnightBeam>(), npc.damage / 2, 0f, Main.myPlayer);
            }
            else if (timer == 170 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                // Fire beam 3
                Vector2 dir = (target.Center - npc.Center).SafeNormalize(Vector2.UnitX).RotatedBy(MathHelper.ToRadians(-25f));
                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, dir, ModContent.ProjectileType<CryogenStarnightBeam>(), npc.damage / 2, 0f, Main.myPlayer);
            }

            if (timer >= duration)
            {
                SelectNextAttack(npc, phase);
            }
        }

        // P2 Attack 9: Shadecrystal Barrage
        private void ExecuteState_ShadecrystalBarrage(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            int duration = 240;

            // Hover tracking
            Vector2 desiredPos = target.Center + new Vector2(0f, -280f);
            SmoothMove(npc, desiredPos, 0.05f, 15f);

            if (timer == 30 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                // First scatter: Center offset left 30 degrees (佯攻)
                Vector2 baseVel = (target.Center - npc.Center).SafeNormalize(Vector2.UnitY).RotatedBy(MathHelper.ToRadians(-30f)) * 11f;
                float spread = MathHelper.ToRadians(5f);

                for (int i = 0; i < 12; i++)
                {
                    Vector2 vel = baseVel.RotatedBy((i - 5.5f) * spread);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, vel, ModContent.ProjectileType<CryogenShadecrystal>(), npc.damage / 3, 0f, Main.myPlayer);
                }
                SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.6f }, npc.Center);
            }
            else if (timer == 90 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                // Second scatter: Target player predicted Y (补位)
                Vector2 predictedPos = target.Center + target.velocity * 18f;
                Vector2 baseVel = (predictedPos - npc.Center).SafeNormalize(Vector2.UnitY) * 13f;
                float spread = MathHelper.ToRadians(4f);

                for (int i = 0; i < 12; i++)
                {
                    Vector2 vel = baseVel.RotatedBy((i - 5.5f) * spread);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, vel, ModContent.ProjectileType<CryogenShadecrystal>(), npc.damage / 3, 0f, Main.myPlayer);
                }
                SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.6f }, npc.Center);
            }

            if (timer >= duration)
            {
                SelectNextAttack(npc, phase);
            }
        }

        // P2 Attack 10: Daedalus Golem Staff
        private void ExecuteState_DaedalusGolemStaff(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            int duration = 440;

            // Slow hover
            Vector2 desiredPos = target.Center + new Vector2(0f, -260f);
            SmoothMove(npc, desiredPos, 0.025f, 8f);

            // Spawn breakable golem NPCs
            if (timer == 1 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.NewNPC(npc.GetSource_FromAI(), (int)npc.Center.X - 100, (int)npc.Center.Y, ModContent.NPCType<CryogenDaedalusMinion>(), npc.whoAmI, npc.whoAmI, 0f, 0f);
                NPC.NewNPC(npc.GetSource_FromAI(), (int)npc.Center.X + 100, (int)npc.Center.Y, ModContent.NPCType<CryogenDaedalusMinion>(), npc.whoAmI, npc.whoAmI, 1f, MathHelper.Pi);
            }

            // Shoot slow LightBeam every 120 frames
            if (timer % 120 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                Vector2 vel = (target.Center - npc.Center).SafeNormalize(Vector2.UnitY) * 5f;
                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, vel, ModContent.ProjectileType<CryogenLightBeam>(), npc.damage / 2, 0f, Main.myPlayer);
            }

            if (timer >= duration)
            {
                SelectNextAttack(npc, phase);
            }
        }

        // P2 Attack 11: Darkecho bow and Shimmerspark Yoyo
        private void ExecuteState_DarkechoAndShimmerspark(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            int duration = 440;

            if (timer == 1)
            {
                // Spawn orbiting yoyo
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, Vector2.Zero, ModContent.ProjectileType<CryogenShimmersparkYoyo>(), npc.damage / 3, 0f, Main.myPlayer, npc.whoAmI);
                }
            }

            if (timer == 60)
            {
                // Fire arrows and crystal dart
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Vector2 baseVel = (target.Center - npc.Center).SafeNormalize(Vector2.UnitY);
                    
                    // Double arrows
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, baseVel.RotatedBy(0.18f) * 11f, ModContent.ProjectileType<CryogenDarkechoArrow>(), npc.damage / 3, 0f, Main.myPlayer);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, baseVel.RotatedBy(-0.18f) * 11f, ModContent.ProjectileType<CryogenDarkechoArrow>(), npc.damage / 3, 0f, Main.myPlayer);

                    // High speed Crystal dart (bounces once)
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, baseVel * 22f, ModContent.ProjectileType<CryogenCrystalDart>(), npc.damage / 2, 0f, Main.myPlayer);
                }
                SoundEngine.PlaySound(SoundID.Item5, npc.Center);
            }
            else if (timer == 190)
            {
                // Position to opposite side of player and repeat
                float side = Math.Sign(target.Center.X - npc.Center.X);
                npc.Center = target.Center + new Vector2(-side * 300f, -200f);
                npc.velocity = Vector2.Zero;
                npc.netUpdate = true;

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Vector2 baseVel = (target.Center - npc.Center).SafeNormalize(Vector2.UnitY);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, baseVel.RotatedBy(0.18f) * 11f, ModContent.ProjectileType<CryogenDarkechoArrow>(), npc.damage / 3, 0f, Main.myPlayer);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, baseVel.RotatedBy(-0.18f) * 11f, ModContent.ProjectileType<CryogenDarkechoArrow>(), npc.damage / 3, 0f, Main.myPlayer);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, baseVel * 22f, ModContent.ProjectileType<CryogenCrystalDart>(), npc.damage / 2, 0f, Main.myPlayer);
                }
                SoundEngine.PlaySound(SoundID.Item5, npc.Center);
            }
            else
            {
                // Keep orbiting
                Vector2 desiredPos = target.Center + new Vector2(MathF.Sin(ticksRunning * 0.02f) * 260f, -180f);
                SmoothMove(npc, desiredPos, 0.04f, 12f);
            }

            if (timer >= duration)
            {
                SelectNextAttack(npc, phase);
            }
        }

        // P2 Attack 12: Crystal Piercer
        private void ExecuteState_CrystalPiercer(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            int duration = 360;

            // Keep hover position
            Vector2 desiredPos = target.Center + new Vector2(0f, -220f);
            SmoothMove(npc, desiredPos, 0.04f, 15f);

            // Group 1: 3 curved gravity javelins from left (Frame 20)
            if (timer == 20 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector2 spawn = target.Center + new Vector2(-600f, -200f + i * 120f);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), spawn, new Vector2(18f, -4f), ModContent.ProjectileType<CryogenJavelin>(), npc.damage / 3, 0f, Main.myPlayer, 0f);
                }
                SoundEngine.PlaySound(SoundID.Item1, npc.Center);
            }

            // Group 2: 3 straight horizontal javelins from right (Frame 80)
            if (timer == 80 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector2 spawn = target.Center + new Vector2(600f, -200f + i * 120f);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), spawn, new Vector2(-16f, 0f), ModContent.ProjectileType<CryogenJavelin>(), npc.damage / 3, 0f, Main.myPlayer, 1f);
                }
                SoundEngine.PlaySound(SoundID.Item1, npc.Center);
            }

            // Group 3: 2 vertical dropping javelins from top (Frame 160)
            if (timer == 160 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                Projectile.NewProjectile(npc.GetSource_FromAI(), target.Center + new Vector2(-180f, -500f), new Vector2(0f, 14f), ModContent.ProjectileType<CryogenJavelin>(), npc.damage / 3, 0f, Main.myPlayer, 2f);
                Projectile.NewProjectile(npc.GetSource_FromAI(), target.Center + new Vector2(180f, -500f), new Vector2(0f, 14f), ModContent.ProjectileType<CryogenJavelin>(), npc.damage / 3, 0f, Main.myPlayer, 2f);
                SoundEngine.PlaySound(SoundID.Item1, npc.Center);
            }

            if (timer >= duration)
            {
                SelectNextAttack(npc, phase);
            }
        }
        #endregion

        #region Phase 3->4 Transition & Victory states
        private void ExecuteState_FreezeTransition(NPC npc, ref float timer)
        {
            npc.damage = 0;
            npc.velocity *= 0.85f;

            int transitionDuration = 40;
            if (targetSubphase == 4)
            {
                transitionDuration = 90; // GFB/Big Transition to Phase 4
            }
            else if (targetSubphase == 2)
            {
                transitionDuration = 30;
            }
            else if (targetSubphase == 6)
            {
                transitionDuration = 50;
            }

            // Flash effect windup
            if (timer < transitionDuration - 10)
            {
                transitionFlashAlpha = MathHelper.Clamp(timer / (transitionDuration - 10f), 0f, 1f);
            }
            else
            {
                transitionFlashAlpha = MathHelper.Clamp((transitionDuration - timer) / 10f, 0f, 1f);
            }

            // Transition shell shatter blast
            if (timer >= transitionDuration)
            {
                transitionFlashAlpha = 0f;
                SoundEngine.PlaySound(TransitionSound, npc.Center);
                SoundEngine.PlaySound(BlastSound, npc.Center);

                Player target = Main.player[npc.target];
                if (target.active && !target.dead)
                {
                    target.Calamity().GeneralScreenShakePower = 12f;
                }

                // Shards explosion on server
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    for (int i = 0; i < 20; i++)
                    {
                        Vector2 vel = (MathHelper.TwoPi * i / 20f).ToRotationVector2() * 6f;
                        Projectile.NewProjectile(
                            npc.GetSource_FromAI(),
                            npc.Center,
                            vel,
                            ModContent.ProjectileType<CryogenJavelinShard>(),
                            npc.damage / 3,
                            0f,
                            Main.myPlayer
                        );
                    }
                }

                EmitCryoDustRing(npc.Center, 60f, 35, 4.5f);
                EmitCryoDustRing(npc.Center, 40f, 25, 3f);

                // Update phase and sync
                npc.ai[0] = targetSubphase;
                
                // Select first attack of the new phase
                AttackState nextState = targetSubphase >= 4 ? AttackState.DarklightGreatsword : AttackState.HoarfrostBow;
                TransitionToAttack(npc, nextState);
            }
        }

        private void ExecuteState_DeathAnimation(NPC npc, ref float timer)
        {
            npc.damage = 0;
            npc.velocity *= 0.88f;
            npc.rotation += 0.05f;

            if (timer % 6 == 0)
            {
                SoundEngine.PlaySound(SoundID.Item27, npc.Center);
                EmitCryoDustRing(npc.Center, 30f, 12, 2.5f);
            }

            if (timer >= 120)
            {
                npc.life = 0;
                npc.HitEffect();
                npc.active = false;
                npc.netUpdate = true;
            }
        }

        private void ExecuteVictoryDespawn(NPC npc)
        {
            npc.velocity.Y -= 0.2f;
            if (npc.velocity.Y < -15f)
            {
                npc.velocity.Y = -15f;
            }

            npc.rotation += 0.05f;

            if (npc.timeLeft > 10)
            {
                npc.timeLeft = 10;
            }
        }
        #endregion

        #region Helper Math & Spawning Functions
        private static void SmoothMove(NPC npc, Vector2 desiredPosition, float acceleration, float maxSpeed)
        {
            Vector2 desiredVelocity = (desiredPosition - npc.Center) * acceleration;
            if (desiredVelocity.Length() > maxSpeed)
            {
                desiredVelocity = Vector2.Normalize(desiredVelocity) * maxSpeed;
            }
            npc.velocity = Vector2.Lerp(npc.velocity, desiredVelocity, 0.12f);
        }

        private static void EmitCryoDustRing(Vector2 center, float radius, int count, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                Vector2 vel = (MathHelper.TwoPi * i / count).ToRotationVector2() * speed;
                Vector2 spawnPos = center + vel.SafeNormalize(Vector2.UnitX) * radius;
                Dust d = Dust.NewDustPerfect(spawnPos, DustID.Ice, vel, 100, Color.Cyan, Main.rand.NextFloat(0.8f, 1.3f));
                d.noGravity = true;
            }
        }
        #endregion

        #region Drawing & Visual Layouts
        public override void FindFrame(NPC npc, int frameHeight)
        {
            npc.frameCounter++;
            if (npc.frameCounter >= 5)
            {
                npc.frame.Y += frameHeight;
                if (npc.frame.Y >= frameHeight * Main.npcFrameCount[npc.type])
                {
                    npc.frame.Y = 0;
                }
                npc.frameCounter = 0;
            }
        }

        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            Texture2D coreTex = TextureAssets.Npc[npc.type].Value;
            Texture2D shieldTex = GetCalamityTexture(ProjShield, coreTex);
            Vector2 drawPos = npc.Center - screenPos;
            int currentSubphase = (int)npc.ai[0];

            // 1. Color shift based on P1 / P2
            Color finalDrawColor = drawColor;
            if (currentSubphase >= 4)
            {
                // Deep blue + purple overlay
                finalDrawColor = Color.Lerp(drawColor, new Color(90, 70, 200), 0.6f) * npc.Opacity;
            }
            else
            {
                // Frost blue overlay
                finalDrawColor = Color.Lerp(drawColor, new Color(200, 240, 255), 0.3f) * npc.Opacity;
            }

            // 2. Draw 10-Hit Shield (Glacial Embrace) in P1
            if (shieldActive && shieldTex != coreTex && currentSubphase <= 3)
            {
                float scale = npc.scale * (1.1f + 0.05f * (float)Math.Sin(ticksRunning * 0.1f));
                Color c = Color.Cyan * shieldChargeProgress * 0.72f * npc.Opacity;
                
                spriteBatch.Draw(
                    shieldTex,
                    drawPos,
                    null,
                    c,
                    shieldRotation,
                    shieldTex.Size() * 0.5f,
                    scale,
                    SpriteEffects.None,
                    0f
                );
            }

            // 3. Draw Core with tilting (摇头晃脑) and breathing
            spriteBatch.Draw(
                coreTex,
                drawPos,
                npc.frame,
                npc.GetAlpha(finalDrawColor),
                npc.rotation,
                npc.frame.Size() * 0.5f,
                npc.scale,
                SpriteEffects.None,
                0f
            );

            // 4. Draw active holding weapon
            DrawActiveWeapon(npc, spriteBatch, screenPos);

            // 5. Draw screen flash overlay
            if (transitionFlashAlpha > 0f)
            {
                Color flashColor = Color.White * transitionFlashAlpha;
                spriteBatch.Draw(TextureAssets.BlackTile.Value, new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), flashColor);
            }

            return false;
        }

        private void DrawActiveWeapon(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos)
        {
            AttackState state = (AttackState)(int)npc.ai[1];
            string weaponName = "";
            float rotOffset = 0f;
            float scaleMultiplier = 1.4f;

            switch (state)
            {
                case AttackState.HoarfrostBow:
                    weaponName = "HoarfrostBow";
                    rotOffset = -MathHelper.PiOver2; // Point up
                    break;
                case AttackState.Icebreaker:
                    weaponName = "Icebreaker";
                    rotOffset = ticksRunning * 0.12f; // Rotate
                    break;
                case AttackState.Avalanche:
                    weaponName = "Avalanche";
                    rotOffset = ticksRunning * 0.18f; // Spin ax
                    break;
                case AttackState.SnowstormStaff:
                    weaponName = "SnowstormStaff";
                    rotOffset = -MathHelper.PiOver4;
                    break;
                case AttackState.DarklightGreatsword:
                    weaponName = "DarklightGreatsword";
                    rotOffset = MathHelper.PiOver4;
                    break;
                case AttackState.StarnightLance:
                    weaponName = "StarnightLance";
                    rotOffset = -MathHelper.PiOver4;
                    break;
                case AttackState.ShadecrystalBarrage:
                    weaponName = "ShadecrystalBarrage";
                    rotOffset = 0f;
                    break;
                case AttackState.DaedalusGolemStaff:
                    weaponName = "DaedalusGolemStaff";
                    rotOffset = -MathHelper.PiOver4;
                    break;
                case AttackState.DarkechoAndShimmerspark:
                    weaponName = "DarkechoGreatbow";
                    rotOffset = -MathHelper.PiOver2;
                    break;
                case AttackState.CrystalPiercer:
                    weaponName = "CrystalPiercer";
                    rotOffset = MathHelper.PiOver4;
                    break;
            }

            if (string.IsNullOrEmpty(weaponName))
                return;

            int itemType = IUMWWeaponBossRegistry.GetItemType(weaponName);
            if (itemType <= 0 || itemType >= TextureAssets.Item.Length)
                return;

            // Load weapon texture
            Main.instance.LoadItem(itemType);
            Texture2D weaponTex = TextureAssets.Item[itemType].Value;
            if (weaponTex == null)
                return;

            Vector2 weaponPos = npc.Center - screenPos + new Vector2(0f, -npc.height * 0.6f);
            float rot = npc.rotation + rotOffset;
            float scale = npc.scale * scaleMultiplier;

            // Draw glowing outline
            int currentPhase = (int)npc.ai[0];
            Color glowColor = currentPhase >= 4 ? Color.Purple * 0.5f : Color.DeepSkyBlue * 0.5f;

            for (int i = 0; i < 8; i++)
            {
                Vector2 offset = (MathHelper.TwoPi * i / 8f).ToRotationVector2() * 2.2f * scale;
                spriteBatch.Draw(
                    weaponTex,
                    weaponPos + offset,
                    null,
                    glowColor * npc.Opacity,
                    rot,
                    weaponTex.Size() * 0.5f,
                    scale,
                    SpriteEffects.None,
                    0f
                );
            }

            // Draw solid body
            spriteBatch.Draw(
                weaponTex,
                weaponPos,
                null,
                Color.White * npc.Opacity,
                rot,
                weaponTex.Size() * 0.5f,
                scale,
                SpriteEffects.None,
                0f
            );
        }

        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);

            Vector2 drawPos = npc.Center - screenPos;
            Texture2D coreTex = TextureAssets.Npc[npc.type].Value;
            int currentSubphase = (int)npc.ai[0];

            // Pulsing core aura
            float pulseScale = 1.0f + 0.08f * (float)Math.Sin(ticksRunning * 0.12f);
            Color auraColor;

            if (currentSubphase < 4)
            {
                // P1: Light blue/cyan pulse
                auraColor = Color.Lerp(Color.DeepSkyBlue, Color.Cyan, 0.5f + 0.5f * (float)Math.Sin(ticksRunning * 0.08f)) * 0.45f * npc.Opacity;
            }
            else
            {
                // P2: Violet/magenta/dark blue pulse
                auraColor = Color.Lerp(Color.Purple, Color.DarkBlue, 0.5f + 0.5f * (float)Math.Sin(ticksRunning * 0.08f)) * 0.55f * npc.Opacity;
            }

            spriteBatch.Draw(
                coreTex,
                drawPos,
                npc.frame,
                auraColor,
                npc.rotation,
                npc.frame.Size() * 0.5f,
                npc.scale * pulseScale,
                SpriteEffects.None,
                0f
            );

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
        }

        private static Texture2D GetCalamityTexture(string path, Texture2D fallback)
        {
            if (ModContent.RequestIfExists<Texture2D>("CalamityMod/NPCs/Cryogen/" + path, out var asset))
            {
                return asset.Value;
            }
            return fallback;
        }
        #endregion
    }
}
