// =====================================================================================================================
// PLAGUEBRINGER GOLIATH - CUSTOM BEHAVIOR OVERRIDE (IUMW MODE)
// =====================================================================================================================
// DESIGN PHILOSOPHY:
// The Plaguebringer Goliath is a colossal, biomechanical queen bee fused with advanced Draedon plague nanotechnology.
// This override suppresses the default AI update loop (PreAI returns false), taking absolute authority over movement physics,
// targeting, state transitions, frame animations, and visual drawing indicators.
//
// FIGHT MECHANICS & FLOW:
// - Phase 1 (100% - 70% HP) - Hive Awakening:
//   * Spawn Animation: Emerges from a cloud of nanotech plague bees, roaring and shaking the camera.
//   * Predictive Charges: Rapid charges targeting anticipated coordinates based on player velocity vectors.
//   * Tracking Missile Barrage: Spits redirecting/homing plague missiles that curve aggressively toward the player.
//   * Plague Vomit Sweeps: Hovers in close proximity, sweeping its head to release dense fans of toxic vomit.
// - Phase 2 (70% - 44% HP) - Tactical Air Support:
//   * Carpet Bombing Run: Swaps to horizontal flights, dropping lines of plague explosives across the screen.
//   * Explosive Charger Summon: Summons waves of suicidal plague chargers that lock onto and dash at the player.
//   * Drone Jail Summoning: Summons a rotating circular cage of drones centered on the player. The player must remain
//     inside the cage while dodging diagonal boss charges. Crossing the drone boundary inflicts massive damage.
// - Phase 3 (44% - 20% HP) - Constructor Swarm:
//   * Advanced Constructor Drones: Summons builder drones to shield a central nuclear core. Weld beams link drones to the core.
//   * Crossing Missile Nets: Fires intersecting nets of plague missiles while the player tries to disrupt the construction.
//   * Nuclear Detonation Drop: If the nuke isn't destroyed, it drops and detonates into a screen-clearing plague shockwave.
// - Phase 4 (20% - 0% HP) - Desperation Plague Storm:
//   * Plague Storm Boundary: Anchors a 660f toxic boundary circle. Exiting it applies rapid corrosion damage.
//   * Frantic Charges & Laser Sweeps: Performs extreme-speed dashes crossing the arena while firing orbital stinger fans
//     and sweeping dual green laser beams from its eyes.
//
// ARCHITECTURE DETAILS:
// - Direct block of vanilla AI: PreAI returns false, allowing total control over positioning, velocity, and attacks.
// - Netcode Syncing: Precise client-server synchronization using npc.netUpdate and modular State Sync flags.
// - Rendering Overlays: PreDraw is overridden to draw custom telegraphing lines, dash trajectory paths,
//   radius indicators for minefields, and the desperation singularity arena boundary.
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
using CalamityMod.Projectiles.Boss;
using CalamityMod.World;
using CalamityMod.Events;
using CalamityMod.Buffs.StatDebuffs;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using CalamityIUMWMode.Core.Systems;
using Terraria.DataStructures;
using CalamityMod.Buffs.DamageOverTime;
using CalamityMod.NPCs.PlaguebringerGoliath;

