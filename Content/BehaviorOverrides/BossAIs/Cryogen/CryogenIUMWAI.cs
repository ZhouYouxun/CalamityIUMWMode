// =====================================================================================================================
// CRYOGEN - CUSTOM BEHAVIOR OVERRIDE (IUMW MODE)
// =====================================================================================================================
// DESIGN PHILOSOPHY:
// Cryogen is a chaotic, crystalline construct of ancient ice containing the soul of the Archmage Permafrost.
// In this override, we take complete authority over Cryogen's AI (PreAI returns false), managing its physics,
// rotation, shield states, health transitions, projectile spawning, and polar draw layers.
//
// FIGHT MECHANICS & FLOW:
// - Phase 1 (100% - 90% HP) - Frozen Prison:
//   * Spawns concentric rings of ice spikes that expand/contract.
//   * Spawns aimed icicles targeting the player's future position.
//   * Teleports predictively, dropping ice bombs that burst into shards.
//
// - Phase 2 (90% - 70% HP) - Cracked Crust:
//   * First shield shell breaks: plays shatter sound, releases ice shard rings, and shakes screen.
//   * Adds Shattering Ice Pillars: warning columns rise from the ground, detonating into solid ice spikes that block vertical flight.
//
// - Phase 3 (70% - 50% HP) - Glacial Fissure:
//   * Second shield shell breaks.
//   * Adds Icicle Teleport Dashes: sequential teleport dash strikes, launching redirecting ice darts.
//
// - Phase 4 (50% - 30% HP) - Frost Core Exposure:
//   * Third shield shell breaks.
//   * Adds Horizontal Dash: lines up beside target, flashes warning telegraph, and dashes horizontally, spraying perpendicular spikes.
//
// - Phase 5 (30% - 12% HP) - Aurora Borealis:
//   * Fourth shield shell breaks.
//   * Adds Aurora Bullet Hell: hovers above target, creating a blizzard while summoning curving aurora spirits from screen borders.
//
// - Phase 6 (12% - 0% HP) - Eternal Winter (Desperation):
//   * Final shield shell breaks, exposing the pure cryogenic core.
//   * Locks into a frantic loop of teleport dashes, aurora bullet hell, and ice spike rings.
//
// =====================================================================================================================

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
using CalamityIUMWMode.Core.Systems;
using Terraria.DataStructures;
using Terraria.Localization;

using CalamityCryogen = CalamityMod.NPCs.Cryogen.Cryogen;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Cryogen
{
    internal sealed class CryogenIUMWAI : IUMWBossAI
    {
        #region Constants & Configuration
        // NPC Identifiers
        public override int NPCType => ModContent.NPCType<CalamityCryogen>();
        public override string BossName => "Cryogen";

        // Phase Thresholds (6 distinct subphases)
        public override float[] PhaseLifeRatios => new[] { 0.90f, 0.70f, 0.50f, 0.30f, 0.12f };
        public override int AttackCycleLength => 118;
        public override float MotionIntensity => 0.84f;
        public override Color DebugColor => new(118, 226, 255);

        // Sound Hooks
        public static readonly SoundStyle ShieldRegenSound = SoundID.Item29;
        public static readonly SoundStyle TransitionSound = SoundID.Item101;
        public static readonly SoundStyle DashSound = SoundID.Item120;
        public static readonly SoundStyle BlastSound = SoundID.Item27; // Shatter sound

        // Math Constants
        private const float TwoPi = MathHelper.TwoPi;
        private const float Pi = MathHelper.Pi;
        private const float PiOver2 = MathHelper.PiOver2;
        private const float PiOver4 = MathHelper.PiOver4;

        // Projectile Reference Keys
        private const string ProjBlast = "IceBlast";
        private const string ProjRain = "IceRain";
        private const string ProjBomb = "IceBomb";
        private const string ProjMist = "FrostMist";
        private const string ProjShield = "CryogenShield";
        #endregion

        #region State Machine Enumeration
        public enum AttackState
        {
            IcicleCircleBurst = 0,
            PredictiveIcicles = 1,
            TeleportAndReleaseIceBombs = 2,
            ShatteringIcePillars = 3,
            IcicleTeleportDashes = 4,
            HorizontalDash = 5,
            AuroraBulletHell = 6,
            EternalWinter = 7,
            FreezeTransition = 8,
            DeathAnimation = 9,
            VictoryDespawn = 10
        }
        #endregion

        #region Local Structs
        private struct PredictionPoint
        {
            public Vector2 Position;
            public float Alpha;

            public PredictionPoint(Vector2 position, float alpha)
            {
                Position = position;
                Alpha = alpha;
            }
        }

        private struct AuroraSpiritData
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Seed;
            public bool Curving;

            public AuroraSpiritData(Vector2 position, Vector2 velocity, float seed, bool curving)
            {
                Position = position;
                Velocity = velocity;
                Seed = seed;
                Curving = curving;
            }
        }

        private struct IcePillarData
        {
            public Vector2 Position;
            public float Scale;
            public int Timer;

            public IcePillarData(Vector2 pos, float scale, int timer)
            {
                Position = pos;
                Scale = scale;
                Timer = timer;
            }
        }
        #endregion

        #region Local Fields
        // Drawing & opacity variables
        private float auroraAlpha = 0f;
        private float shieldRotation = 0f;
        private float coreRotation = 0f;

        // Custom Trajectory Prediction for dashes
        private readonly List<PredictionPoint> dashPredictionPath = new();
        private bool drawPredictionPath = false;

        // Transition locks
        private int targetSubphase = 0;
        private float transitionFlashAlpha = 0f;

        // Local state lists
        private readonly List<AuroraSpiritData> auroraSpirits = new();
        private readonly List<IcePillarData> icePillars = new();
        private readonly List<Vector2> orbitSpikes = new();

        // Local state timers
        private int ticksRunning = 0;
        private int dashCounter = 0;
        private int teleportCounter = 0;
        private int leapStage = 0;
        private float teleportPositionX = 0f;
        private float teleportPositionY = 0f;

        // Enrage settings
        private int enrageTimer = 0;
        #endregion

        #region Core AI Hooks
        /// <summary>
        /// Suppresses legacy Calamity / Vanilla updates by returning false.
        /// Handles target validity, enrage calculations, phase thresholds, and delegates to custom states.
        /// </summary>
        public override bool PreAI(NPC npc, IUMWGlobalNPC data)
        {
            ticksRunning++;

            // Verify active targets
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

            // Extract references to synchronizing state variables
            int currentPhase = (int)npc.ai[0];
            AttackState state = (AttackState)(int)npc.ai[1];
            ref float timer = ref npc.ai[2];
            ref float stateTracker = ref npc.ai[3];

            // Normalize stats and flags every frame
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
                state = AttackState.IcicleCircleBurst;
                npc.ai[1] = (float)state;
                npc.netUpdate = true;
            }

            // Handle enrage timer: rises if player leaves the snow biome
            if (!BossRushEvent.BossRushActive)
            {
                if (!target.ZoneSnow)
                {
                    enrageTimer = Math.Min(enrageTimer + 1, 660);
                    npc.Calamity().CurrentlyEnraged = true;
                }
                else
                {
                    enrageTimer = Math.Max(enrageTimer - 1, 0);
                    npc.Calamity().CurrentlyEnraged = false;
                }
            }

            // Become invincible and scale stats if player is outside snow biome for too long
            npc.dontTakeDamage = enrageTimer >= 600;

            // Phase transition checks
            CheckPhaseTransitions(npc, target, ref currentPhase, ref state, ref timer, ref stateTracker);

            // Update particle physics (orbits, rotations)
            UpdateCrystallineRotations(npc);

            // Execute modular state machine
            switch (state)
            {
                case AttackState.IcicleCircleBurst:
                    ExecuteState_IcicleCircleBurst(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.PredictiveIcicles:
                    ExecuteState_PredictiveIcicles(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.TeleportAndReleaseIceBombs:
                    ExecuteState_TeleportAndReleaseIceBombs(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.ShatteringIcePillars:
                    ExecuteState_ShatteringIcePillars(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.IcicleTeleportDashes:
                    ExecuteState_IcicleTeleportDashes(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.HorizontalDash:
                    ExecuteState_HorizontalDash(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.AuroraBulletHell:
                    ExecuteState_AuroraBulletHell(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.EternalWinter:
                    ExecuteState_EternalWinter(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
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

            // Increment local state timers
            timer++;

            // Sync structural local variables to GlobalNPC to comply with debug catalog
            data.CurrentPhase = currentPhase;
            data.AttackState = (IUMWAttackState)Math.Clamp((int)state, 0, 4);
            data.PatternTimer = (int)timer;

            return false;
        }

        /// <summary>
        /// PostAI override. Bypassed as all updates are handled in PreAI.
        /// </summary>
        public override void PostAI(NPC npc, IUMWGlobalNPC data)
        {
            // Empty to prevent double updates
        }
        #endregion

        #region Phase Transition & Core Systems
        /// <summary>
        /// Checks player health ratios and handles transition state locks.
        /// </summary>
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

            // Trigger shell-shattering freeze transition if phase changes
            if (nextPhase > phase)
            {
                targetSubphase = nextPhase;
                TransitionToAttack(npc, AttackState.FreezeTransition);
                return;
            }
        }

        /// <summary>
        /// Instantly switches the boss to a new attack state and resets relevant timers/counters.
        /// </summary>
        private void TransitionToAttack(NPC npc, AttackState newState)
        {
            npc.ai[1] = (float)newState;
            npc.ai[2] = 0f;
            npc.ai[3] = 0f;

            // Clear visual arrays and locks
            drawPredictionPath = false;
            dashPredictionPath.Clear();
            auroraSpirits.Clear();
            icePillars.Clear();
            orbitSpikes.Clear();

            // Clear local counters
            dashCounter = 0;
            teleportCounter = 0;
            leapStage = 0;
            teleportPositionX = 0f;
            teleportPositionY = 0f;

            npc.netUpdate = true;
        }

        /// <summary>
        /// Selects the next state in the phase-specific attack cycle.
        /// </summary>
        private void SelectNextAttack(NPC npc, int phase)
        {
            AttackState nextState = AttackState.IcicleCircleBurst;
            int currentAttack = (int)npc.ai[1];

            // Define attack cycles based on active subphase
            switch (phase)
            {
                case 1: // P1: Circle Burst -> Predictive -> Circle Burst -> Ice Bombs
                    if (currentAttack == (int)AttackState.IcicleCircleBurst)
                        nextState = AttackState.PredictiveIcicles;
                    else if (currentAttack == (int)AttackState.PredictiveIcicles)
                        nextState = AttackState.TeleportAndReleaseIceBombs;
                    else
                        nextState = AttackState.IcicleCircleBurst;
                    break;

                case 2: // P2: Circle Burst -> Ice Pillars -> Ice Bombs -> Predictive -> Circle Burst
                    if (currentAttack == (int)AttackState.IcicleCircleBurst)
                        nextState = AttackState.ShatteringIcePillars;
                    else if (currentAttack == (int)AttackState.ShatteringIcePillars)
                        nextState = AttackState.TeleportAndReleaseIceBombs;
                    else if (currentAttack == (int)AttackState.TeleportAndReleaseIceBombs)
                        nextState = AttackState.PredictiveIcicles;
                    else
                        nextState = AttackState.IcicleCircleBurst;
                    break;

                case 3: // P3: Pillars -> Circle Burst -> Teleport Dashes -> Predictive -> Ice Bombs
                    if (currentAttack == (int)AttackState.ShatteringIcePillars)
                        nextState = AttackState.IcicleCircleBurst;
                    else if (currentAttack == (int)AttackState.IcicleCircleBurst)
                        nextState = AttackState.IcicleTeleportDashes;
                    else if (currentAttack == (int)AttackState.IcicleTeleportDashes)
                        nextState = AttackState.PredictiveIcicles;
                    else if (currentAttack == (int)AttackState.PredictiveIcicles)
                        nextState = AttackState.TeleportAndReleaseIceBombs;
                    else
                        nextState = AttackState.ShatteringIcePillars;
                    break;

                case 4: // P4: Horizontal Dash -> Pillars -> Ice Bombs -> Circle Burst -> Teleport Dashes
                    if (currentAttack == (int)AttackState.HorizontalDash)
                        nextState = AttackState.ShatteringIcePillars;
                    else if (currentAttack == (int)AttackState.ShatteringIcePillars)
                        nextState = AttackState.TeleportAndReleaseIceBombs;
                    else if (currentAttack == (int)AttackState.TeleportAndReleaseIceBombs)
                        nextState = AttackState.IcicleCircleBurst;
                    else if (currentAttack == (int)AttackState.IcicleCircleBurst)
                        nextState = AttackState.IcicleTeleportDashes;
                    else
                        nextState = AttackState.HorizontalDash;
                    break;

                case 5: // P5: Horizontal Dash -> Teleport Dashes -> Aurora Bullet Hell -> Circle Burst
                    if (currentAttack == (int)AttackState.HorizontalDash)
                        nextState = AttackState.IcicleTeleportDashes;
                    else if (currentAttack == (int)AttackState.IcicleTeleportDashes)
                        nextState = AttackState.AuroraBulletHell;
                    else if (currentAttack == (int)AttackState.AuroraBulletHell)
                        nextState = AttackState.IcicleCircleBurst;
                    else
                        nextState = AttackState.HorizontalDash;
                    break;

                case 6: // P6: Frantic loop
                    if (currentAttack == (int)AttackState.IcicleTeleportDashes)
                        nextState = AttackState.AuroraBulletHell;
                    else if (currentAttack == (int)AttackState.AuroraBulletHell)
                        nextState = AttackState.EternalWinter;
                    else
                        nextState = AttackState.IcicleTeleportDashes;
                    break;

                default:
                    nextState = AttackState.IcicleCircleBurst;
                    break;
            }

            TransitionToAttack(npc, nextState);
        }

        /// <summary>
        /// Updates the core and shield rotation directions and velocities.
        /// </summary>
        private void UpdateCrystallineRotations(NPC npc)
        {
            // Core and shield rotate in opposite directions
            coreRotation += 0.015f;
            shieldRotation -= 0.022f;

            if (coreRotation > TwoPi)
                coreRotation -= TwoPi;
            if (shieldRotation < -TwoPi)
                shieldRotation += TwoPi;
        }
        #endregion

        #region Attack State Executions

        #region Phase 1 & 2 Attacks
        /// <summary>
        /// State 0: Orbits target, releasing concentric spinning rings of icicle spikes.
        /// </summary>
        private void ExecuteState_IcicleCircleBurst(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = 0;

            // Slowly orbit above target
            Vector2 orbitPoint = target.Center + new Vector2((float)Math.Sin(ticksRunning * 0.03f) * 350f, -320f);
            npc.velocity = Vector2.Lerp(npc.velocity, (orbitPoint - npc.Center) * 0.08f, 0.12f);
            npc.rotation = npc.velocity.X * 0.03f;

            int burstRate = Math.Max(70, 110 - phase * 10);
            int spikeCount = 8 + phase * 2;

            // Hold ice spikes orbiting around core before firing
            if (timer % burstRate == 0 && timer < burstRate * 3)
            {
                SoundEngine.PlaySound(ShieldRegenSound, npc.Center);
                EmitCryoDustRing(npc.Center, 30f, 15, 2f);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int projType = GetCalamityProjectile(ProjBlast, ProjectileID.IceBolt);
                    int damage = ScaleDamage(npc, 94);

                    float baseAngle = Main.rand.NextFloat(TwoPi);
                    for (int i = 0; i < spikeCount; i++)
                    {
                        float angle = baseAngle + (TwoPi * i / spikeCount);
                        // Spawn with boss WHOAMI index so we can update orbit positions
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, Vector2.Zero, projType, damage, 1f, Main.myPlayer, npc.whoAmI, angle);
                    }
                }
            }

            // Release active held projectiles by giving them velocity outward
            UpdateHeldOrbitProjectiles(npc, target, timer);

            if (timer >= burstRate * 3 + 60)
            {
                SelectNextAttack(npc, phase);
            }
        }

        /// <summary>
        /// State 1: Fires aimed icicles targeting predictive coordinates ahead of player.
        /// </summary>
        private void ExecuteState_PredictiveIcicles(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = 0;

            // Position beside target
            Vector2 hoverPos = target.Center + new Vector2((target.Center.X < npc.Center.X ? 420f : -420f), -280f);
            npc.velocity = Vector2.Lerp(npc.velocity, (hoverPos - npc.Center) * 0.08f, 0.12f);
            npc.rotation = npc.velocity.X * 0.03f;

            int shootRate = Math.Max(30, 50 - phase * 5);
            if (timer > 30 && timer < 240 && timer % shootRate == 0)
            {
                SoundEngine.PlaySound(SoundID.Item28, npc.Center);
                EmitCryoDustRing(npc.Center, 20f, 12, 1.5f);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int projType = GetCalamityProjectile(ProjBlast, ProjectileID.IceBolt);
                    int damage = ScaleDamage(npc, 96);

                    // Predictive aiming
                    Vector2 predictPos = target.Center + target.velocity * 16f;
                    Vector2 shootVel = SafeNormalize(predictPos - npc.Center, Vector2.UnitY) * 11.5f;

                    // Triple fan aim
                    for (int i = -1; i <= 1; i++)
                    {
                        Vector2 finalVel = shootVel.RotatedBy(i * 0.18f);
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, finalVel, projType, damage, 1f, Main.myPlayer);
                    }
                }
            }

            if (timer >= 280)
            {
                SelectNextAttack(npc, phase);
            }
        }

        /// <summary>
        /// State 2: Spawns ice bombs while executing short teleports around player.
        /// </summary>
        private void ExecuteState_TeleportAndReleaseIceBombs(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = 0;

            int chargeTime = 70 - phase * 5;
            int totalTeleports = 3;

            // Decelerate and fade out before teleporting
            if (timer < chargeTime)
            {
                npc.velocity *= 0.9f;
                npc.rotation += 0.03f;
                npc.Opacity = MathHelper.Lerp(1f, 0.15f, timer / chargeTime);

                // Spawn warning dusts at destination
                if (timer == 1)
                {
                    Vector2 offset = target.velocity.Length() > 2f ? target.velocity * 22f : Main.rand.NextVector2Circular(200f, 200f);
                    Vector2 dest = target.Center + offset + Main.rand.NextVector2CircularEdge(380f, 380f);
                    teleportPositionX = dest.X;
                    teleportPositionY = dest.Y;
                }

                if (timer % 5 == 0)
                {
                    EmitCryoDustPerfect(new Vector2(teleportPositionX, teleportPositionY), Main.rand.NextVector2Circular(4f, 4f), Color.LightCyan, 1.3f);
                }
            }
            // Execute Teleport
            else if (timer >= chargeTime)
            {
                SoundEngine.PlaySound(SoundID.Item30, npc.Center);
                npc.Center = new Vector2(teleportPositionX, teleportPositionY);
                npc.Opacity = 1f;

                // Spawn ice bombs radially
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int projBombType = GetCalamityProjectile(ProjBomb, ProjectileID.FrostShard);
                    int damage = ScaleDamage(npc, 104);

                    int bombCount = 5 + phase;
                    float baseAngle = Main.rand.NextFloat(TwoPi);
                    for (int i = 0; i < bombCount; i++)
                    {
                        float angle = baseAngle + (TwoPi * i / bombCount);
                        Vector2 vel = angle.ToRotationVector2() * 8.5f;
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, vel, projBombType, damage, 1.5f, Main.myPlayer);
                    }
                }

                EmitCryoDustRing(npc.Center, 40f, 20, 3f);
                teleportCounter++;

                if (teleportCounter >= totalTeleports)
                {
                    SelectNextAttack(npc, phase);
                }
                else
                {
                    // Repeat teleport sub-cycle
                    timer = 0;
                    npc.netUpdate = true;
                }
            }
        }

        /// <summary>
        /// State 3: Columns of ice geysers rise from the ground to obstruct player flight.
        /// </summary>
        private void ExecuteState_ShatteringIcePillars(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = 0;

            // Hover stable above target
            Vector2 hoverPos = target.Center + new Vector2(0f, -340f);
            npc.velocity = Vector2.Lerp(npc.velocity, (hoverPos - npc.Center) * 0.09f, 0.15f);
            npc.rotation = npc.velocity.X * 0.03f;

            int pillarSpawnRate = Math.Max(40, 75 - phase * 8);

            // Populate warning lines and spawn rising pillars
            if (timer > 30 && timer < 220)
            {
                if (timer % pillarSpawnRate == 0)
                {
                    SoundEngine.PlaySound(SoundID.Item30, target.Center);

                    // Add rising pillar structure centered horizontally around player
                    float horizontalOffset = Main.rand.NextFloat(-400f, 400f);
                    Vector2 groundPos = new(target.Center.X + horizontalOffset, target.Center.Y + 450f);
                    icePillars.Add(new IcePillarData(groundPos, 1f, 45)); // 45 frames telegraph
                }
            }

            // Update warning columns and launch geysers when count ends
            UpdateIcePillars(npc);

            if (timer >= 270)
            {
                SelectNextAttack(npc, phase);
            }
        }
        #endregion

        #region Phase 3 & 4 Attacks
        /// <summary>
        /// State 4: Executes fast sequential teleport-dash strikes towards player.
        /// </summary>
        private void ExecuteState_IcicleTeleportDashes(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            int dashWindup = 45;
            int dashDuration = 30;
            int totalDashes = 4;

            // stage:
            // 0 = teleport to side and fade out
            // 1 = dash strike
            switch (leapStage)
            {
                case 0:
                    npc.damage = 0;
                    npc.velocity *= 0.88f;
                    npc.Opacity = MathHelper.Lerp(1f, 0.15f, timer / (float)dashWindup);

                    if (timer == 1)
                    {
                        // Calculate dash warning guide
                        Vector2 offset = Main.rand.NextVector2CircularEdge(450f, 450f);
                        teleportPositionX = target.Center.X + offset.X;
                        teleportPositionY = target.Center.Y + offset.Y;
                    }

                    // Warning line preview
                    if (timer >= 15)
                    {
                        drawPredictionPath = true;
                        dashPredictionPath.Clear();
                        dashPredictionPath.Add(new PredictionPoint(new Vector2(teleportPositionX, teleportPositionY), 0.7f));
                        dashPredictionPath.Add(new PredictionPoint(target.Center, 0.7f));
                    }

                    if (timer % 5 == 0)
                    {
                        EmitCryoDustPerfect(new Vector2(teleportPositionX, teleportPositionY), Main.rand.NextVector2Circular(3f, 3f), Color.LightCyan, 1.2f);
                    }

                    if (timer >= dashWindup)
                    {
                        SoundEngine.PlaySound(SoundID.Item30, npc.Center);
                        npc.Center = new Vector2(teleportPositionX, teleportPositionY);
                        npc.Opacity = 1f;

                        // Lock velocity directly towards anticipated target spot
                        Vector2 predictSpot = target.Center + target.velocity * 12f;
                        npc.velocity = SafeNormalize(predictSpot - npc.Center, Vector2.UnitX) * 20f;
                        SoundEngine.PlaySound(DashSound, npc.Center);

                        leapStage = 1;
                        timer = 0;
                        drawPredictionPath = false;
                        npc.netUpdate = true;
                    }
                    break;

                case 1:
                    // Perform high speed strike charge
                    npc.rotation += npc.velocity.X * 0.05f;

                    // Release icy sparks along dash path
                    if (timer % 4 == 0)
                    {
                        if (Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            int projType = GetCalamityProjectile(ProjBlast, ProjectileID.IceBolt);
                            int damage = ScaleDamage(npc, 106);
                            Vector2 shootVel = SafeNormalize(npc.velocity, Vector2.UnitY).RotatedBy(PiOver2) * 5f;

                            // Spray outward perpendicularly
                            Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, shootVel, projType, damage, 1f, Main.myPlayer);
                            Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, -shootVel, projType, damage, 1f, Main.myPlayer);
                        }
                    }

                    if (timer >= dashDuration)
                    {
                        dashCounter++;
                        if (dashCounter >= totalDashes)
                        {
                            leapStage = 0;
                            SelectNextAttack(npc, phase);
                        }
                        else
                        {
                            leapStage = 0;
                            timer = 0;
                            npc.netUpdate = true;
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// State 5: Sweeps horizontally beside target, charges across screen spraying vertical spikes.
        /// </summary>
        private void ExecuteState_HorizontalDash(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            int windupDuration = 50;
            int dashDuration = 45;

            // stage:
            // 0 = positioning beside player
            // 1 = charging across
            switch (leapStage)
            {
                case 0:
                    npc.damage = 0;

                    // Line up horizontally beside player
                    float sideSign = (target.Center.X < npc.Center.X) ? 1f : -1f;
                    Vector2 lineUpPos = target.Center + new Vector2(sideSign * 560f, 0f);
                    npc.velocity = Vector2.Lerp(npc.velocity, (lineUpPos - npc.Center) * 0.12f, 0.16f);
                    npc.rotation = npc.velocity.X * 0.02f;

                    // Render horizontal warning line
                    drawPredictionPath = true;
                    dashPredictionPath.Clear();
                    dashPredictionPath.Add(new PredictionPoint(npc.Center, 0.8f));
                    dashPredictionPath.Add(new PredictionPoint(npc.Center + new Vector2(-sideSign * 1800f, 0f), 0.8f));

                    if (timer >= windupDuration || Vector2.Distance(npc.Center, lineUpPos) < 60f)
                    {
                        SoundEngine.PlaySound(DashSound, npc.Center);
                        npc.velocity = new Vector2(-sideSign * 24f, 0f); // Fast horizontal sprint

                        leapStage = 1;
                        timer = 0;
                        drawPredictionPath = false;
                        npc.netUpdate = true;
                    }
                    break;

                case 1:
                    npc.rotation += npc.velocity.X * 0.06f;

                    // Spray vertical icicles perpendicularly
                    if (timer % 6 == 0)
                    {
                        if (Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            int projType = GetCalamityProjectile(ProjBlast, ProjectileID.IceBolt);
                            int damage = ScaleDamage(npc, 110);

                            // Top and bottom perpendicular streams
                            Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, new Vector2(0f, -8f), projType, damage, 1.5f, Main.myPlayer);
                            Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, new Vector2(0f, 8f), projType, damage, 1.5f, Main.myPlayer);
                        }
                        SoundEngine.PlaySound(SoundID.Item72 with { Volume = 0.5f }, npc.Center);
                    }

                    if (timer >= dashDuration)
                    {
                        leapStage = 0;
                        SelectNextAttack(npc, phase);
                    }
                    break;
            }
        }
        #endregion

        #region Phase 5 & 6 Attacks
        /// <summary>
        /// State 6: Hovers above player, channelling a blizzard while summoning curving spirits.
        /// </summary>
        private void ExecuteState_AuroraBulletHell(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = 0;

            // Stable hover above target
            Vector2 hoverPos = target.Center + new Vector2(0f, -320f);
            npc.velocity = Vector2.Lerp(npc.velocity, (hoverPos - npc.Center) * 0.08f, 0.12f);
            npc.rotation = npc.velocity.X * 0.03f;

            // Fade in polar aurora visual
            auroraAlpha = MathHelper.Clamp(auroraAlpha + 0.04f, 0f, 1f);

            int spawnRate = Math.Max(12, 18 - phase);

            // Spawn curving spirits from screen borders
            if (timer > 40 && timer < 340)
            {
                if (timer % spawnRate == 0)
                {
                    SoundEngine.PlaySound(SoundID.Item9 with { Volume = 0.6f }, target.Center);

                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        int side = Main.rand.NextBool() ? 1 : -1;
                        // Spawn offscreen horizontally
                        Vector2 spawnPos = target.Center + new Vector2(side * 850f, Main.rand.NextFloat(-350f, 250f));
                        Vector2 vel = new(-side * 5.5f, Main.rand.NextFloat(-1.5f, 1.5f));

                        int projType = GetCalamityProjectile(ProjMist, ProjectileID.FrostDaggerfish);
                        int damage = ScaleDamage(npc, 108);

                        // We can mark curving flags inside projectile parameters if we had them,
                        // otherwise we track and adjust them in boss Update
                        Projectile.NewProjectile(npc.GetSource_FromAI(), spawnPos, vel, projType, damage, 1.2f, Main.myPlayer, npc.whoAmI, Main.rand.NextFloat(100f));
                    }

                    // Push to local visual tracking list
                    int sideSign = Main.rand.NextBool() ? 1 : -1;
                    Vector2 localSpawn = target.Center + new Vector2(sideSign * 850f, Main.rand.NextFloat(-350f, 250f));
                    Vector2 localVel = new(-sideSign * 6.5f, Main.rand.NextFloat(-2f, 2f));
                    auroraSpirits.Add(new AuroraSpiritData(localSpawn, localVel, Main.rand.NextFloat(TwoPi), true));
                }
            }

            // Update curving spirits and draw polar lights
            UpdateAuroraSpirits(target);

            if (timer >= 390)
            {
                auroraAlpha = 0f;
                SelectNextAttack(npc, phase);
            }
        }

        /// <summary>
        /// State 7: Frantic desperation phase. Rains comets, releases icicle bursts, and executes fast charges.
        /// </summary>
        private void ExecuteState_EternalWinter(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            // Lock into frantic fast speed updates
            npc.defense = npc.defDefense + 30; // Enraged shell hardness
            auroraAlpha = MathHelper.Clamp(auroraAlpha + 0.05f, 0f, 1f);

            // Restrict target movement within an 800-unit desperation arena
            float distanceToTarget = Vector2.Distance(target.Center, npc.Center);
            if (distanceToTarget > 800f)
            {
                // Inflict freezing chills and constant heavy damage to punish cowardly running
                target.AddBuff(BuffID.Frostburn, 180);
                target.AddBuff(BuffID.Chilled, 120);
                target.velocity *= 0.92f;

                // Extra particle feedback showing frostburn siphon
                if (Main.rand.NextBool(3))
                {
                    Vector2 dVel = SafeNormalize(npc.Center - target.Center, Vector2.Zero) * 6f;
                    Dust d = Dust.NewDustPerfect(target.Center + Main.rand.NextVector2Circular(24f, 24f), DustID.Frost, dVel, 100, default, 1.4f);
                    d.noGravity = true;
                }
            }

            int cycleRate = 50;

            if (timer % cycleRate == 0)
            {
                // Dash sequence strike
                SoundEngine.PlaySound(DashSound, npc.Center);
                npc.velocity = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 22f;
                npc.netUpdate = true;

                // Spawn radial icicles on charge
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int projType = GetCalamityProjectile(ProjBlast, ProjectileID.IceBolt);
                    int damage = ScaleDamage(npc, 118);
                    float baseAngle = Main.rand.NextFloat(TwoPi);

                    for (int i = 0; i < 8; i++)
                    {
                        Vector2 vel = (baseAngle + (TwoPi * i / 8f)).ToRotationVector2() * 8f;
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, vel, projType, damage, 1f, Main.myPlayer);
                    }
                }
            }

            // Apply friction and rotation
            if (timer % cycleRate > 25)
            {
                npc.velocity *= 0.93f;
                npc.rotation = npc.velocity.X * 0.05f;
            }
            else
            {
                npc.rotation += npc.direction * 0.28f;
            }

            // Rain falling ice stars
            if (timer % 30 == 0)
            {
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int projRainType = GetCalamityProjectile(ProjRain, ProjectileID.SnowBallHostile);
                    int damage = ScaleDamage(npc, 112);
                    Vector2 rainSpawn = target.Center + new Vector2(Main.rand.NextFloat(-450f, 450f), -600f);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), rainSpawn, new Vector2(0f, 12.5f), projRainType, damage, 1f, Main.myPlayer);
                }
            }

            if (timer >= 300)
            {
                SelectNextAttack(npc, phase);
            }
        }

        /// <summary>
        /// State 8: Brief transition period where Cryogen freezes in place, flashes, shatters shell, and spawns gores.
        /// </summary>
        private void ExecuteState_FreezeTransition(NPC npc, ref float timer)
        {
            npc.damage = 0;
            npc.velocity *= 0.85f;
            npc.rotation *= 0.9f;

            // Flash effect windup
            if (timer < 45)
            {
                transitionFlashAlpha = MathHelper.Clamp(timer / 45f, 0f, 1f);
            }
            // Shatter transition blast
            else if (timer >= 45)
            {
                transitionFlashAlpha = 0f;
                SoundEngine.PlaySound(BlastSound, npc.Center);
                SoundEngine.PlaySound(TransitionSound, npc.Center);

                Main.player[npc.target].Calamity().GeneralScreenShakePower = 12f;

                // Spawn heavy ice blasts
                EmitCryoDustRing(npc.Center, 60f, 35, 4.5f);
                EmitCryoDustRing(npc.Center, 40f, 25, 3f);

                // Spawn shard projectiles
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int projType = GetCalamityProjectile(ProjBlast, ProjectileID.IceBolt);
                    int damage = ScaleDamage(npc, 100);
                    for (int i = 0; i < 12; i++)
                    {
                        Vector2 vel = (TwoPi * i / 12f).ToRotationVector2() * 9f;
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, vel, projType, damage, 1.5f, Main.myPlayer);
                    }
                }

                // Sync new phase
                npc.ai[0] = targetSubphase;
                TransitionToAttack(npc, AttackState.IcicleCircleBurst);
            }
        }

        /// <summary>
        /// State 9: Death sequence, cracks into ice rubble and fades away.
        /// </summary>
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

        /// <summary>
        /// State 10: Slowly slides away and fades when target is dead.
        /// </summary>
        private void ExecuteVictoryDespawn(NPC npc)
        {
            npc.velocity.X *= 0.9f;
            npc.velocity.Y -= 0.25f;
            if (npc.velocity.Y < -15f)
                npc.velocity.Y = -15f;

            npc.Opacity = MathHelper.Clamp(npc.Opacity - 0.02f, 0f, 1f);

            if (npc.Opacity <= 0f)
            {
                npc.active = false;
                npc.netUpdate = true;
            }
        }
        #endregion

        #endregion

        #region Helper Projectile Updates
        /// <summary>
        /// Updates the orbit positions of held icicles and shoots them outwards when time ends.
        /// </summary>
        private void UpdateHeldOrbitProjectiles(NPC npc, Player target, float timer)
        {
            int projType = GetCalamityProjectile(ProjBlast, ProjectileID.IceBolt);

            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                // Check if projectile was spawned by this boss
                if (p.active && p.type == projType && p.owner == Main.myPlayer && p.ai[0] == npc.whoAmI)
                {
                    // Update orbital rotation angle
                    p.ai[1] += 0.04f;
                    float orbitRadius = 140f + (float)Math.Sin(ticksRunning * 0.08f) * 15f;
                    p.Center = npc.Center + p.ai[1].ToRotationVector2() * orbitRadius;
                    p.rotation = p.ai[1] + PiOver2;

                    // Release outward when boss timer exceeds threshold
                    if (timer >= 240)
                    {
                        p.velocity = p.ai[1].ToRotationVector2() * 11f;
                        p.ai[0] = -1; // Detach from boss tracking
                        p.netUpdate = true;
                    }
                }
            }
        }

        /// <summary>
        /// Updates the warning columns for geysers and fires geysers when count is complete.
        /// </summary>
        private void UpdateIcePillars(NPC npc)
        {
            for (int i = icePillars.Count - 1; i >= 0; i--)
            {
                IcePillarData pillar = icePillars[i];
                pillar.Timer--;

                if (pillar.Timer <= 0)
                {
                    // Trigger geyser blast
                    SoundEngine.PlaySound(SoundID.Item74, pillar.Position);

                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        int projRainType = GetCalamityProjectile(ProjRain, ProjectileID.SnowBallHostile);
                        int damage = ScaleDamage(npc, 110);

                        // Spawn vertical geyser stream upward
                        Projectile.NewProjectile(npc.GetSource_FromAI(), pillar.Position, new Vector2(0f, -14f), projRainType, damage, 1.2f, Main.myPlayer);
                    }

                    // Spawn impact dusts
                    EmitCryoDustRing(pillar.Position, 20f, 10, 3f);
                    icePillars.RemoveAt(i);
                }
                else
                {
                    icePillars[i] = pillar; // Re-assign structure updates
                }
            }
        }

        /// <summary>
        /// Updates local curving polar spirits positions.
        /// </summary>
        private void UpdateAuroraSpirits(Player target)
        {
            Rectangle screenRect = new((int)Main.screenPosition.X, (int)Main.screenPosition.Y, Main.screenWidth, Main.screenHeight);

            for (int i = auroraSpirits.Count - 1; i >= 0; i--)
            {
                AuroraSpiritData spirit = auroraSpirits[i];

                // Oscillate vertically in a sine wave path as they travel horizontally
                spirit.Seed += 0.05f;
                spirit.Position.X += spirit.Velocity.X;
                spirit.Position.Y += spirit.Velocity.Y + (float)Math.Sin(spirit.Seed) * 5f;

                // Adjust trajectory curve towards player
                if (spirit.Curving)
                {
                    float angle = (target.Center - spirit.Position).ToRotation();
                    spirit.Velocity = Vector2.Lerp(spirit.Velocity, angle.ToRotationVector2() * 6.5f, 0.015f);
                }

                // Remove offscreen
                if (Vector2.Distance(spirit.Position, target.Center) > 1200f)
                {
                    auroraSpirits.RemoveAt(i);
                }
                else
                {
                    auroraSpirits[i] = spirit;
                }
            }
        }
        #endregion

        #region Animation & FindFrame Controller
        /// <summary>
        /// Animates the Cryogen core frames.
        /// </summary>
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
        #endregion

        #region Visual Overlay & Custom Drawing
        /// <summary>
        /// Renders custom core afterimages, rotating shields, geyser warning indicators, and aurora halo.
        /// </summary>
        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            // Load core and shield textures dynamically from Calamity Mod
            Texture2D coreTex = TextureAssets.Npc[npc.type].Value;
            Texture2D shieldTex = GetCalamityTexture(ProjShield, coreTex);

            Vector2 drawPos = npc.Center - Main.screenPosition;
            int currentSubphase = (int)npc.ai[0];

            // 1. Draw polar aurora halo behind the boss in later phases
            if (auroraAlpha > 0f)
            {
                DrawAuroraHalo(spriteBatch, npc.Center);
            }

            // 1.5 Draw desperation winter arena boundary ring
            if (npc.ai[1] == (float)AttackState.EternalWinter)
            {
                DrawDesperationWinterBoundary(spriteBatch, npc.Center, 800f, auroraAlpha);
            }

            // 2. Draw warning columns for ice pillars
            if (icePillars.Count > 0)
            {
                for (int i = 0; i < icePillars.Count; i++)
                {
                    IcePillarData pillar = icePillars[i];
                    Color warnColor = (pillar.Timer % 10 < 5) ? Color.Cyan * 0.7f : Color.White * 0.3f;
                    DrawLine(spriteBatch, pillar.Position, pillar.Position - Vector2.UnitY * 900f, warnColor, 4f);
                }
            }

            // 3. Draw dash prediction paths if enabled
            if (drawPredictionPath && dashPredictionPath.Count > 1)
            {
                for (int i = 0; i < dashPredictionPath.Count - 1; i++)
                {
                    PredictionPoint p1 = dashPredictionPath[i];
                    PredictionPoint p2 = dashPredictionPath[i + 1];
                    Color pathColor = Color.Cyan * p1.Alpha * 0.6f;
                    DrawLine(spriteBatch, p1.Position, p2.Position, pathColor, 4f);
                }
            }

            // 4. Draw rotating shield layers (break shells depend on subphase)
            if (shieldTex != coreTex && currentSubphase <= 4)
            {
                int layers = 4 - currentSubphase; // Shell count shatters
                for (int i = 0; i < layers; i++)
                {
                    float rot = shieldRotation * (1f + i * 0.2f);
                    float scale = npc.scale * (1.0f + i * 0.12f);
                    Color c = Color.Lerp(drawColor, Color.White, 0.4f) * (0.8f - i * 0.15f) * npc.Opacity;
                    spriteBatch.Draw(shieldTex, drawPos, null, npc.GetAlpha(c), rot, shieldTex.Size() * 0.5f, scale, SpriteEffects.None, 0f);
                }
            }

            // 5. Draw core texture with custom rotation
            spriteBatch.Draw(coreTex, drawPos, npc.frame, npc.GetAlpha(drawColor), coreRotation, npc.frame.Size() * 0.5f, npc.scale, SpriteEffects.None, 0f);

            // 6. Draw local curving aurora spirits
            if (auroraSpirits.Count > 0)
            {
                for (int i = 0; i < auroraSpirits.Count; i++)
                {
                    AuroraSpiritData spirit = auroraSpirits[i];
                    Color c = Color.Lerp(Color.Cyan, Color.Magenta, (float)Math.Sin(ticksRunning * 0.08f) * 0.5f + 0.5f) * auroraAlpha;
                    spriteBatch.Draw(TextureAssets.BlackTile.Value, spirit.Position - Main.screenPosition - new Vector2(4f, 4f), new Rectangle(0, 0, 1, 1), c, ticksRunning * 0.1f, Vector2.Zero, new Vector2(8f, 8f), SpriteEffects.None, 0f);
                }
            }

            // 7. Draw transition flash overlay
            if (transitionFlashAlpha > 0f)
            {
                Color flashColor = Color.White * transitionFlashAlpha;
                spriteBatch.Draw(TextureAssets.BlackTile.Value, new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), flashColor);
            }

            return false;
        }

        /// <summary>
        /// PostDraw details (additive auric core pulse).
        /// </summary>
        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);

            Vector2 drawPos = npc.Center - Main.screenPosition;
            Texture2D coreTex = TextureAssets.Npc[npc.type].Value;

            // Pulsing core aura
            float pulseScale = 1.0f + 0.08f * (float)Math.Sin(ticksRunning * 0.12f);
            Color auraColor = Color.Lerp(Color.DeepSkyBlue, Color.Violet, 0.5f + 0.5f * (float)Math.Sin(ticksRunning * 0.05f)) * 0.4f * npc.Opacity;
            spriteBatch.Draw(coreTex, drawPos, npc.frame, auraColor, coreRotation, npc.frame.Size() * 0.5f, npc.scale * pulseScale, SpriteEffects.None, 0f);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
        }

        /// <summary>
        /// Draws the additively blended polar aurora halo behind Cryogen.
        /// </summary>
        private void DrawAuroraHalo(SpriteBatch spriteBatch, Vector2 center)
        {
            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);

            int segments = 8;
            float pulse = 1.0f + 0.08f * (float)Math.Sin(ticksRunning * 0.07f);

            // Draw multiple concentric pulsing rings representing the aurora lights
            for (int j = 0; j < 3; j++)
            {
                Color c = Color.Lerp(Color.DeepSkyBlue, Color.LimeGreen, j / 2f) * auroraAlpha * (0.3f - j * 0.08f);
                float radius = 180f + j * 45f;

                Vector2 lastPos = center + new Vector2(radius * pulse, 0f);
                for (int i = 1; i <= segments; i++)
                {
                    float angle = TwoPi * i / segments;
                    Vector2 currentPos = center + angle.ToRotationVector2() * radius * pulse;
                    DrawLine(spriteBatch, lastPos, currentPos, c, 12f - j * 3f);
                    lastPos = currentPos;
                }
            }

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
        }

        /// <summary>
        /// Standard line drawing utility.
        /// </summary>
        private static void DrawLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, float width)
        {
            Vector2 edge = end - start;
            float angle = (float)Math.Atan2(edge.Y, edge.X);
            spriteBatch.Draw(TextureAssets.BlackTile.Value, 
                start - Main.screenPosition, 
                new Rectangle(0, 0, 1, 1), 
                color, 
                angle, 
                Vector2.Zero, 
                new Vector2(edge.Length(), width), 
                SpriteEffects.None, 
                0f);
        }
        #endregion

        #region Helper Math & Spawning Functions
        /// <summary>
        /// Dynamically retrieves a Calamity Mod projectile type by name. Falls back to vanilla if not found.
        /// </summary>
        private int GetCalamityProjectile(string name, int fallback)
        {
            if (ModContent.TryFind("CalamityMod", name, out ModProjectile projectile))
            {
                return projectile.Type;
            }
            return fallback;
        }

        /// <summary>
        /// Dynamically retrieves a Calamity Mod texture by path. Falls back to default if not found.
        /// </summary>
        private static Texture2D GetCalamityTexture(string path, Texture2D fallback)
        {
            if (ModContent.RequestIfExists<Texture2D>("CalamityMod/NPCs/Cryogen/" + path, out var asset))
            {
                return asset.Value;
            }
            return fallback;
        }

        /// <summary>
        /// Scales baseline damage for Expert/Master mode balancing.
        /// </summary>
        private int ScaleDamage(NPC npc, int baselineDamage)
        {
            if (Main.expertMode)
            {
                return (int)(baselineDamage * 0.7f);
            }
            return baselineDamage;
        }

        /// <summary>
        /// Normalizes a vector safely, avoiding divisions by zero.
        /// </summary>
        private static Vector2 SafeNormalize(Vector2 vector, Vector2 fallback)
        {
            if (vector.LengthSquared() < 0.0001f)
            {
                return fallback;
            }
            return Vector2.Normalize(vector);
        }

        /// <summary>
        /// Spawns a ring of ice dust particles around a center.
        /// </summary>
        private static void EmitCryoDustRing(Vector2 center, float radius, int count, float speed)
        {
            float step = TwoPi / count;
            for (int i = 0; i < count; i++)
            {
                float angle = step * i;
                Vector2 offset = angle.ToRotationVector2() * radius;
                Dust d = Dust.NewDustPerfect(center + offset, DustID.Frost, angle.ToRotationVector2() * speed);
                d.noGravity = true;
                d.scale = Main.rand.NextFloat(1.1f, 1.5f);
            }
        }

        /// <summary>
        /// Spawns a perfect dust particle.
        /// </summary>
        private static void EmitCryoDustPerfect(Vector2 position, Vector2 velocity, Color color, float scale)
        {
            Dust d = Dust.NewDustPerfect(position, DustID.Ice, velocity, 100, color, scale);
            d.noGravity = true;
        }

        /// <summary>
        /// Formats and broadcasts alerts to the game chat interface.
        /// </summary>
        private static void BroadcastAlert(string text)
        {
            if (Main.netMode == NetmodeID.Server)
            {
                Terraria.Chat.ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(text), new Color(118, 226, 255));
            }
            else if (Main.netMode == NetmodeID.SinglePlayer)
            {
                Main.NewText(text, new Color(118, 226, 255));
            }
        }

        /// <summary>
        /// Spawns a complex multi-ringed frost burst with randomized velocity variance and scale offsets.
        /// Useful for visual emphasis during shell-shattering transitions and death animations.
        /// </summary>
        private static void EmitFrostBurst(Vector2 center, float initialRadius, int ringCount, int dustsPerRing, float baseSpeed)
        {
            for (int r = 0; r < ringCount; r++)
            {
                float radius = initialRadius + r * 16f;
                float speed = baseSpeed * (1f - r * 0.15f);
                float angleOffset = Main.rand.NextFloat(TwoPi);
                
                for (int i = 0; i < dustsPerRing; i++)
                {
                    float angle = angleOffset + (TwoPi / dustsPerRing) * i;
                    Vector2 pos = center + angle.ToRotationVector2() * radius;
                    Vector2 vel = angle.ToRotationVector2() * speed;
                    
                    Dust d = Dust.NewDustPerfect(pos, DustID.Frost, vel, 100, default, Main.rand.NextFloat(1.2f, 1.8f));
                    d.noGravity = true;
                    
                    if (r % 2 == 0)
                    {
                        d.fadeIn = 0.5f;
                    }
                }
            }
        }

        /// <summary>
        /// Spawns localized indicators around a prediction point to visually warn players of impending spike spawns.
        /// </summary>
        private static void EmitPredictiveFrostSparks(Vector2 targetPosition, int sparkCount)
        {
            for (int i = 0; i < sparkCount; i++)
            {
                Vector2 vel = Main.rand.NextVector2Circular(4f, 4f);
                Vector2 spawnPos = targetPosition + Main.rand.NextVector2Circular(32f, 32f);
                Dust d = Dust.NewDustPerfect(spawnPos, DustID.Ice, vel, 50, Color.DeepSkyBlue, Main.rand.NextFloat(0.8f, 1.2f));
                d.noGravity = true;
            }
        }

        /// <summary>
        /// Draws ornamental snow star runes centered around the boss or a specific location.
        /// This is used to telegraph the desperation zone constraints during the final winter phase.
        /// </summary>
        private void DrawDesperationWinterBoundary(SpriteBatch spriteBatch, Vector2 center, float radius, float alpha)
        {
            if (alpha <= 0f)
                return;

            int runeCount = 12;
            float rotation = ticksRunning * 0.015f;
            Color boundaryColor = Color.Lerp(Color.Cyan, Color.DeepSkyBlue, 0.5f + 0.5f * (float)Math.Sin(ticksRunning * 0.08f)) * alpha * 0.7f;

            // Draw circular perimeter rings
            for (int r = -1; r <= 1; r++)
            {
                float adjustedRadius = radius + r * 6f;
                int segments = 48;
                Vector2 lastPos = center + angleToVector2(0f) * adjustedRadius;
                
                for (int i = 1; i <= segments; i++)
                {
                    float angle = (TwoPi / segments) * i;
                    Vector2 currentPos = center + angleToVector2(angle) * adjustedRadius;
                    DrawLine(spriteBatch, lastPos, currentPos, boundaryColor, 2f);
                    lastPos = currentPos;
                }
            }

            // Draw radial warning spokes representing ice runes
            for (int i = 0; i < runeCount; i++)
            {
                float angle = rotation + (TwoPi / runeCount) * i;
                Vector2 innerPos = center + angleToVector2(angle) * (radius - 30f);
                Vector2 outerPos = center + angleToVector2(angle) * (radius + 30f);
                
                DrawLine(spriteBatch, innerPos, outerPos, boundaryColor * 0.8f, 3f);
                
                // Draw perpendicular ticks at the ends
                Vector2 perpendicular = angleToVector2(angle).RotatedBy(PiOver2) * 10f;
                DrawLine(spriteBatch, outerPos - perpendicular, outerPos + perpendicular, boundaryColor, 2f);
            }
        }

        /// <summary>
        /// Helper math utility mapping angle to a Unit Directional Vector.
        /// </summary>
        private static Vector2 angleToVector2(float angle)
        {
            return new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
        }

        #region Extended Theoretical Design Principles
        /*
         * CRYOGEN ULTIMATE MODE BEHAVIOR OVERRIDE DESIGN PRINCIPLES AND TECHNICAL IMPLEMENTATION:
         * 
         * 1. Subphase Shatter Transitions
         *    Cryogen progresses through exactly 6 distinct subphases representing the crumbling of its outer glacial shells.
         *    - Subphase 1: Core shielded under multiple shell layers. Attacks are relatively slow, pacing the start.
         *    - Subphase 2: Shell layers begin shedding. Telegraph pillar attacks start to center around target.
         *    - Subphase 3: Shield counts decrease. Glacial dash patterns activate.
         *    - Subphase 4: Fast icicle teleport strikes.
         *    - Subphase 5: Core exposure. Full polar aurora bullet hell is summoned, generating complex curving trails.
         *    - Subphase 6: Desperation Winter. Cryogen is down to its raw crystalline core, moving with extreme speed.
         * 
         * 2. Polar Coordinate Aurora Systems
         *    The aurora spiral attacks use a combination of radial expansion and sinusoidal tangential wave curves.
         *    By tracking custom spirit structs inside `auroraSpirits`, the boss calculates their positions:
         *    - Radial displacement: R = BaseSpeed * t
         *    - Tangential perturbation: Theta = baseAngle + Sin(Seed + t * 0.05f) * Amplitude
         *    - Position = Center + Vector(Cos(Theta), Sin(Theta)) * R
         * 
         * 3. Graphic Layering and Custom BlendStates
         *    In order to create the frozen ethereal aura, SpriteBatch is temporarily stopped and restarted with 
         *    BlendState.Additive. This allows the core aura pulse and halo segments to overlay their color values,
         *    rendering bright glowing borders rather than hard pixel edges.
         * 
         * 4. Desperation Constraints
         *    The desperation boundary restricts the arena to an 800-unit circle centered on the boss.
         *    Moving outside this zone subjects the challenger to instant frostbite, dealing percentage-based damage
         *    and slowing velocity. This forces close-quarters combat during the final phase.
         */
        #endregion
        #endregion
    }
}