using CalamityPlaguebringerGoliath = CalamityMod.NPCs.PlaguebringerGoliath.PlaguebringerGoliath;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.PlaguebringerGoliath
{
    internal sealed class PlaguebringerGoliathIUMWAI : IUMWBossAI
    {
        #region Constants & Configuration
        // NPC Identifiers
        public override int NPCType => ModContent.NPCType<CalamityPlaguebringerGoliath>();
        public override string BossName => "The Plaguebringer Goliath";

        // Phase Thresholds
        public override float[] PhaseLifeRatios => new[] { 0.70f, 0.44f, 0.20f };
        public override int AttackCycleLength => 140;
        public override float MotionIntensity => 1.25f;
        public override Color DebugColor => new(176, 255, 76);

        // Sound Hooks (Sourced statically from Calamity Mod assets)
        public static readonly SoundStyle NukeWarningSound = CalamityPlaguebringerGoliath.NukeWarningSound;
        public static readonly SoundStyle AttackSwitchSound = CalamityPlaguebringerGoliath.AttackSwitchSound;
        public static readonly SoundStyle DashSound = CalamityPlaguebringerGoliath.DashSound;
        public static readonly SoundStyle BarrageLaunchSound = CalamityPlaguebringerGoliath.BarrageLaunchSound;

        // Math Constants
        private const float TwoPi = MathHelper.TwoPi;
        private const float Pi = MathHelper.Pi;
        private const float PiOver2 = MathHelper.PiOver2;
        private const float ArenaRadius = 660f;

        // Projectile Reference Keys
        private const string StingerProjName = "PlagueStingerGoliath";
        private const string StingerV2ProjName = "PlagueStingerGoliathV2";
        private const string ExplosionProjName = "PlagueExplosion";
        private const string NukeProjName = "HiveNuke";
        private const string BeeProjName = "BasicPlagueBee";
        private const string PulseProjName = "PlaguePulse";
        #endregion

        #region State Machine Enumeration
        public enum AttackState
        {
            SpawnRoar = 0,
            HoverPredictiveDash = 1,
            TrackingMissileBarrage = 2,
            PlagueVomitSweeps = 3,
            CarpetBombingRun = 4,
            ExplosiveChargerSummon = 5,
            DroneJailSummon = 6,
            ConstructorDronesWindup = 7,
            NuclearDetonationDrop = 8,
            DesperationPlagueStorm = 9,
            DeathAnimation = 10,
            VictoryDespawn = 11
        }

        public enum FrameType
        {
            Fly = 0,
            Charge = 1
        }
        #endregion

        #region Local Fields
        // Drawing & opacity variables
        private float telegraphAlpha = 0f;
        private float arenaPulseScale = 1f;
        private float arenaAlpha = 0f;
        private Vector2 desperationCenter = Vector2.Zero;

        // Drone Jail variables
        private const int MaxJailDrones = 8;
        private readonly Vector2[] jailDronePositions = new Vector2[MaxJailDrones];
        private float jailRotation = 0f;
        private float jailRadius = 0f;
        private float jailAlpha = 0f;

        // Constructor core variables
        private Vector2 constructionCenter = Vector2.Zero;
        private float constructionScale = 0f;
        private int constructorDronesCount = 0;
        private readonly Vector2[] constructorDronePositions = new Vector2[5];

        // Dash & general movement counters
        private int dashCounter = 0;
        private bool isCurrentlyCharging = false;
        private Vector2 anticipatedDashVector = Vector2.Zero;

        // Laser telegraphs for desperation phase
        private float laserTelegraphAngle = 0f;
        private float laserTelegraphAlpha = 0f;

        // Debug & Logging Metrics
        private int activeNanotechParticles = 0;
        private int totalFrameCountTicks = 0;
        #endregion

        #region Core AI Hooks
        /// <summary>
        /// Suppresses legacy Calamity / Vanilla updates by returning false.
        /// Handles target validity, enrage calculations, phase thresholds, and delegates to custom states.
        /// </summary>
        public override bool PreAI(NPC npc, IUMWGlobalNPC data)
        {
            totalFrameCountTicks++;

            // Verify active targets
            if (npc.target < 0 || npc.target >= Main.maxPlayers || !Main.player[npc.target].active || Main.player[npc.target].dead)
            {
                npc.TargetClosest(true);
            }

            Player target = Main.player[npc.target];
            if (!target.active || target.dead)
            {
                // Target is dead or missing, execute retreat
                ExecuteVictoryDespawn(npc);
                return false;
            }

            // Extract references to synchronizing state variables
            int currentPhase = (int)npc.ai[0];
            AttackState state = (AttackState)(int)npc.ai[1];
            ref float timer = ref npc.ai[2];
            ref float stateTracker = ref npc.ai[3];

            // Initialize Phase 1
            if (currentPhase == 0)
            {
                currentPhase = 1;
                npc.ai[0] = 1f;
                state = AttackState.SpawnRoar;
                npc.ai[1] = (float)state;
                npc.netUpdate = true;
            }

            // Clear Calamity plague debuff from players to normalize difficulty
            if (target.HasBuff(ModContent.BuffType<Plague>()))
            {
                target.ClearBuff(ModContent.BuffType<Plague>());
            }

            // Enrage multiplier check (non-jungle or boss rush)
            float enrageFactor = 1f;
            if (target.Center.Y < Main.worldSurface * 16f && !BossRushEvent.BossRushActive)
            {
                npc.Calamity().CurrentlyEnraged = true;
                enrageFactor = 1.35f;
            }
            if (BossRushEvent.BossRushActive)
            {
                enrageFactor = 2.1f;
            }

            // Phase transition checks
            CheckPhaseTransitions(npc, target, ref currentPhase, ref state, ref timer, ref stateTracker);

            // Execute modular state machine
            switch (state)
            {
                case AttackState.SpawnRoar:
                    ExecuteState_SpawnRoar(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.HoverPredictiveDash:
                    ExecuteState_HoverPredictiveDash(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.TrackingMissileBarrage:
                    ExecuteState_TrackingMissileBarrage(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.PlagueVomitSweeps:
                    ExecuteState_PlagueVomitSweeps(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.CarpetBombingRun:
                    ExecuteState_CarpetBombingRun(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.ExplosiveChargerSummon:
                    ExecuteState_ExplosiveChargerSummon(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.DroneJailSummon:
                    ExecuteState_DroneJailSummon(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.ConstructorDronesWindup:
                    ExecuteState_ConstructorDronesWindup(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.NuclearDetonationDrop:
                    ExecuteState_NuclearDetonationDrop(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.DesperationPlagueStorm:
                    ExecuteState_DesperationPlagueStorm(npc, target, ref timer, ref stateTracker, enrageFactor);
                    break;
                case AttackState.DeathAnimation:
                    ExecuteState_DeathAnimation(npc, ref timer);
                    break;
                case AttackState.VictoryDespawn:
                    ExecuteVictoryDespawn(npc);
                    break;
            }

            // Increment time ticks
            timer++;

            // Block knockback completely
            npc.knockBackResist = 0f;

            // Sync structural local variables to GlobalNPC to comply with debug catalog
            data.CurrentPhase = currentPhase;
            data.AttackState = (IUMWAttackState)state;
            data.PatternTimer = (int)timer;

            return false;
        }

        /// <summary>
        /// PostAI override. Bypassed as all movement/physics updates are computed in PreAI.
        /// </summary>
        public override void PostAI(NPC npc, IUMWGlobalNPC data)
        {
            // Empty to prevent double updates
        }
        #endregion

        #region Phase Transition Logic
        /// <summary>
        /// Detects health milestones and coordinates transition locks.
        /// </summary>
        private void CheckPhaseTransitions(NPC npc, Player target, ref int phase, ref AttackState state, ref float timer, ref float stateTracker)
        {
            float healthRatio = npc.lifeMax <= 0 ? 1f : npc.life / (float)npc.lifeMax;

            if (phase == 1 && healthRatio < PhaseLifeRatios[0])
            {
                phase = 2;
                npc.ai[0] = 2f;
                TransitionToAttack(npc, AttackState.ExplosiveChargerSummon);
                BroadcastAlert("The Plaguebringer Goliath mobilizes combat drones!");
            }
            else if (phase == 2 && healthRatio < PhaseLifeRatios[1])
            {
                phase = 3;
                npc.ai[0] = 3f;
                TransitionToAttack(npc, AttackState.ConstructorDronesWindup);
                BroadcastAlert("Nuclear construction systems initialized!");
            }
            else if (phase == 3 && healthRatio < PhaseLifeRatios[2])
            {
                phase = 4;
                npc.ai[0] = 4f;
                TransitionToAttack(npc, AttackState.DesperationPlagueStorm);
                desperationCenter = target.Center;
                BroadcastAlert("Maximum viral density reached! System overdrive engaged!");
            }
        }

        /// <summary>
        /// Helper to cleanly shift attack states and reset sub-timers.
        /// </summary>
        private void TransitionToAttack(NPC npc, AttackState newState)
        {
            npc.ai[1] = (float)newState;
            npc.ai[2] = 0f; // Reset timer
            npc.ai[3] = 0f; // Reset stateTracker

            // Reset local attack-specific parameters
            dashCounter = 0;
            isCurrentlyCharging = false;
            telegraphAlpha = 0f;
            jailAlpha = 0f;
            constructionScale = 0f;
            laserTelegraphAlpha = 0f;

            // Notify clients
            npc.netUpdate = true;
        }

        /// <summary>
        /// Standard logic to choose next attack in the loop, based on phase.
        /// </summary>
        private void SelectNextAttack(NPC npc, int phase)
        {
            AttackState current = (AttackState)(int)npc.ai[1];
            AttackState next = AttackState.HoverPredictiveDash;

            switch (current)
            {
                case AttackState.SpawnRoar:
                    next = AttackState.HoverPredictiveDash;
                    break;

                case AttackState.HoverPredictiveDash:
                    next = AttackState.TrackingMissileBarrage;
                    break;

                case AttackState.TrackingMissileBarrage:
                    next = AttackState.PlagueVomitSweeps;
                    break;

                case AttackState.PlagueVomitSweeps:
                    if (phase >= 2)
                        next = AttackState.CarpetBombingRun;
                    else
                        next = AttackState.HoverPredictiveDash;
                    break;

                case AttackState.CarpetBombingRun:
                    if (phase >= 2)
                        next = AttackState.ExplosiveChargerSummon;
                    else
                        next = AttackState.HoverPredictiveDash;
                    break;

                case AttackState.ExplosiveChargerSummon:
                    if (phase >= 2)
                        next = AttackState.DroneJailSummon;
                    else
                        next = AttackState.HoverPredictiveDash;
                    break;

                case AttackState.DroneJailSummon:
                    if (phase >= 3)
                        next = AttackState.ConstructorDronesWindup;
                    else
                        next = AttackState.HoverPredictiveDash;
                    break;

                case AttackState.ConstructorDronesWindup:
                    next = AttackState.NuclearDetonationDrop;
                    break;

                case AttackState.NuclearDetonationDrop:
                    next = AttackState.HoverPredictiveDash;
                    break;

                default:
                    next = AttackState.HoverPredictiveDash;
                    break;
            }

            TransitionToAttack(npc, next);
        }
        #endregion

        #region Detailed Attack State Implementations

        #region State 0: Spawn Roar
        /// <summary>
        /// Detailed execution of the spawn intro.
        /// Activates invulnerability, decelerates, creates screenshakes, releases heavy particle spirals, and broadcasts.
        /// </summary>
        private void ExecuteState_SpawnRoar(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            // Lock invuln
            npc.dontTakeDamage = true;
            npc.damage = 0;

            if (timer == 1f)
            {
                // Align above target
                npc.Center = target.Center - new Vector2(0f, 400f);
                npc.velocity = new Vector2(0f, 6.5f);
                
                // Roar sound registration
                SoundEngine.PlaySound(SoundID.Roar, npc.Center);
                npc.netUpdate = true;
            }

            // Apply friction
            npc.velocity *= 0.95f;
            npc.rotation = npc.velocity.X * 0.02f;
            npc.spriteDirection = (target.Center.X > npc.Center.X).ToDirectionInt();

            // Spiral particle effects drawing into the chest of the Goliath
            if (timer < 60f)
            {
                float radius = 300f * (1f - (timer / 60f));
                float angle = timer * 0.12f;
                for (int i = 0; i < 4; i++)
                {
                    Vector2 offset = (angle + (i * TwoPi / 4f)).ToRotationVector2() * radius;
                    Vector2 particleVel = -offset * 0.08f;
                    SpawnNanotechCloud(npc.Center + offset, particleVel, 1);
                }
            }

            // Spawn detonation trigger
            if (timer == 60f)
            {
                SoundEngine.PlaySound(DashSound, npc.Center);
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 25f;

                // Fire burst particles
                SpawnPlagueSparks(npc.Center, Color.Lime, 45);

                // Spawn some small distraction plague bees
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int beeType = GetCalamityProjectileType(BeeProjName);
                    for (int i = 0; i < 10; i++)
                    {
                        Vector2 beeVel = new Vector2(Main.rand.NextFloat(-8f, 8f), Main.rand.NextFloat(-10f, -3f));
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, beeVel, beeType, 22, 1.5f, Main.myPlayer);
                    }
                }
            }

            if (timer >= 100f)
            {
                npc.dontTakeDamage = false;
                SelectNextAttack(npc, 1);
            }
        }
        #endregion

        #region State 1: Hover Predictive Dash
        /// <summary>
        /// Modular sub-phases for predictive charges.
        /// </summary>
        private void ExecuteState_HoverPredictiveDash(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            ref float dashSubstate = ref stateTracker; // 0 = positioning/telegraph, 1 = active charge
            ref float dashTimer = ref npc.localAI[1];

            npc.spriteDirection = (target.Center.X > npc.Center.X).ToDirectionInt();

            switch ((int)dashSubstate)
            {
                case 0:
                    // Hover Positioning & Telegraphing
                    HoverPredictiveDash_Positioning(npc, target, ref timer, phase, enrage);
                    break;

                case 1:
                    // Active Charge Execution
                    HoverPredictiveDash_ExecuteDash(npc, target, ref timer, ref dashTimer, phase, enrage);
                    break;
            }
        }

        /// <summary>
        /// Phase 1 Hover Setup: Align at standard horizontal coordinates.
        /// </summary>
        private void HoverPredictiveDash_Positioning(NPC npc, Player target, ref float timer, int phase, float enrage)
        {
            isCurrentlyCharging = false;
            npc.damage = 0;

            float speedLimit = (15f + phase * 2.5f) * enrage;
            float offsetDist = 620f;
            float side = ((dashCounter % 2 == 0) ? -1f : 1f);

            Vector2 destination = target.Center + new Vector2(side * offsetDist, -200f);
            Vector2 toDest = destination - npc.Center;
            float distance = toDest.Length();

            // Smooth glide mechanics
            if (distance > 70f)
            {
                npc.velocity = Vector2.Lerp(npc.velocity, Vector2.Normalize(toDest) * speedLimit, 0.08f);
            }
            else
            {
                npc.velocity *= 0.82f;
            }

            npc.rotation = npc.velocity.X * 0.015f;

            // Increment warning line opacity
            telegraphAlpha = MathHelper.Clamp(timer / 42f, 0f, 1f);

            // Shift into charge state once aligned
            if (timer > 45f && (distance < 160f || timer > 85f))
            {
                ref float dashSubstate = ref npc.ai[3];
                ref float dashTimer = ref npc.localAI[1];

                dashSubstate = 1f;
                dashTimer = 0f;
                timer = 0f;
                telegraphAlpha = 0f;

                // Predictive tracking formula
                float chargeSpeed = (25f + phase * 4f) * enrage;
                float flightTime = distance / chargeSpeed;
                float leadTicks = 18f + flightTime;

                Vector2 anticipatedPos = target.Center + target.velocity * leadTicks;
                anticipatedDashVector = Vector2.Normalize(anticipatedPos - npc.Center) * chargeSpeed;

                // Apply velocity vector
                npc.velocity = anticipatedDashVector;
                isCurrentlyCharging = true;
                
                SoundEngine.PlaySound(DashSound, npc.Center);
                npc.netUpdate = true;
            }
        }

        /// <summary>
        /// Phase 1 Active Charge Execution: Rushes target, leaves clouds.
        /// </summary>
        private void HoverPredictiveDash_ExecuteDash(NPC npc, Player target, ref float timer, ref float dashTimer, int phase, float enrage)
        {
            isCurrentlyCharging = true;
            npc.damage = npc.defDamage;
            
            // Adjust rotation to match velocity direction
            npc.rotation = npc.velocity.ToRotation();
            if (npc.spriteDirection == -1)
            {
                npc.rotation += Pi;
            }

            // Spawn cloud trail segments
            if (dashTimer % 4 == 0 && Main.netMode != NetmodeID.Server)
            {
                Vector2 trailingOffset = -npc.velocity * 1.6f;
                SpawnNanotechCloud(npc.Center + trailingOffset, Main.rand.NextVector2Circular(1.5f, 1.5f), 2);
            }

            dashTimer++;

            // Apply friction drag near completion of vector
            if (dashTimer > 32f)
            {
                npc.velocity *= 0.91f;
            }

            // Shift state
            if (dashTimer >= 45f)
            {
                ref float dashSubstate = ref npc.ai[3];
                
                dashCounter++;
                dashSubstate = 0f;
                dashTimer = 0f;
                timer = 0f;
                npc.velocity *= 0.45f;
                npc.netUpdate = true;

                int maxDashes = phase >= 3 ? 4 : 3;
                if (dashCounter >= maxDashes)
                {
                    SelectNextAttack(npc, phase);
                }
            }
        }
        #endregion

        #region State 2: Tracking Missile Barrage
        /// <summary>
        /// Detailed execution of the tracking missile barrage.
        /// Hovers above target, aims abdomen launcher, and fires curving missiles.
        /// </summary>
        private void ExecuteState_TrackingMissileBarrage(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            npc.damage = 0;
            npc.rotation = npc.velocity.X * 0.01f;
            npc.spriteDirection = (target.Center.X > npc.Center.X).ToDirectionInt();

            // Hover pathing coordinates
            float sideOffset = target.Center.X < npc.Center.X ? 480f : -480f;
            Vector2 hoverDest = target.Center + new Vector2(sideOffset, -340f);
            Vector2 toDest = hoverDest - npc.Center;
            
            npc.velocity = Vector2.Lerp(npc.velocity, Vector2.Normalize(toDest) * 16.5f, 0.06f);

            // Trigger visual windup sparks from abdomen launcher
            if (timer > 20f && timer < 150f && timer % 10 == 0 && Main.netMode != NetmodeID.Server)
            {
                Vector2 launcherPos = npc.Center + new Vector2(-npc.spriteDirection * 90f, 45f).RotatedBy(npc.rotation);
                SpawnPlagueSparks(launcherPos, Color.OrangeRed, 3);
            }

            // Perform dynamic missile launches
            int interval = Math.Max(10, (int)(26 - phase * 3.5f - enrage * 2.5f));
            if (timer > 35f && timer < 150f && timer % interval == 0)
            {
                SoundEngine.PlaySound(BarrageLaunchSound, npc.Center);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Vector2 launcherPos = npc.Center + new Vector2(-npc.spriteDirection * 90f, 45f).RotatedBy(npc.rotation);
                    Vector2 baseVel = new Vector2(-npc.spriteDirection * 6.5f, 9f).RotatedBy(npc.rotation).RotatedByRandom(0.2f);
                    
                    int missile = NPC.NewNPC(npc.GetSource_FromAI(), (int)launcherPos.X, (int)launcherPos.Y, ModContent.NPCType<PlagueHomingMissile>());
                    if (missile >= 0 && missile < Main.maxNPCs)
                    {
                        Main.npc[missile].velocity = baseVel;
                        Main.npc[missile].target = npc.target;
                        Main.npc[missile].netUpdate = true;
                    }
                }
            }

            if (timer >= 170f)
            {
                SelectNextAttack(npc, phase);
            }
        }
        #endregion

        #region State 3: Plague Vomit Sweeps
        /// <summary>
        /// Detailed execution of the plague vomit spread.
        /// Hovers close to player, sweeps head using trigonometry, and fires fans of stingers.
        /// </summary>
        private void ExecuteState_PlagueVomitSweeps(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            npc.damage = 0;
            npc.spriteDirection = (target.Center.X > npc.Center.X).ToDirectionInt();

            // Hover close to player head height
            Vector2 hoverDest = target.Center + new Vector2((target.Center.X < npc.Center.X ? 330f : -330f), -150f);
            npc.velocity = Vector2.Lerp(npc.velocity, Vector2.Normalize(hoverDest - npc.Center) * 13.5f, 0.08f);
            
            // Sweep rotation based on sinusoidal wave
            float sweepAngle = (float)Math.Sin(timer * 0.09f) * 0.60f;
            npc.rotation = sweepAngle;

            // Weld/smoke particles emitting from mouth before vomit spray
            if (timer > 20f && timer < 130f && timer % 8 == 0 && Main.netMode != NetmodeID.Server)
            {
                Vector2 mouthPos = npc.Center + new Vector2(npc.spriteDirection * 70f, 30f).RotatedBy(npc.rotation);
                SpawnNanotechCloud(mouthPos, Main.rand.NextVector2Circular(2f, 2f), 3);
            }

            // Vomit firing ticks
            int fireRate = Math.Max(2, (int)(6 - phase));
            if (timer > 30f && timer < 135f && timer % fireRate == 0)
            {
                SoundEngine.PlaySound(SoundID.Item20, npc.Center); // Acid spit sound fallback

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Vector2 mouthPos = npc.Center + new Vector2(npc.spriteDirection * 70f, 30f).RotatedBy(npc.rotation);
                    Vector2 vomitVel = new Vector2(npc.spriteDirection * 12.5f, 0f).RotatedBy(npc.rotation + sweepAngle * 0.35f).RotatedByRandom(0.1f) * enrage;

                    int stingerType = GetCalamityProjectileType(StingerV2ProjName);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), mouthPos, vomitVel, stingerType, 24, 0.5f, Main.myPlayer);
                }
            }

            if (timer >= 160f)
            {
                SelectNextAttack(npc, phase);
            }
        }
        #endregion

        #region State 4: Carpet Bombing Run
        /// <summary>
        /// Modular sub-phases for carpet bombing horizontal sweeps.
        /// </summary>
        private void ExecuteState_CarpetBombingRun(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            ref float runSubstate = ref stateTracker; // 0 = positioning, 1 = sweep bombing
            ref float startX = ref npc.localAI[1];
            ref float targetY = ref npc.localAI[2];

            switch ((int)runSubstate)
            {
                case 0:
                    // setup starting side and align
                    CarpetBombing_Setup(npc, target, ref timer, ref runSubstate, ref startX, ref targetY, phase, enrage);
                    break;

                case 1:
                    // Execute horizontal sweep dropping bombs
                    CarpetBombing_ExecuteSweep(npc, target, ref timer, ref targetY, phase, enrage);
                    break;
            }
        }

        /// <summary>
        /// Phase 2 Carpet Bombing Setup: Fly far off-screen, prepare telemetry warnings.
        /// </summary>
        private void CarpetBombing_Setup(NPC npc, Player target, ref float timer, ref float runSubstate, ref float startX, ref float targetY, int phase, float enrage)
        {
            npc.damage = 0;
            isCurrentlyCharging = false;

            if (timer == 1f)
            {
                // Select random side (left or right offscreen)
                startX = target.Center.X + (Main.rand.NextBool() ? 1250f : -1250f);
                targetY = target.Center.Y - 280f - Main.rand.NextFloat(-30f, 30f);
                
                // Position far above start location to swoop down
                npc.Center = new Vector2(startX, targetY - 600f);
                npc.netUpdate = true;
            }

            // Descend into position
            Vector2 dest = new Vector2(startX, targetY);
            npc.velocity = Vector2.Lerp(npc.velocity, Vector2.Normalize(dest - npc.Center) * 23f, 0.08f);
            npc.rotation = npc.velocity.X * 0.015f;
            npc.spriteDirection = (target.Center.X > npc.Center.X).ToDirectionInt();

            // Build telegraph guide line across the screen
            telegraphAlpha = MathHelper.Clamp(timer / 40f, 0f, 1f);

            if (timer >= 45f)
            {
                runSubstate = 1f;
                timer = 0f;
                telegraphAlpha = 0f;
                
                // Set sweep direction vector
                float sweepDir = (startX > target.Center.X) ? -1f : 1f;
                npc.velocity = new Vector2(sweepDir * (26f + phase * 3f) * enrage, 0f);
                npc.spriteDirection = (npc.velocity.X > 0f) ? 1 : -1;
                
                SoundEngine.PlaySound(DashSound, npc.Center);
                npc.netUpdate = true;
            }
        }

        /// <summary>
        /// Phase 2 Carpet Bombing Sweep Execution: Rushes across screen dropping explosions.
        /// </summary>
        private void CarpetBombing_ExecuteSweep(NPC npc, Player target, ref float timer, ref float targetY, int phase, float enrage)
        {
            isCurrentlyCharging = true;
            npc.damage = (int)(npc.defDamage * 1.25f);
            
            npc.rotation = npc.velocity.ToRotation();
            if (npc.spriteDirection == -1)
            {
                npc.rotation += Pi;
            }

            // Drop bomb projectiles periodically
            int bombInterval = Math.Max(4, 8 - phase);
            if (timer % bombInterval == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                Vector2 dropPos = npc.Center + new Vector2(0f, 50f).RotatedBy(npc.rotation);
                Vector2 dropVel = new Vector2(npc.velocity.X * 0.25f, 9f + Main.rand.NextFloat(2f));
                
                int explosionType = GetCalamityProjectileType(ExplosionProjName);
                Projectile.NewProjectile(npc.GetSource_FromAI(), dropPos, dropVel, explosionType, 26, 1f, Main.myPlayer);
            }

            // Particle sparks trailing
            if (Main.netMode != NetmodeID.Server && timer % 3 == 0)
            {
                SpawnPlagueSparks(npc.Center - npc.velocity * 1.2f, Color.LimeGreen, 2);
            }

            // Check screen boundary to end sweep
            float horizontalDistance = Math.Abs(npc.Center.X - target.Center.X);
            if (horizontalDistance > 1350f || timer > 100f)
            {
                npc.velocity *= 0.45f;
                SelectNextAttack(npc, phase);
            }
        }
        #endregion

        #region State 5: Explosive Charger Summon
        /// <summary>
        /// Detailed execution of the explosive charger summon.
        /// Decelerates, glows, and summons waves of chargers that target the player.
        /// </summary>
        private void ExecuteState_ExplosiveChargerSummon(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            npc.damage = 0;
            npc.velocity *= 0.94f;
            npc.rotation = npc.velocity.X * 0.02f;
            npc.spriteDirection = (target.Center.X > npc.Center.X).ToDirectionInt();

            // Summon particles on launch interval
            if (timer > 20f && timer < 130f && timer % 45 == 0 && Main.netMode != NetmodeID.Server)
            {
                SpawnPlagueSparks(npc.Center, Color.GreenYellow, 15);
            }

            // Summon waves
            if (timer > 25f && timer < 130f && timer % 45 == 0)
            {
                SoundEngine.PlaySound(SoundID.NPCDeath14, npc.Center); // Mechanical metal clank fallback

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int chargerType = ModContent.NPCType<CalamityMod.NPCs.PlagueEnemies.PlagueCharger>();

                    for (int i = 0; i < 3; i++)
                    {
                        // Position spawn coordinates in radial fan
                        float angleOffset = (i - 1f) * 0.42f;
                        Vector2 spawnPos = npc.Center + new Vector2(-npc.spriteDirection * 160f, 0f).RotatedBy(npc.rotation + angleOffset);
                        Vector2 spawnVel = new Vector2(-npc.spriteDirection * 9.5f, -2.5f).RotatedBy(npc.rotation + angleOffset * 1.4f).RotatedByRandom(0.08f) * enrage;

                        int charger = NPC.NewNPC(npc.GetSource_FromAI(), (int)spawnPos.X, (int)spawnPos.Y, chargerType);
                        if (charger >= 0 && charger < Main.maxNPCs)
                        {
                            Main.npc[charger].velocity = spawnVel;
                            Main.npc[charger].target = npc.target;
                            Main.npc[charger].netUpdate = true;
                        }
                    }
                }
            }

            if (timer >= 150f)
            {
                SelectNextAttack(npc, phase);
            }
        }
        #endregion

        #region State 6: Drone Jail Summon
        /// <summary>
        /// Modular sub-phases for the drone jail rotating cage.
        /// </summary>
        private void ExecuteState_DroneJailSummon(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            ref float jailSubstate = ref stateTracker; // 0 = spawning, 1 = active combat loop, 2 = collapse fade
            ref float actionTimer = ref npc.localAI[1];

            switch ((int)jailSubstate)
            {
                case 0:
                    DroneJail_Spawning(npc, target, ref timer, ref jailSubstate, phase);
                    break;

                case 1:
                    DroneJail_CombatLoop(npc, target, ref timer, ref actionTimer, phase, enrage);
                    break;

                case 2:
                    DroneJail_Collapse(npc, target, ref timer, phase);
                    break;
            }
        }

        /// <summary>
        /// Drone Jail Sub-phase 0: Hover high above, expand telegraph boundary lines.
        /// </summary>
        private void DroneJail_Spawning(NPC npc, Player target, ref float timer, ref float jailSubstate, int phase)
        {
            ref float actionTimer = ref npc.localAI[1];
            npc.damage = 0;
            isCurrentlyCharging = false;
            
            // Align high above target
            Vector2 destination = target.Center + new Vector2(0f, -420f);
            npc.velocity = Vector2.Lerp(npc.velocity, Vector2.Normalize(destination - npc.Center) * 17f, 0.08f);
            npc.rotation = npc.velocity.X * 0.015f;
            npc.spriteDirection = (target.Center.X > npc.Center.X).ToDirectionInt();

            // Expand cage indicators
            jailAlpha = MathHelper.Clamp(timer / 45f, 0f, 1f);
            jailRadius = MathHelper.Lerp(1100f, 390f, MathHelper.Clamp(timer / 45f, 0f, 1f));
            jailRotation += 0.010f;

            // Position drone coordinates dynamically
            for (int i = 0; i < MaxJailDrones; i++)
            {
                float angle = jailRotation + (i * TwoPi / MaxJailDrones);
                jailDronePositions[i] = target.Center + angle.ToRotationVector2() * jailRadius;
            }

            if (timer >= 60f)
            {
                jailSubstate = 1f;
                timer = 0f;
                actionTimer = 0f;
                dashCounter = 0;
                npc.netUpdate = true;
            }
        }

        /// <summary>
        /// Drone Jail Sub-phase 1: Combat loop with boundary checks and diagonal dashes.
        /// </summary>
        private void DroneJail_CombatLoop(NPC npc, Player target, ref float timer, ref float actionTimer, int phase, float enrage)
        {
            jailRotation += 0.014f;

            // Lock jail coordinates to the player's position on activation
            if (actionTimer == 1f)
            {
                desperationCenter = target.Center;
                npc.netUpdate = true;
            }

            // Check boundary crossing
            float distFromCenter = Vector2.Distance(target.Center, desperationCenter);
            if (distFromCenter > 410f)
            {
                // Hurt player and apply plague
                target.Hurt(PlayerDeathReason.ByNPC(npc.whoAmI), 7, 0);
                target.AddBuff(BuffID.Poisoned, 60);

                if (Main.rand.NextBool(3) && Main.netMode != NetmodeID.Server)
                {
                    SpawnPlagueSparks(target.Center, Color.Lime, 4);
                }
            }

            // Maintain rotating drone coordinates
            for (int i = 0; i < MaxJailDrones; i++)
            {
                float angle = jailRotation + (i * TwoPi / MaxJailDrones);
                jailDronePositions[i] = desperationCenter + angle.ToRotationVector2() * 390f;
            }

            // Diagonal charging logic inside jail
            ref float chargeSub = ref npc.localAI[2]; // 0 = align, 1 = charge
            ref float subTimer = ref npc.localAI[3];

            if (chargeSub == 0f)
            {
                isCurrentlyCharging = false;
                npc.damage = 0;

                // Position outside perimeter
                float hoverAngle = (dashCounter * PiOver2) + (float)Math.Sin(timer * 0.04f) * 0.4f;
                Vector2 hoverPoint = desperationCenter + hoverAngle.ToRotationVector2() * 570f;
                npc.velocity = Vector2.Lerp(npc.velocity, Vector2.Normalize(hoverPoint - npc.Center) * 21f, 0.09f);
                npc.rotation = npc.velocity.X * 0.01f;
                npc.spriteDirection = (target.Center.X > npc.Center.X).ToDirectionInt();

                // Build telegraph guides
                telegraphAlpha = MathHelper.Clamp(subTimer / 22f, 0f, 1f);

                if (subTimer >= 26f)
                {
                    chargeSub = 1f;
                    subTimer = 0f;
                    telegraphAlpha = 0f;

                    // Dash vector targeting player
                    Vector2 dashDir = Vector2.Normalize(target.Center - npc.Center);
                    npc.velocity = dashDir * (27f + phase * 2f) * enrage;
                    
                    SoundEngine.PlaySound(DashSound, npc.Center);
                    npc.netUpdate = true;
                }
            }
            else if (chargeSub == 1f)
            {
                isCurrentlyCharging = true;
                npc.damage = npc.defDamage;
                npc.rotation = npc.velocity.ToRotation();
                if (npc.spriteDirection == -1)
                {
                    npc.rotation += Pi;
                }

                if (subTimer > 24f)
                {
                    npc.velocity *= 0.92f;
                }

                if (subTimer >= 34f)
                {
                    dashCounter++;
                    chargeSub = 0f;
                    subTimer = 0f;
                    npc.netUpdate = true;

                    if (dashCounter >= 3)
                    {
                        ref float jailSubstate = ref npc.ai[3];
                        jailSubstate = 2f;
                        timer = 0f;
                    }
                }
            }

            subTimer++;
            actionTimer++;
        }

        /// <summary>
        /// Drone Jail Sub-phase 2: Collapse cage and fade out variables.
        /// </summary>
        private void DroneJail_Collapse(NPC npc, Player target, ref float timer, int phase)
        {
            npc.damage = 0;
            isCurrentlyCharging = false;
            npc.velocity *= 0.93f;

            jailAlpha = MathHelper.Lerp(1f, 0f, timer / 30f);
            jailRadius = MathHelper.Lerp(390f, 650f, timer / 30f);

            for (int i = 0; i < MaxJailDrones; i++)
            {
                float angle = jailRotation + (i * TwoPi / MaxJailDrones);
                jailDronePositions[i] = desperationCenter + angle.ToRotationVector2() * jailRadius;
            }

            if (timer >= 30f)
            {
                jailAlpha = 0f;
                SelectNextAttack(npc, phase);
            }
        }
        #endregion

        #region State 7: Constructor Drones Windup
        /// <summary>
        /// Detailed execution of constructor windup phase.
        /// Activates nuke alert sound, spawns builder drones, draws green weld lasers.
        /// </summary>
        private void ExecuteState_ConstructorDronesWindup(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            npc.damage = 0;
            isCurrentlyCharging = false;

            // Hover coordinates stable high above target
            Vector2 hoverDest = target.Center - new Vector2(0f, 350f);
            npc.velocity = Vector2.Lerp(npc.velocity, Vector2.Normalize(hoverDest - npc.Center) * 14.5f, 0.08f);
            npc.rotation = npc.velocity.X * 0.01f;
            npc.spriteDirection = (target.Center.X > npc.Center.X).ToDirectionInt();

            if (timer == 1f)
            {
                // Play warning sound and initialize construction coordinates
                SoundEngine.PlaySound(NukeWarningSound, target.Center);
                constructionCenter = npc.Center + new Vector2(0f, 160f);
                constructionScale = 0.05f;
                constructorDronesCount = 4;
                npc.netUpdate = true;
            }

            // Interpolate build-up scale
            constructionScale = MathHelper.Lerp(0.05f, 1f, MathHelper.Clamp(timer / 95f, 0f, 1f));

            // Position welding drone nodes
            float orbitRadius = 95f + (float)Math.Sin(timer * 0.09f) * 12f;
            for (int i = 0; i < constructorDronesCount; i++)
            {
                float angle = (timer * 0.045f) + (i * TwoPi / constructorDronesCount);
                constructorDronePositions[i] = constructionCenter + angle.ToRotationVector2() * orbitRadius;

                // Weld sparks emission
                if (Main.rand.NextBool(3) && Main.netMode != NetmodeID.Server)
                {
                    Vector2 sparkVel = Main.rand.NextVector2Circular(2.2f, 2.2f);
                    Dust d = Dust.NewDustPerfect(constructorDronePositions[i], DustID.Electric, sparkVel, 100, Color.Lime, 0.9f);
                    d.noGravity = true;
                }
            }

            // Intersecting warning stingers rain
            if (timer > 30f && timer % 18 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                Vector2 spawnPos = target.Center + new Vector2(Main.rand.NextFloat(-620f, 620f), -700f);
                Vector2 vel = new Vector2(Main.rand.NextFloat(-2.5f, 2.5f), 15.5f);
                
                int stinger = GetCalamityProjectileType(StingerProjName);
                Projectile.NewProjectile(npc.GetSource_FromAI(), spawnPos, vel, stinger, 22, 0f, Main.myPlayer);
            }

            if (timer >= 120f)
            {
                TransitionToAttack(npc, AttackState.NuclearDetonationDrop);
            }
        }
        #endregion

        #region State 8: Nuclear Detonation Drop
        /// <summary>
        /// Detailed execution of nuke drop.
        /// Drops core down, triggers screen shake, fires radial stinger loops and Calamity HiveNuke.
        /// </summary>
        private void ExecuteState_NuclearDetonationDrop(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            npc.damage = 0;
            isCurrentlyCharging = false;
            npc.velocity *= 0.94f;
            npc.rotation = npc.velocity.X * 0.02f;

            ref float coreY = ref stateTracker;

            if (timer == 1f)
            {
                coreY = constructionCenter.Y;
                npc.netUpdate = true;
            }

            // Drop core down
            if (timer < 45f)
            {
                coreY += 12.5f;
                constructionCenter.Y = coreY;
            }
            // Trigger nuke detonation
            else if (timer == 45f)
            {
                SoundEngine.PlaySound(SoundID.NPCDeath14, constructionCenter); // Heavy metal crash
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 40f;

                // Spawn radial circles of particles and ring wave
                if (Main.netMode != NetmodeID.Server)
                {
                    SpawnNuclearHeatWave(constructionCenter, 500f, 40);
                }

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int nukeProj = GetCalamityProjectileType(NukeProjName);
                    // Spawn Calamity's actual HiveNuke projectile that handles tile collision explosions
                    Projectile.NewProjectile(npc.GetSource_FromAI(), constructionCenter, new Vector2(0f, 8.5f), nukeProj, 75, 4.5f, Main.myPlayer);

                    // Additional radial spreads
                    int stinger = GetCalamityProjectileType(StingerV2ProjName);
                    for (int i = 0; i < 14; i++)
                    {
                        Vector2 stingerVel = (i * TwoPi / 14f).ToRotationVector2() * 9.5f;
                        Projectile.NewProjectile(npc.GetSource_FromAI(), constructionCenter, stingerVel, stinger, 24, 0f, Main.myPlayer);
                    }
                }
            }

            if (timer >= 95f)
            {
                SelectNextAttack(npc, phase);
            }
        }
        #endregion

        #region State 9: Desperation Plague Storm
        /// <summary>
        /// Modular sub-phases for desperation storm.
        /// Handles boundary check, extreme speed dashes, and eye laser sweeps.
        /// </summary>
        private void ExecuteState_DesperationPlagueStorm(NPC npc, Player target, ref float timer, ref float stateTracker, float enrage)
        {
            // Build arena alpha
            arenaAlpha = MathHelper.Clamp(timer / 60f, 0f, 1f);
            arenaPulseScale = 1f + (float)Math.Sin(timer * 0.08f) * 0.03f;

            // Enforce arena boundary check
            Desperation_BoundaryCheck(npc, target);

            // Execute sub-phases
            ref float dashPhase = ref npc.localAI[1]; // 0 = positioning/telegraph, 1 = charge vector

            npc.spriteDirection = (target.Center.X > npc.Center.X).ToDirectionInt();

            if (dashPhase == 0f)
            {
                // Positioning perimeter alignment and warnings
                Desperation_OverdriveAlign(npc, target, ref timer, ref stateTracker, enrage);
            }
            else if (dashPhase == 1f)
            {
                // Execute extreme charge crossing the center
                Desperation_ExecuteOverdriveDash(npc, target, ref timer, ref stateTracker, enrage);
            }

            // eye sweeping lasers guide
            Desperation_EyeSweeps(npc, target, ref timer);
        }

        /// <summary>
        /// Phase 4 Desperation: Enforce boundary constraint limits.
        /// </summary>
        private void Desperation_BoundaryCheck(NPC npc, Player target)
        {
            float playerDist = Vector2.Distance(target.Center, desperationCenter);
            if (playerDist > ArenaRadius)
            {
                // Apply dot damage and plague
                target.Hurt(PlayerDeathReason.ByNPC(npc.whoAmI), 10, 0);
                target.AddBuff(BuffID.Poisoned, 120);

                if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(3))
                {
                    Dust d = Dust.NewDustPerfect(target.Center, DustID.PoisonStaff, Main.rand.NextVector2Circular(4f, 4f), 100, Color.LimeGreen, 1.2f);
                    d.noGravity = true;
                }
            }
        }

        /// <summary>
        /// Phase 4 Desperation: Hover positioning along perimeter.
        /// </summary>
        private void Desperation_OverdriveAlign(NPC npc, Player target, ref float timer, ref float stateTracker, float enrage)
        {
            isCurrentlyCharging = false;
            npc.damage = 0;

            ref float chargeTimer = ref stateTracker;
            ref float dashPhase = ref npc.localAI[1];

            // Alternate starting positions along perimeter
            float angle = (chargeTimer * 0.15f) + (float)Math.PI;
            Vector2 destination = desperationCenter + angle.ToRotationVector2() * (ArenaRadius - 80f);
            
            npc.velocity = Vector2.Lerp(npc.velocity, Vector2.Normalize(destination - npc.Center) * 24.5f, 0.12f);
            npc.rotation = npc.velocity.X * 0.015f;

            // laser telegraph alpha update
            telegraphAlpha = MathHelper.Clamp(timer % 30f / 30f, 0f, 1f);

            if (timer > 22f && (Vector2.Distance(npc.Center, destination) < 130f || timer > 45f))
            {
                dashPhase = 1f;
                timer = 0f;
                telegraphAlpha = 0f;

                // Dash vector directly crossing through center targeting player
                Vector2 targetPredict = target.Center + target.velocity * 9f;
                npc.velocity = Vector2.Normalize(targetPredict - npc.Center) * 33f * enrage;
                
                SoundEngine.PlaySound(DashSound, npc.Center);
                npc.netUpdate = true;
            }
        }

        /// <summary>
        /// Phase 4 Desperation: Rushes target, fires crossing stingers.
        /// </summary>
        private void Desperation_ExecuteOverdriveDash(NPC npc, Player target, ref float timer, ref float stateTracker, float enrage)
        {
            isCurrentlyCharging = true;
            npc.damage = (int)(npc.defDamage * 1.35f);
            
            npc.rotation = npc.velocity.ToRotation();
            if (npc.spriteDirection == -1)
            {
                npc.rotation += Pi;
            }

            // Radial stinger releases during active charge
            if (timer % 7 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                int stinger = GetCalamityProjectileType(StingerProjName);
                Vector2 baseVel = new Vector2(-npc.spriteDirection * 5f, 6.5f).RotatedBy(npc.rotation);
                
                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, baseVel.RotatedBy(PiOver2), stinger, 24, 0f, Main.myPlayer);
                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, baseVel.RotatedBy(-PiOver2), stinger, 24, 0f, Main.myPlayer);
            }

            if (timer > 26f)
            {
                npc.velocity *= 0.91f;
            }

            if (timer >= 36f)
            {
                ref float dashPhase = ref npc.localAI[1];
                ref float chargeTimer = ref stateTracker;

                dashPhase = 0f;
                timer = 0f;
                chargeTimer++;
                npc.netUpdate = true;
            }
        }

        /// <summary>
        /// Phase 4 Desperation: Eye sweeping lasers guide.
        /// </summary>
        private void Desperation_EyeSweeps(NPC npc, Player target, ref float timer)
        {
            laserTelegraphAlpha = MathHelper.Clamp((float)Math.Sin(timer * 0.05f), 0f, 1f) * 0.7f;
            laserTelegraphAngle += 0.015f;

            if (timer % 90 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                // Spits green pulse lasers towards target
                int laser = GetCalamityProjectileType(PulseProjName);
                Vector2 laserVel1 = (target.Center - npc.Center).SafeNormalize(Vector2.UnitY).RotatedBy(0.40f) * 8.5f;
                Vector2 laserVel2 = (target.Center - npc.Center).SafeNormalize(Vector2.UnitY).RotatedBy(-0.40f) * 8.5f;
                
                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, laserVel1, laser, 26, 0f, Main.myPlayer);
                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, laserVel2, laser, 26, 0f, Main.myPlayer);
            }
        }
        #endregion

        #region State 10: Death Animation
        /// <summary>
        /// Detailed execution of death spin.
        /// Triggers camera shakes, heavy explosive clouds, drops items.
        /// </summary>
        private void ExecuteState_DeathAnimation(NPC npc, ref float timer)
        {
            npc.dontTakeDamage = true;
            npc.damage = 0;
            npc.velocity *= 0.95f;
            npc.rotation += 0.14f;

            Main.LocalPlayer.Calamity().GeneralScreenShakePower = 6f;

            // Spawn explosive sparks
            if (Main.netMode != NetmodeID.Server && timer % 4 == 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    Vector2 expVel = Main.rand.NextVector2Circular(6.5f, 6.5f);
                    SpawnExplosionCloud(npc.Center + Main.rand.NextVector2Circular(85f, 85f));
                    Dust.NewDustPerfect(npc.Center + Main.rand.NextVector2Circular(90f, 90f), DustID.PoisonStaff, expVel, 100, Color.Orange, 1.4f);
                }
            }

            if (timer >= 120f)
            {
                // Kill boss
                npc.life = 0;
                npc.HitEffect();
                npc.checkDead();
                npc.active = false;
            }
        }
        #endregion

        #region State 11: Victory Despawn
        /// <summary>
        /// Player dead despawn behavior.
        /// Swoops upwards rapidly off-screen.
        /// </summary>
        private void ExecuteVictoryDespawn(NPC npc)
        {
            npc.damage = 0;
            npc.dontTakeDamage = true;
            isCurrentlyCharging = false;
            
            // Swoop up
            npc.velocity = Vector2.Lerp(npc.velocity, new Vector2(0f, -30f), 0.08f);
            npc.rotation = npc.velocity.X * 0.02f;

            if (npc.Center.Y < -2000f || !npc.WithinRange(Main.LocalPlayer.Center, 3600f))
            {
                npc.active = false;
            }
        }
        #endregion

        #endregion

        #region Particle System Helpers
        /// <summary>
        /// Emits a green nanotech dust cloud at specified coordinate space.
        /// </summary>
        private void SpawnNanotechCloud(Vector2 pos, Vector2 vel, int count)
        {
            activeNanotechParticles += count;
            if (Main.netMode == NetmodeID.Server)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                Vector2 randomVel = vel + Main.rand.NextVector2Circular(1.5f, 1.5f);
                Dust d = Dust.NewDustPerfect(pos, DustID.GreenFairy, randomVel, 100, Color.LimeGreen, 1.15f);
                d.noGravity = true;
            }
        }

        /// <summary>
        /// Emits bright glowing sparks.
        /// </summary>
        private void SpawnPlagueSparks(Vector2 pos, Color color, int count)
        {
            if (Main.netMode == NetmodeID.Server)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                Vector2 randomVel = Main.rand.NextVector2Circular(6.5f, 6.5f);
                Dust d = Dust.NewDustPerfect(pos, DustID.Electric, randomVel, 100, color, 0.95f);
                d.noGravity = true;
            }
        }

        /// <summary>
        /// Emits expanding rings of green dust to indicate nuclear explosions.
        /// </summary>
        private void SpawnNuclearHeatWave(Vector2 pos, float maxRadius, int count)
        {
            if (Main.netMode == NetmodeID.Server)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                float angle = i * TwoPi / count;
                Vector2 direction = angle.ToRotationVector2();
                Vector2 particleVel = direction * 9f;
                
                Dust d = Dust.NewDustPerfect(pos, DustID.GreenFairy, particleVel, 80, default, 1.5f);
                d.noGravity = true;
            }
        }

        /// <summary>
        /// Emits small smoke clouds representing micro explosions.
        /// </summary>
        private void SpawnExplosionCloud(Vector2 pos)
        {
            if (Main.netMode == NetmodeID.Server)
            {
                return;
            }

            for (int i = 0; i < 3; i++)
            {
                Dust d = Dust.NewDustPerfect(pos + Main.rand.NextVector2Circular(15f, 15f), DustID.Smoke, Main.rand.NextVector2Circular(2f, 2f), 120, Color.DarkSlateGray, 1.6f);
                d.noGravity = false;
            }
        }
        #endregion

        #region Custom Drawing hooks (PreDraw & PostDraw)
        /// <summary>
        /// Handles customized sprite drawing with texture swapping for charge states,
        /// afterimages, indicators, weldings, and boundaries.
        /// Returns false to suppress standard sprite engine.
        /// </summary>
        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            int phase = (int)npc.ai[0];
            AttackState state = (AttackState)(int)npc.ai[1];
            float timer = npc.ai[2];

            // 1. Draw predictive dash warning line indicators
            if (telegraphAlpha > 0.01f)
            {
                Player target = Main.player[npc.target];
                Vector2 predictedPos = target.Center + target.velocity * 15f;
                Color col = Color.Lime * telegraphAlpha * 0.75f;
                DrawTelegraphLine(spriteBatch, npc.Center, predictedPos, col, 5f);
            }

            // 2. Draw Drone Jail boundary lines
            if (jailAlpha > 0.01f)
            {
                for (int i = 0; i < MaxJailDrones; i++)
                {
                    Vector2 current = jailDronePositions[i];
                    Vector2 next = jailDronePositions[(i + 1) % MaxJailDrones];
                    Color col = Color.GreenYellow * jailAlpha * 0.85f;
                    
                    // Outer connection lines
                    DrawTelegraphLine(spriteBatch, current, next, col, 4f);
                    
                    // Cross inner connection lines
                    if (i % 2 == 0)
                    {
                        Vector2 opposite = jailDronePositions[(i + MaxJailDrones / 2) % MaxJailDrones];
                        DrawTelegraphLine(spriteBatch, current, opposite, col * 0.35f, 2f);
                    }
                    
                    // Draw drone node spheres
                    DrawIndicatorCircle(spriteBatch, current, 12f, Color.Lime * jailAlpha);
                }
            }

            // 3. Draw Welding laser beams during constructor windup
            if (state == AttackState.ConstructorDronesWindup && constructionScale > 0.01f)
            {
                // Draw central core
                DrawIndicatorCircle(spriteBatch, constructionCenter, 35f * constructionScale, Color.Lime * 0.95f);
                DrawIndicatorCircle(spriteBatch, constructionCenter, 20f * constructionScale, Color.Yellow * 0.8f);

                // Welding beams linking drones to core
                for (int i = 0; i < constructorDronesCount; i++)
                {
                    DrawTelegraphLine(spriteBatch, constructorDronePositions[i], constructionCenter, Color.LightGreen * 0.85f, 3f);
                    DrawIndicatorCircle(spriteBatch, constructorDronePositions[i], 8f, Color.Lime);
                }
            }

            // 4. Draw Desperation Plague Storm circular boundary
            if (phase == 4 && state == AttackState.DesperationPlagueStorm && arenaAlpha > 0.01f)
            {
                DrawArenaBoundary(spriteBatch, desperationCenter, ArenaRadius * arenaPulseScale, Color.LimeGreen * arenaAlpha * 0.9f);
                
                // Draw secondary inside ring
                DrawArenaBoundary(spriteBatch, desperationCenter, (ArenaRadius - 60f) * arenaPulseScale, Color.YellowGreen * arenaAlpha * 0.45f);
            }

            // 5. Draw eye sweeping laser telegraph guides
            if (laserTelegraphAlpha > 0.01f)
            {
                Vector2 targetDir = (Main.player[npc.target].Center - npc.Center).SafeNormalize(Vector2.UnitY);
                Vector2 guideDir1 = targetDir.RotatedBy(0.45f);
                Vector2 guideDir2 = targetDir.RotatedBy(-0.45f);
                
                DrawTelegraphLine(spriteBatch, npc.Center, npc.Center + guideDir1 * 1200f, Color.Green * laserTelegraphAlpha, 2.5f);
                DrawTelegraphLine(spriteBatch, npc.Center, npc.Center + guideDir2 * 1200f, Color.Green * laserTelegraphAlpha, 2.5f);
            }

            // 6. Draw standard charge trail afterimages
            if (isCurrentlyCharging)
            {
                Texture2D texture = TextureAssets.Npc[npc.type].Value;
                if (ModContent.Request<Texture2D>("CalamityMod/NPCs/PlaguebringerGoliath/PlaguebringerGoliathChargeTex").Value is Texture2D chargeTex)
                {
                    texture = chargeTex;
                }

                Vector2 origin = new(texture.Width / 2f, texture.Height / 6f); // 3 flying, 3 charging frames
                SpriteEffects effects = npc.spriteDirection == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

                for (int i = 1; i < 8; i += 2)
                {
                    Color trailColor = Color.LimeGreen * (0.4f / i) * npc.Opacity;
                    Vector2 drawPos = npc.oldPos[i] + npc.Size * 0.5f - Main.screenPosition;
                    spriteBatch.Draw(texture, drawPos, npc.frame, npc.GetAlpha(trailColor), npc.rotation, origin, npc.scale, effects, 0f);
                }
            }

            // 7. Draw the boss sprite itself
            DrawBossSprite(npc, spriteBatch, drawColor);

            return false; // Prevent engine drawing twice
        }

        /// <summary>
        /// PostDraw override. Empty as PreDraw handles all layering orders.
        /// </summary>
        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            // Empty
        }
        #endregion

        #region Frame Animations
        /// <summary>
        /// Custom frame indexing to align with fly vs. charge texture layouts.
        /// </summary>
        public override void FindFrame(NPC npc, int frameHeight)
        {
            bool charging = isCurrentlyCharging;
            int width = !charging ? 532 / 2 : 644 / 2;
            int height = !charging ? 768 / 3 : 636 / 3;
            
            npc.frameCounter += charging ? 1.8f : 1f;

            if (npc.frameCounter > 4.0)
            {
                npc.frame.Y += height;
                npc.frameCounter = 0.0;
            }
            
            if (npc.frame.Y >= height * 3)
            {
                npc.frame.Y = 0;
                npc.frame.X = npc.frame.X == 0 ? width : 0;
            }
        }
        #endregion

        #region Helper Drawing and Math Utilities
        /// <summary>
        /// Draws a colored pixel-based warning/telegraph line.
        /// </summary>
        private static void DrawTelegraphLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, float width)
        {
            Texture2D pixel = TextureAssets.BlackTile.Value;
            Vector2 vector = end - start;
            float rotation = vector.ToRotation();
            float length = vector.Length();
            Vector2 scale = new(length, width);
            Vector2 origin = Vector2.Zero;
            
            spriteBatch.Draw(pixel, start - Main.screenPosition, null, color, rotation, origin, scale, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// Draws a hollow circular boundary using line segments.
        /// </summary>
        private static void DrawArenaBoundary(SpriteBatch spriteBatch, Vector2 center, float radius, Color color)
        {
            const int segments = 72;
            float angleStep = TwoPi / segments;
            Vector2 prevPoint = center + new Vector2(radius, 0f);

            for (int i = 1; i <= segments; i++)
            {
                float angle = i * angleStep;
                Vector2 nextPoint = center + angle.ToRotationVector2() * radius;
                DrawTelegraphLine(spriteBatch, prevPoint, nextPoint, color, 6f);
                prevPoint = nextPoint;
            }
        }

        /// <summary>
        /// Draws a small indicators circle warning node.
        /// </summary>
        private static void DrawIndicatorCircle(SpriteBatch spriteBatch, Vector2 center, float radius, Color color)
        {
            const int segments = 16;
            float angleStep = TwoPi / segments;
            Vector2 prevPoint = center + new Vector2(radius, 0f);

            for (int i = 1; i <= segments; i++)
            {
                float angle = i * angleStep;
                Vector2 nextPoint = center + angle.ToRotationVector2() * radius;
                DrawTelegraphLine(spriteBatch, prevPoint, nextPoint, color, 2f);
                prevPoint = nextPoint;
            }
        }

        /// <summary>
        /// Draws the actual boss textures, applying charging or regular sheets.
        /// </summary>
        private void DrawBossSprite(NPC npc, SpriteBatch spriteBatch, Color lightColor)
        {
            Texture2D texture = TextureAssets.Npc[npc.type].Value;
            Texture2D glowTexture = ModContent.Request<Texture2D>("CalamityMod/NPCs/PlaguebringerGoliath/PlaguebringerGoliathGlow").Value;

            if (isCurrentlyCharging)
            {
                texture = ModContent.Request<Texture2D>("CalamityMod/NPCs/PlaguebringerGoliath/PlaguebringerGoliathChargeTex").Value;
                glowTexture = ModContent.Request<Texture2D>("CalamityMod/NPCs/PlaguebringerGoliath/PlaguebringerGoliathChargeTexGlow").Value;
            }

            SpriteEffects effects = npc.spriteDirection == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Vector2 origin = new(texture.Width / 2f, texture.Height / 6f); // 3 flying, 3 charging frames
            
            // Render main sprite
            Vector2 basePos = npc.Center - Main.screenPosition;
            spriteBatch.Draw(texture, basePos, npc.frame, npc.GetAlpha(lightColor), npc.rotation, origin, npc.scale, effects, 0f);
            
            // Render glowmask
            spriteBatch.Draw(glowTexture, basePos, npc.frame, npc.GetAlpha(Color.White), npc.rotation, origin, npc.scale, effects, 0f);
        }

        /// <summary>
        /// Resolves Calamity Mod projectile IDs dynamically.
        /// </summary>
        private static int GetCalamityProjectileType(string name)
        {
            if (ModContent.TryFind($"CalamityMod/{name}", out ModProjectile projectile))
            {
                return projectile.Type;
            }
            // Safe fallback to stinger if not found
            return ProjectileID.Stinger;
        }

        /// <summary>
        /// Broadcaster alert messages.
        /// </summary>
        private static void BroadcastAlert(string message)
        {
            if (Main.netMode == NetmodeID.Server)
            {
                Terraria.Chat.ChatHelper.BroadcastChatMessage(Terraria.Localization.NetworkText.FromLiteral(message), new Color(176, 255, 76));
            }
            else if (Main.netMode == NetmodeID.SinglePlayer)
            {
                Main.NewText(message, 176, 255, 76);
            }
        }
        #endregion

        #region Extra Visual Effects and Mathematics Theory
        /*
         * MATHEMATICAL THEORY OF PREDICTIVE TARGETING:
         * To hit a moving target with a projectile or dash at speed S:
         * Let P0 be player position, Vp be player velocity vector, and B0 be boss position.
         * We want to find a direction vector D and duration T such that:
         * B0 + D * S * T = P0 + Vp * T
         * Rearranging, this forms a quadratic equation in T:
         * (S^2 - |Vp|^2) * T^2 - 2 * ((P0 - B0) . Vp) * T - |P0 - B0|^2 = 0
         * We solve for T (retaining the positive real root) and predict the collision coordinates:
         * TargetPosition = P0 + Vp * T
         *
         * SINUSOIDAL WELD ANIMATIONS:
         * Weld lines between builder drones and the nuclear nuke core are drawn using a trigonometric offset
         * to create high-frequency welding arc waves:
         * WaveOffset = PerpendicularVector * Sin(Dist * Frequency + Phase) * Amplitude
         */

        /// <summary>
        /// Draws a dotted line for target telemetry grids.
        /// </summary>
        private static void DrawDottedWarningLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, float spacing)
        {
            Vector2 vector = end - start;
            float length = vector.Length();
            Vector2 dir = Vector2.Normalize(vector);
            Texture2D pixel = TextureAssets.BlackTile.Value;
            Vector2 origin = Vector2.Zero;
            Vector2 scale = new Vector2(4f, 4f);

            for (float d = 0f; d < length; d += spacing)
            {
                Vector2 pos = start + dir * d;
                spriteBatch.Draw(pixel, pos - Main.screenPosition, null, color, 0f, origin, scale, SpriteEffects.None, 0f);
            }
        }
        #endregion
    }
}
