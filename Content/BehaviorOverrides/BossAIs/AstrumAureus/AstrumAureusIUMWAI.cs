// =====================================================================================================================
// ASTRUM AUREUS - CUSTOM BEHAVIOR OVERRIDE (IUMW MODE)
// =====================================================================================================================
// DESIGN PHILOSOPHY & Fight METRICS:
// Astrum Aureus is a colossal, biomechanical star-walker infected by the cosmic influence of the Astral Infection.
// This override suppresses the default AI update loop (PreAI returns false), taking absolute authority over movement physics,
// targeting, state transitions, frame animations, and visual drawing indicators.
//
// FIGHT MECHANICS & FLOW:
// - Phase 1 (100% - 72% HP) - Stellar Engine Activation:
//   * Spawn Animation: Emerges in a dormant state, channelling a cosmic storm before roaring and shaking the camera.
//   * Walk and Shoot: Walks back and forth, shooting wide arcing fans of Astral Lasers and Astral Flames.
//   * Cyber Leap: Jumps high, slamming down on the player. Creates heavy stomp shockwaves and radial comets.
//   * Rocket Barrage: Releases barrages of homing plasma rockets with arcing warning paths.
//   * Star Furnace Recharge: Sits down, generating an energy shield that draws in particles and fires sparks.
//
// - Phase 2 (72% - 46% HP) - Overclocked Locomotives:
//   * Overclock Walk: Walking speed increases. If the target is too far, Aureus gains enrage speed multipliers.
//   * Comet Rain: Charges up astral energy (gaining defense and drawing in particles), then rains comets from the sky preceded by vertical warning beams.
//   * Double Stomps: Performs two consecutive leaping stomps, releasing heavy shockwaves and homing comets.
//   * Tachyon Ricochet: Fires bounding tachyon stars that ricochet off screen boundaries to frame the player.
//
// - Phase 3 (46% - 18% HP) - Solar Flare & Arcing Rays:
//   * Dual Drill Laser: Channels two massive arcing lasers (Blue and Orange) that sweep across the screen in opposite directions, forcing the player to jump or hover over them.
//   * Astral Nova Ring: Releases large expanding circular waves of Astral Lasers and Astral Flames.
//
// - Phase 4 (18% - 0% HP) - Astral Nova Collapse (Desperation):
//   * Singularity Arena: Confines the player inside a 660f circular boundary. Crossing it inflicts massive event horizon damage.
//   * Core Pulses: Boss sits invulnerable at the center, releasing expanding rings of plasma comets.
//   * Constellation Laser Grid: Dotted warning lines sweep across the screen, detonating into laser lines.
//   * Orbital Probes: Spawns small orbital probe sentinels that circle around the arena boundary, firing lasers inward.
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
using CalamityMod.NPCs.AstrumAureus;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using CalamityIUMWMode.Core.Systems;
using Terraria.DataStructures;
using Terraria.Localization;

using CalamityAstrumAureus = CalamityMod.NPCs.AstrumAureus.AstrumAureus;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.AstrumAureus
{
    internal sealed class AstrumAureusIUMWAI : IUMWBossAI
    {
        #region Constants & Configuration
        // NPC Identifiers
        public override int NPCType => ModContent.NPCType<CalamityAstrumAureus>();
        public override string BossName => "Astrum Aureus";

        // Phase Thresholds
        public override float[] PhaseLifeRatios => new[] { 0.72f, 0.46f, 0.18f };
        public override int AttackCycleLength => 126;
        public override float MotionIntensity => 0.95f;
        public override Color DebugColor => new(255, 196, 72);

        // Sound Hooks (Sourced statically from Calamity Mod assets)
        public static readonly SoundStyle LeapSound = SoundID.Zombie105;
        public static readonly SoundStyle ChargeSound = SoundID.Item15;
        public static readonly SoundStyle LaserFireSound = SoundID.Item91;
        public static readonly SoundStyle StompSound = SoundID.Item14;
        public static readonly SoundStyle NukeFireSound = SoundID.Item62;

        // Math Constants
        private const float TwoPi = MathHelper.TwoPi;
        private const float Pi = MathHelper.Pi;
        private const float PiOver2 = MathHelper.PiOver2;
        private const float ArenaRadius = 660f;

        // Projectile Reference Keys
        private const string ProjLaser = "AstralLaser";
        private const string ProjFlame = "AstralFlame";
        private const string ProjRocket = "AstralShot2";
        private const string ProjMine = "DeusMine";
        private const string ProjGodRay = "AstralGodRay";
        private const string ProjComet = "AstralBlueComet";
        #endregion

        #region State Machine Enumeration
        public enum AttackState
        {
            SpawnRoar = 0,
            WalkAndShoot = 1,
            CyberLeap = 2,
            RocketBarrage = 3,
            StarFurnaceRecharge = 4,
            OverclockWalk = 5,
            CometRain = 6,
            DoubleLeaps = 7,
            TachyonRicochet = 8,
            DualDrillLaser = 9,
            AstralNovaRing = 10,
            DesperationSingularity = 11,
            DeathAnimation = 12,
            VictoryDespawn = 13
        }

        public enum AureusFrameType
        {
            Idle = 0,
            SitAndRecharge = 1,
            Walk = 2,
            Jump = 3,
            Stomp = 4
        }
        #endregion

        #region Local Structs
        private struct OrbitalProbe
        {
            public float Angle;
            public float Speed;
            public int FireTimer;

            public OrbitalProbe(float angle, float speed)
            {
                Angle = angle;
                Speed = speed;
                FireTimer = 0;
            }
        }

        private struct LaserGridLine
        {
            public Vector2 Start;
            public Vector2 End;
            public int Timer;
            public bool IsVertical;

            public LaserGridLine(Vector2 start, Vector2 end, int timer, bool isVertical)
            {
                Start = start;
                End = end;
                Timer = timer;
                IsVertical = isVertical;
            }
        }

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

        private struct OrbitSpark
        {
            public float Angle;
            public float Distance;
            public float Speed;
            public int Type;

            public OrbitSpark(float angle, float distance, float speed, int type)
            {
                Angle = angle;
                Distance = distance;
                Speed = speed;
                Type = type;
            }
        }

        private struct BounceStar
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public int Bounces;

            public BounceStar(Vector2 pos, Vector2 vel)
            {
                Position = pos;
                Velocity = vel;
                Bounces = 0;
            }
        }
        #endregion

        #region Local Fields
        // Drawing & opacity variables
        private float telegraphAlpha = 0f;
        private float singularityAlpha = 0f;
        private float arenaPulseScale = 1f;
        private Vector2 singularityCenter = Vector2.Zero;

        // Custom Trajectory Prediction for Leaps
        private readonly List<PredictionPoint> leapPredictionPath = new();
        private bool drawPredictionPath = false;

        // Jump & general movement counters
        private int leapStage = 0;
        private Vector2 leapHoverTarget = Vector2.Zero;

        // Sweeping drill lasers
        private float drillLaserProgress = 0f;
        private float drillLaserAngleLeft = 0f;
        private float drillLaserAngleRight = 0f;
        private float telegraphLineAlpha = 0f;

        // Desperation settings
        private readonly List<OrbitalProbe> orbitalProbes = new();
        private readonly List<LaserGridLine> laserGridLines = new();
        private int gridSpawnCooldown = 0;

        // Visual timers and particle trackers
        private int ticksRunning = 0;
        private bool hasAnnouncedDesperation = false;

        // Biomechanical footstep trackers for sound triggers
        private int footstepTimer = 0;

        // Particle vectors for gravity well simulation
        private readonly List<Vector2> gravityWellParticles = new();
        private float gravityWellSpin = 0f;

        // Orbiting sparks for Star Furnace Recharge state
        private readonly List<OrbitSpark> furnaceSparks = new();

        // Bouncing stars for Tachyon Ricochet state
        private readonly List<BounceStar> bouncingStars = new();
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
            npc.noGravity = false;
            npc.noTileCollide = false;

            // Initialize Phase 1
            if (currentPhase == 0)
            {
                currentPhase = 1;
                npc.ai[0] = 1f;
                state = AttackState.SpawnRoar;
                npc.ai[1] = (float)state;
                npc.netUpdate = true;
            }

            // Clear Calamity debuffs dynamically to ensure compilation safety and balance normalization
            ClearAureusDebuffs(target);

            // Enrage factor based on whether target is in the Astral Infection biome or Boss Rush is active
            float enrageFactor = 1f;
            if (target.Calamity() != null && !target.Calamity().ZoneAstral && !BossRushEvent.BossRushActive)
            {
                npc.Calamity().CurrentlyEnraged = true;
                enrageFactor = 1.4f;
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
                case AttackState.WalkAndShoot:
                    ExecuteState_WalkAndShoot(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.CyberLeap:
                    ExecuteState_CyberLeap(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.RocketBarrage:
                    ExecuteState_RocketBarrage(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.StarFurnaceRecharge:
                    ExecuteState_StarFurnaceRecharge(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.OverclockWalk:
                    ExecuteState_OverclockWalk(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.CometRain:
                    ExecuteState_CometRain(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.DoubleLeaps:
                    ExecuteState_DoubleLeaps(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.TachyonRicochet:
                    ExecuteState_TachyonRicochet(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.DualDrillLaser:
                    ExecuteState_DualDrillLaser(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.AstralNovaRing:
                    ExecuteState_AstralNovaRing(npc, target, ref timer, ref stateTracker, currentPhase, enrageFactor);
                    break;
                case AttackState.DesperationSingularity:
                    ExecuteState_DesperationSingularity(npc, target, ref timer, ref stateTracker, enrageFactor);
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
            float lifeRatio = npc.lifeMax <= 0 ? 1f : npc.life / (float)npc.lifeMax;

            // Desperation Phase Transition (under 18% HP)
            if (phase < 4 && lifeRatio <= PhaseLifeRatios[2])
            {
                phase = 4;
                npc.ai[0] = 4f;
                TransitionToAttack(npc, AttackState.DesperationSingularity);
                if (Main.netMode != NetmodeID.MultiplayerClient && !hasAnnouncedDesperation)
                {
                    hasAnnouncedDesperation = true;
                    BroadcastAlert("Astrum Aureus initiates stellar core collapse!");
                }
                return;
            }

            // Phase 3 Transition (under 46% HP)
            if (phase == 2 && lifeRatio <= PhaseLifeRatios[1])
            {
                phase = 3;
                npc.ai[0] = 3f;
                TransitionToAttack(npc, AttackState.DualDrillLaser);
                BroadcastAlert("Astrum Aureus' laser cores are fully operational!");
                return;
            }

            // Phase 2 Transition (under 72% HP)
            if (phase == 1 && lifeRatio <= PhaseLifeRatios[0])
            {
                phase = 2;
                npc.ai[0] = 2f;
                TransitionToAttack(npc, AttackState.OverclockWalk);
                BroadcastAlert("Astrum Aureus' engines are overclocking!");
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

            // Clean jump mechanics
            leapStage = 0;
            drawPredictionPath = false;
            leapPredictionPath.Clear();

            // Clean laser mechanics
            drillLaserProgress = 0f;
            drillLaserAngleLeft = 0f;
            drillLaserAngleRight = 0f;
            telegraphLineAlpha = 0f;

            // Clean desperation mechanics
            laserGridLines.Clear();
            gridSpawnCooldown = 0;

            // Clean state lists
            furnaceSparks.Clear();
            bouncingStars.Clear();

            npc.netUpdate = true;
        }

        /// <summary>
        /// Selects the next state in the phase-specific attack cycle.
        /// </summary>
        private void SelectNextAttack(NPC npc, int phase)
        {
            AttackState nextState = AttackState.WalkAndShoot;
            int currentAttack = (int)npc.ai[1];

            if (phase == 1)
            {
                // P1 Cycle: WalkAndShoot -> CyberLeap -> RocketBarrage -> StarFurnaceRecharge
                if (currentAttack == (int)AttackState.WalkAndShoot)
                    nextState = AttackState.CyberLeap;
                else if (currentAttack == (int)AttackState.CyberLeap)
                    nextState = AttackState.RocketBarrage;
                else if (currentAttack == (int)AttackState.RocketBarrage)
                    nextState = AttackState.StarFurnaceRecharge;
                else
                    nextState = AttackState.WalkAndShoot;
            }
            else if (phase == 2)
            {
                // P2 Cycle: OverclockWalk -> CometRain -> DoubleLeaps -> TachyonRicochet
                if (currentAttack == (int)AttackState.OverclockWalk)
                    nextState = AttackState.CometRain;
                else if (currentAttack == (int)AttackState.CometRain)
                    nextState = AttackState.DoubleLeaps;
                else if (currentAttack == (int)AttackState.DoubleLeaps)
                    nextState = AttackState.TachyonRicochet;
                else
                    nextState = AttackState.OverclockWalk;
            }
            else if (phase == 3)
            {
                // P3 Cycle: DualDrillLaser -> AstralNovaRing -> OverclockWalk
                if (currentAttack == (int)AttackState.DualDrillLaser)
                    nextState = AttackState.AstralNovaRing;
                else if (currentAttack == (int)AttackState.AstralNovaRing)
                    nextState = AttackState.OverclockWalk;
                else
                    nextState = AttackState.DualDrillLaser;
            }
            else
            {
                nextState = AttackState.DesperationSingularity;
            }

            TransitionToAttack(npc, nextState);
        }

        /// <summary>
        /// Clears Calamity's Astral Infection debuff from the player to prevent unfair damage stacking.
        /// </summary>
        private void ClearAureusDebuffs(Player player)
        {
            if (ModContent.TryFind("CalamityMod", "AstralInfectionDebuff", out ModBuff buff))
            {
                if (player.HasBuff(buff.Type))
                {
                    player.ClearBuff(buff.Type);
                }
            }
            if (ModContent.TryFind("CalamityMod", "AstralInfection", out ModBuff buffV2))
            {
                if (player.HasBuff(buffV2.Type))
                {
                    player.ClearBuff(buffV2.Type);
                }
            }
        }
        #endregion

        #region Custom Gravity & Collision Engine
        /// <summary>
        /// Standard gravity calculations for Astrum Aureus when grounded or airborne.
        /// </summary>
        private void ApplyGravity(NPC npc, float gravity = 0.58f, float maxFallSpeed = 18f)
        {
            if (npc.noGravity)
                return;

            if (npc.wet)
            {
                gravity *= 0.6f;
                maxFallSpeed *= 0.7f;
            }

            npc.velocity.Y += gravity;
            if (npc.velocity.Y > maxFallSpeed)
                npc.velocity.Y = maxFallSpeed;
        }

        /// <summary>
        /// Custom tile collision logic to step over platforms, block ledges, or walk upward.
        /// </summary>
        private void DoTileCollision(NPC npc, Player target)
        {
            if (npc.noTileCollide)
                return;

            int searchWidth = 120;
            int searchHeight = 30;
            Vector2 checkPosition = new(npc.Center.X - searchWidth * 0.5f, npc.Bottom.Y - searchHeight);

            bool standingOnPlatforms = false;
            for (int i = (int)(npc.BottomLeft.X / 16f); i <= (int)(npc.BottomRight.X / 16f); i++)
            {
                Tile tile = Framing.GetTileSafely(i, (int)(npc.Bottom.Y / 16f) + 1);
                if (tile.HasUnactuatedTile && (Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]))
                {
                    standingOnPlatforms = true;
                    break;
                }
            }

            // If player is significantly higher, ignore platform collision to step up
            if (target.Top.Y < npc.Bottom.Y - 140f)
            {
                standingOnPlatforms = false;
            }

            if (Collision.SolidCollision(checkPosition, searchWidth, searchHeight) || standingOnPlatforms)
            {
                if (npc.velocity.Y > 0f)
                    npc.velocity.Y = 0f;

                if (npc.velocity.Y > -0.2f)
                    npc.velocity.Y -= 0.06f;
                else
                    npc.velocity.Y -= 0.22f;

                if (npc.velocity.Y < -4f)
                    npc.velocity.Y = -4f;

                // Step up blocks to climb hills or reach player
                if (npc.Center.Y > target.Bottom.Y && npc.velocity.Y > -14f)
                {
                    npc.velocity.Y -= 0.18f;
                }
            }
            else
            {
                if (npc.velocity.Y < 0f)
                    npc.velocity.Y = 0f;

                if (npc.velocity.Y < 0.1f)
                    npc.velocity.Y += 0.05f;
                else
                    npc.velocity.Y += 0.5f;
            }
        }
        #endregion

        #region Attack State Executions

        #region Phase 1 States
        /// <summary>
        /// AttackState 0: Play spawn animation, shake camera, and emit energy flares.
        /// </summary>
        private void ExecuteState_SpawnRoar(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.velocity.X *= 0.82f;
            npc.damage = 0;
            npc.localAI[0] = (float)AureusFrameType.SitAndRecharge;

            // Spawning visual particles
            if (timer % 4 == 0)
            {
                SpawnDustPerfectRing(npc.Center, 50f, DustID.OrangeTorch, 12, 1.2f);
                SpawnDustPerfectRing(npc.Center, 50f, DustID.BlueTorch, 12, 1.2f);
            }

            if (timer == 40)
            {
                SoundEngine.PlaySound(SoundID.Roar with { Pitch = -0.25f, Volume = 1.3f }, npc.Center);
                target.Calamity().GeneralScreenShakePower = 11f;
            }

            if (timer >= 100)
            {
                SelectNextAttack(npc, 1);
            }
        }

        /// <summary>
        /// AttackState 1: Walking on ground, launching arcing sweeps of lasers.
        /// </summary>
        private void ExecuteState_WalkAndShoot(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            npc.localAI[0] = (float)AureusFrameType.Walk;
            npc.spriteDirection = (target.Center.X > npc.Center.X) ? 1 : -1;

            float horizontalDistance = Math.Abs(target.Center.X - npc.Center.X);
            float walkSpeed = MathHelper.Lerp(4.5f, 7.5f, 1f - (npc.life / (float)npc.lifeMax)) * enrage;

            if (horizontalDistance > 90f)
            {
                float steer = (target.Center.X > npc.Center.X) ? walkSpeed : -walkSpeed;
                npc.velocity.X = (npc.velocity.X * 13f + steer) / 14f;
            }
            else
            {
                npc.velocity.X *= 0.85f;
            }

            ApplyGravity(npc);
            DoTileCollision(npc, target);
            PlayWalkSounds(npc);

            // Periodic laser bursts
            int shootRate = Math.Max(38, 70 - phase * 8);
            if (timer % shootRate == 0)
            {
                SoundEngine.PlaySound(LaserFireSound, npc.Center);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int laserCount = 5 + phase;
                    int projLaserType = GetCalamityProjectile(ProjLaser, ProjectileID.DeathLaser);
                    int damage = ScaleDamage(npc, 98);

                    Vector2 direction = SafeNormalize(target.Center - npc.Center, Vector2.UnitY);
                    float spread = 0.55f;
                    float startAngle = -spread * 0.5f;

                    for (int i = 0; i < laserCount; i++)
                    {
                        float angle = startAngle + (spread * i / (laserCount - 1));
                        Vector2 velocity = direction.RotatedBy(angle) * 11.5f;
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center + velocity * 2f, velocity, projLaserType, damage, 1.5f, Main.myPlayer);
                    }
                }
            }

            if (timer >= 320)
            {
                SelectNextAttack(npc, phase);
            }
        }

        /// <summary>
        /// AttackState 2: Cyber leap high above target, hover with warning beam, slam down with shockwaves.
        /// </summary>
        private void ExecuteState_CyberLeap(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            ApplyGravity(npc);

            // Stages:
            // 0 = Preparation/Windup
            // 1 = High Ascent and Hovering
            // 2 = High Speed Slam
            // 3 = Slam Recovery
            switch (leapStage)
            {
                case 0:
                    npc.localAI[0] = (float)AureusFrameType.Jump;
                    npc.velocity.X *= 0.78f;

                    // Generate a preview landing path using player predictive position
                    if (timer == 1)
                    {
                        CalculateLeapTrajectory(npc, target);
                    }

                    if (timer >= 25)
                    {
                        npc.noTileCollide = true;
                        float horizontalForce = Math.Sign(target.Center.X - npc.Center.X) * 10f;
                        npc.velocity = new Vector2(horizontalForce, -23f);
                        SoundEngine.PlaySound(LeapSound, npc.Center);

                        leapStage = 1;
                        timer = 0;
                        npc.netUpdate = true;
                    }
                    break;

                case 1:
                    npc.localAI[0] = (float)AureusFrameType.Jump;
                    npc.noGravity = true;
                    npc.noTileCollide = true;

                    // Float to hovering position above target
                    Vector2 hoverDestination = target.Center - Vector2.UnitY * 370f;
                    npc.velocity = Vector2.Lerp(npc.velocity, (hoverDestination - npc.Center) * 0.18f, 0.13f);

                    // Fade in telegraph line
                    telegraphAlpha = MathHelper.Clamp(telegraphAlpha + 0.08f, 0f, 1f);

                    if (timer >= 55 || Vector2.Distance(npc.Center, hoverDestination) < 70f)
                    {
                        npc.velocity = new Vector2(npc.velocity.X * 0.1f, 1f);
                        leapStage = 2;
                        timer = 0;
                        drawPredictionPath = false;
                        npc.netUpdate = true;
                    }
                    break;

                case 2:
                    npc.localAI[0] = (float)AureusFrameType.Stomp;
                    npc.noGravity = true;
                    npc.noTileCollide = true;

                    // Heavy acceleration down
                    npc.velocity.Y = MathHelper.Lerp(npc.velocity.Y, 34f, 0.13f);
                    npc.velocity.X *= 0.94f;

                    telegraphAlpha = MathHelper.Clamp(telegraphAlpha - 0.12f, 0f, 1f);

                    // Ground contact check
                    bool hasHitGround = false;
                    for (int i = (int)(npc.BottomLeft.X / 16f); i <= (int)(npc.BottomRight.X / 16f); i++)
                    {
                        Tile tile = Framing.GetTileSafely(i, (int)(npc.Bottom.Y / 16f) + 1);
                        if (tile.HasUnactuatedTile && Main.tileSolid[tile.TileType])
                        {
                            hasHitGround = true;
                            break;
                        }
                    }

                    if (npc.Bottom.Y >= target.Bottom.Y + 260f)
                    {
                        hasHitGround = true;
                    }

                    if (hasHitGround)
                    {
                        npc.velocity = Vector2.Zero;
                        npc.noGravity = false;
                        npc.noTileCollide = false;

                        // Heavy stomp feedback
                        SoundEngine.PlaySound(StompSound, npc.Center);
                        target.Calamity().GeneralScreenShakePower = 12f;

                        // Spawn circular sparks and shockwaves
                        if (Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            int cometCount = 6 + phase;
                            int projCometType = GetCalamityProjectile(ProjComet, ProjectileID.HallowStar);
                            int damage = ScaleDamage(npc, 110);

                            for (int i = 0; i < cometCount; i++)
                            {
                                float angle = -PiOver2 + (Pi * i / (cometCount - 1));
                                Vector2 velocity = angle.ToRotationVector2() * 12.5f;
                                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Bottom - Vector2.UnitY * 30f, velocity, projCometType, damage, 2f, Main.myPlayer);
                            }

                            // Vanilla smasher shockwave
                            Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Bottom, Vector2.UnitY, ProjectileID.DD2OgreSmash, 0, 0f, Main.myPlayer);
                        }

                        // Create ground particles
                        SpawnDustPerfectRing(npc.Bottom, 30f, DustID.OrangeTorch, 15, 2f);
                        SpawnDustPerfectRing(npc.Bottom, 30f, DustID.BlueTorch, 15, 2f);

                        leapStage = 3;
                        timer = 0;
                        npc.netUpdate = true;
                    }
                    break;

                case 3:
                    npc.localAI[0] = (float)AureusFrameType.SitAndRecharge;
                    npc.velocity.X *= 0.82f;

                    if (timer >= 45)
                    {
                        SelectNextAttack(npc, phase);
                    }
                    break;
            }
        }

        /// <summary>
        /// AttackState 3: Stand firm and launch redirecting rockets with preview arcs.
        /// </summary>
        private void ExecuteState_RocketBarrage(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            npc.localAI[0] = (float)AureusFrameType.SitAndRecharge;
            npc.velocity.X *= 0.88f;

            ApplyGravity(npc);
            DoTileCollision(npc, target);

            int interval = Math.Max(12, 22 - phase * 3);
            if (timer > 35 && timer < 150 && timer % interval == 0)
            {
                SoundEngine.PlaySound(NukeFireSound, npc.Center);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int projRocketType = GetCalamityProjectile(ProjRocket, ProjectileID.RocketI);
                    int damage = ScaleDamage(npc, 102);

                    Vector2 aimDirection = SafeNormalize(target.Center - npc.Center, -Vector2.UnitY);
                    Vector2 rocketVelocity = aimDirection.RotatedBy(Main.rand.NextFloat(-0.35f, 0.35f)) * 12f;

                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, rocketVelocity, projRocketType, damage, 1.5f, Main.myPlayer);
                }

                // Spawn exhaust particles
                for (int i = 0; i < 8; i++)
                {
                    Dust d = Dust.NewDustPerfect(npc.Center, DustID.Smoke, Main.rand.NextVector2Circular(4f, 4f), 100, default, 1.5f);
                    d.noGravity = true;
                }
            }

            if (timer >= 210)
            {
                SelectNextAttack(npc, phase);
            }
        }

        /// <summary>
        /// AttackState 4: Sits down and generates an energy shield that draws in particles and fires orbiting sparks.
        /// </summary>
        private void ExecuteState_StarFurnaceRecharge(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            npc.localAI[0] = (float)AureusFrameType.SitAndRecharge;
            npc.velocity.X *= 0.82f;
            npc.defense = npc.defDefense + 70; // Massively increased defense during shield

            ApplyGravity(npc);
            DoTileCollision(npc, target);

            // Populate orbiting sparks initially
            if (timer == 1)
            {
                furnaceSparks.Clear();
                int sparkCount = 8;
                for (int i = 0; i < sparkCount; i++)
                {
                    float angle = TwoPi * i / sparkCount;
                    furnaceSparks.Add(new OrbitSpark(angle, 140f, 0.05f, i % 2));
                }
            }

            // Draw inward gravity well particles
            UpdateGravityWellParticles(npc);

            // Update orbiting furnace sparks
            for (int i = furnaceSparks.Count - 1; i >= 0; i--)
            {
                OrbitSpark spark = furnaceSparks[i];
                spark.Angle += spark.Speed;
                if (spark.Angle > TwoPi)
                    spark.Angle -= TwoPi;

                Vector2 sparkPos = npc.Center + spark.Angle.ToRotationVector2() * spark.Distance;

                // Periodically fire spark outward targeting player
                if (timer > 40 && timer % 30 == 0 && i % 3 == 0)
                {
                    SoundEngine.PlaySound(SoundID.Item9, sparkPos);

                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        int projType = GetCalamityProjectile(ProjFlame, ProjectileID.Spark);
                        int damage = ScaleDamage(npc, 94);
                        Vector2 vel = SafeNormalize(target.Center - sparkPos, Vector2.UnitY) * 9.5f;
                        Projectile.NewProjectile(npc.GetSource_FromAI(), sparkPos, vel, projType, damage, 1.0f, Main.myPlayer);
                    }
                }

                furnaceSparks[i] = spark;
            }

            if (timer >= 240)
            {
                SelectNextAttack(npc, phase);
            }
        }
        #endregion

        #region Phase 2 States
        /// <summary>
        /// AttackState 5: High-speed walking. Spawns comets from heaven and shoots rapid dual lasers.
        /// </summary>
        private void ExecuteState_OverclockWalk(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            npc.localAI[0] = (float)AureusFrameType.Walk;
            npc.spriteDirection = (target.Center.X > npc.Center.X) ? 1 : -1;

            float horizontalDistance = Math.Abs(target.Center.X - npc.Center.X);
            float walkSpeed = 9f * enrage;

            // Enrage speed boost if player stays too far
            if (Vector2.Distance(npc.Center, target.Center) > 950f)
            {
                walkSpeed *= 1.5f;
            }

            if (horizontalDistance > 70f)
            {
                float steer = (target.Center.X > npc.Center.X) ? walkSpeed : -walkSpeed;
                npc.velocity.X = (npc.velocity.X * 11f + steer) / 12f;
            }
            else
            {
                npc.velocity.X *= 0.82f;
            }

            ApplyGravity(npc);
            DoTileCollision(npc, target);
            PlayWalkSounds(npc);

            // Laser volley sweeps
            if (timer % 24 == 0)
            {
                SoundEngine.PlaySound(LaserFireSound, npc.Center);
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int projLaserType = GetCalamityProjectile(ProjLaser, ProjectileID.DeathLaser);
                    int damage = ScaleDamage(npc, 104);

                    Vector2 direction = SafeNormalize(target.Center - npc.Center, Vector2.UnitY);
                    for (int i = -1; i <= 1; i += 2)
                    {
                        Vector2 velocity = direction.RotatedBy(i * 0.2f) * 14f;
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center + velocity * 2f, velocity, projLaserType, damage, 1.5f, Main.myPlayer);
                    }
                }
            }

            // Falling overhead comets
            if (timer % 42 == 0)
            {
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int projCometType = GetCalamityProjectile(ProjComet, ProjectileID.HallowStar);
                    int damage = ScaleDamage(npc, 116);

                    Vector2 spawnPosition = target.Center + new Vector2(Main.rand.NextFloat(-450f, 450f), -650f);
                    Vector2 velocity = new(0f, 13f);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), spawnPosition, velocity, projCometType, damage, 2f, Main.myPlayer);
                }
            }

            if (timer >= 280)
            {
                SelectNextAttack(npc, phase);
            }
        }

        /// <summary>
        /// AttackState 6: Sits, gains defense, channels energy particles, and rains comets preceded by guide beams.
        /// </summary>
        private void ExecuteState_CometRain(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            npc.localAI[0] = (float)AureusFrameType.SitAndRecharge;
            npc.velocity.X *= 0.85f;
            npc.defense = npc.defDefense + 45; // Gained defense shield

            ApplyGravity(npc);
            DoTileCollision(npc, target);

            // Draw in particles (gravity well simulation)
            if (timer < 85)
            {
                UpdateGravityWellParticles(npc);
            }
            else
            {
                gravityWellParticles.Clear();
            }

            // Charge finished, initiate rain
            if (timer >= 85 && timer < 340 && timer % 16 == 0)
            {
                SoundEngine.PlaySound(SoundID.Item77, target.Center);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int projCometType = GetCalamityProjectile(ProjComet, ProjectileID.HallowStar);
                    int damage = ScaleDamage(npc, 112);

                    // Spawn comet offset from target Y
                    Vector2 targetLoc = target.Center + new Vector2(Main.rand.NextFloat(-380f, 380f), 0f);
                    Vector2 spawnPos = targetLoc + new Vector2(Main.rand.NextFloat(-160f, 160f), -720f);
                    Vector2 vel = SafeNormalize(targetLoc - spawnPos, Vector2.UnitY) * 14.5f;

                    Projectile.NewProjectile(npc.GetSource_FromAI(), spawnPos, vel, projCometType, damage, 2.5f, Main.myPlayer);
                }
            }

            if (timer >= 390)
            {
                SelectNextAttack(npc, phase);
            }
        }

        /// <summary>
        /// AttackState 7: Performs two cyber leaps back-to-back, releasing comets and shockwaves on each stomp.
        /// </summary>
        private void ExecuteState_DoubleLeaps(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            ApplyGravity(npc);

            int targetStomps = 2;

            switch (leapStage)
            {
                case 0:
                    npc.localAI[0] = (float)AureusFrameType.Jump;
                    npc.velocity.X *= 0.72f;

                    if (timer == 1)
                    {
                        CalculateLeapTrajectory(npc, target);
                    }

                    if (timer >= 18)
                    {
                        npc.noTileCollide = true;
                        float horizontalForce = Math.Sign(target.Center.X - npc.Center.X) * 12f;
                        npc.velocity = new Vector2(horizontalForce, -24f);
                        SoundEngine.PlaySound(LeapSound, npc.Center);

                        leapStage = 1;
                        timer = 0;
                        npc.netUpdate = true;
                    }
                    break;

                case 1:
                    npc.localAI[0] = (float)AureusFrameType.Jump;
                    npc.noGravity = true;
                    npc.noTileCollide = true;

                    Vector2 hoverDestination = target.Center - Vector2.UnitY * 390f;
                    npc.velocity = Vector2.Lerp(npc.velocity, (hoverDestination - npc.Center) * 0.2f, 0.15f);

                    telegraphAlpha = MathHelper.Clamp(telegraphAlpha + 0.1f, 0f, 1f);

                    if (timer >= 45 || Vector2.Distance(npc.Center, hoverDestination) < 70f)
                    {
                        npc.velocity = new Vector2(npc.velocity.X * 0.1f, 1f);
                        leapStage = 2;
                        timer = 0;
                        drawPredictionPath = false;
                        npc.netUpdate = true;
                    }
                    break;

                case 2:
                    npc.localAI[0] = (float)AureusFrameType.Stomp;
                    npc.noGravity = true;
                    npc.noTileCollide = true;

                    npc.velocity.Y = MathHelper.Lerp(npc.velocity.Y, 36f, 0.15f);
                    npc.velocity.X *= 0.94f;

                    telegraphAlpha = MathHelper.Clamp(telegraphAlpha - 0.15f, 0f, 1f);

                    bool hasHitGround = false;
                    for (int i = (int)(npc.BottomLeft.X / 16f); i <= (int)(npc.BottomRight.X / 16f); i++)
                    {
                        Tile tile = Framing.GetTileSafely(i, (int)(npc.Bottom.Y / 16f) + 1);
                        if (tile.HasUnactuatedTile && Main.tileSolid[tile.TileType])
                        {
                            hasHitGround = true;
                            break;
                        }
                    }

                    if (npc.Bottom.Y >= target.Bottom.Y + 260f)
                    {
                        hasHitGround = true;
                    }

                    if (hasHitGround)
                    {
                        npc.velocity = Vector2.Zero;
                        npc.noGravity = false;
                        npc.noTileCollide = false;

                        SoundEngine.PlaySound(StompSound, npc.Center);
                        target.Calamity().GeneralScreenShakePower = 13f;

                        if (Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            int cometCount = 8;
                            int projCometType = GetCalamityProjectile(ProjComet, ProjectileID.HallowStar);
                            int damage = ScaleDamage(npc, 115);

                            for (int i = 0; i < cometCount; i++)
                            {
                                float angle = -PiOver2 + (Pi * i / (cometCount - 1));
                                Vector2 velocity = angle.ToRotationVector2() * 13f;
                                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Bottom - Vector2.UnitY * 30f, velocity, projCometType, damage, 2.0f, Main.myPlayer);
                            }

                            // Trigger shockwaves
                            Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Bottom, Vector2.UnitY, ProjectileID.DD2OgreSmash, 0, 0f, Main.myPlayer);
                        }

                        // Ground stomp particles
                        SpawnDustPerfectRing(npc.Bottom, 35f, DustID.OrangeTorch, 18, 2.2f);
                        SpawnDustPerfectRing(npc.Bottom, 35f, DustID.BlueTorch, 18, 2.2f);

                        stateTracker += 1f;
                        leapStage = 3;
                        timer = 0;
                        npc.netUpdate = true;
                    }
                    break;

                case 3:
                    npc.localAI[0] = (float)AureusFrameType.SitAndRecharge;
                    npc.velocity.X *= 0.8f;

                    if (timer >= 32)
                    {
                        if (stateTracker >= targetStomps)
                        {
                            SelectNextAttack(npc, phase);
                        }
                        else
                        {
                            leapStage = 0; // jump again
                            timer = 0;
                            npc.netUpdate = true;
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// AttackState 8: Fires bounding tachyon stars that ricochet off screen boundaries to frame the player.
        /// </summary>
        private void ExecuteState_TachyonRicochet(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            npc.localAI[0] = (float)AureusFrameType.SitAndRecharge;
            npc.velocity.X *= 0.88f;

            ApplyGravity(npc);
            DoTileCollision(npc, target);

            // Periodically spawn bouncing stars locally or on server
            if (timer > 20 && timer < 180 && timer % 24 == 0)
            {
                SoundEngine.PlaySound(SoundID.Item24, npc.Center);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int projLaserType = GetCalamityProjectile(ProjLaser, ProjectileID.DeathLaser);
                    int damage = ScaleDamage(npc, 102);

                    Vector2 aimDir = SafeNormalize(target.Center - npc.Center, Vector2.UnitX);
                    Vector2 baseVel = aimDir.RotatedBy(Main.rand.NextFloat(-0.25f, 0.25f)) * 9f;

                    // Spawn ricocheting projectile
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, baseVel, projLaserType, damage, 1.0f, Main.myPlayer);
                }

                // Push visual helper bounce stars
                bouncingStars.Add(new BounceStar(npc.Center, SafeNormalize(target.Center - npc.Center, Vector2.UnitY).RotatedBy(Main.rand.NextFloat(-0.5f, 0.5f)) * 10f));
            }

            // Update local bouncing stars visual effects
            UpdateBouncingStars();

            if (timer >= 240)
            {
                bouncingStars.Clear();
                SelectNextAttack(npc, phase);
            }
        }
        #endregion

        #region Phase 3 States
        /// <summary>
        /// AttackState 9: Channels sweeping lasers (Blue and Orange) in opposite directions.
        /// </summary>
        private void ExecuteState_DualDrillLaser(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            npc.localAI[0] = (float)AureusFrameType.SitAndRecharge;
            npc.velocity.X *= 0.8f;
            ApplyGravity(npc);
            DoTileCollision(npc, target);

            int chargeDuration = 90;
            int sweepDuration = 180;

            // Stage 1: Telegraphing
            if (timer < chargeDuration)
            {
                telegraphLineAlpha = MathHelper.Clamp(timer / (float)chargeDuration, 0f, 1f);

                // Sweeping lines convergence preview
                float progress = timer / (float)chargeDuration;
                drillLaserAngleLeft = MathHelper.Lerp(-PiOver2, -Pi * 0.08f, progress);
                drillLaserAngleRight = MathHelper.Lerp(PiOver2, Pi * 0.08f, progress);

                if (timer == 20)
                {
                    SoundEngine.PlaySound(ChargeSound, npc.Center);
                }
            }
            // Stage 2: Active Sweeping Lasers
            else if (timer >= chargeDuration && timer < chargeDuration + sweepDuration)
            {
                telegraphLineAlpha = 0f;
                drillLaserProgress = (timer - chargeDuration) / (float)sweepDuration;

                // Sweeps outward in opposite directions
                drillLaserAngleLeft = MathHelper.Lerp(-Pi * 0.08f, -PiOver2 - 0.25f, drillLaserProgress);
                drillLaserAngleRight = MathHelper.Lerp(Pi * 0.08f, PiOver2 + 0.25f, drillLaserProgress);

                // Play sound intermittently
                if (timer % 20 == 0)
                {
                    SoundEngine.PlaySound(LaserFireSound, npc.Center);
                }

                // Spawn laser segments or rays at the sweeping coordinates
                if (Main.netMode != NetmodeID.MultiplayerClient && timer % 4 == 0)
                {
                    int projLaserType = GetCalamityProjectile(ProjLaser, ProjectileID.DeathLaser);
                    int damage = ScaleDamage(npc, 122);

                    // Left beam segment
                    Vector2 dirLeft = (drillLaserAngleLeft + PiOver2).ToRotationVector2();
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, dirLeft * 14.5f, projLaserType, damage, 1f, Main.myPlayer);

                    // Right beam segment
                    Vector2 dirRight = (drillLaserAngleRight + PiOver2).ToRotationVector2();
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, dirRight * 14.5f, projLaserType, damage, 1f, Main.myPlayer);
                }
            }

            if (timer >= chargeDuration + sweepDuration + 40)
            {
                SelectNextAttack(npc, phase);
            }
        }

        /// <summary>
        /// AttackState 10: Channels concentric expanding circles of lasers and comets from its center.
        /// </summary>
        private void ExecuteState_AstralNovaRing(NPC npc, Player target, ref float timer, ref float stateTracker, int phase, float enrage)
        {
            npc.localAI[0] = (float)AureusFrameType.SitAndRecharge;
            npc.velocity.X *= 0.84f;
            ApplyGravity(npc);
            DoTileCollision(npc, target);

            int pulseRate = 56;
            if (timer > 25 && timer < 230 && timer % pulseRate == 0)
            {
                SoundEngine.PlaySound(SoundID.Item109, npc.Center);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int ringCount = 14 + phase * 2;
                    int projLaserType = GetCalamityProjectile(ProjLaser, ProjectileID.DeathLaser);
                    int damage = ScaleDamage(npc, 106);

                    // Slightly randomize offset to prevent standing still exploits
                    float baseOffset = Main.rand.NextFloat(TwoPi);

                    for (int i = 0; i < ringCount; i++)
                    {
                        // Leave a 2-segment gap for player navigation
                        if (i == 4 || i == 5)
                            continue;

                        float angle = baseOffset + (TwoPi * i / ringCount);
                        Vector2 vel = angle.ToRotationVector2() * 8.8f;

                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center + vel * 2f, vel, projLaserType, damage, 1f, Main.myPlayer);
                    }
                }
            }

            if (timer >= 270)
            {
                SelectNextAttack(npc, phase);
            }
        }
        #endregion

        #region Phase 4 Desperation State
        /// <summary>
        /// AttackState 11: Desperation phase lock. Confines player to circular arena. Rains comets, grid lasers, and orbital probes.
        /// </summary>
        private void ExecuteState_DesperationSingularity(NPC npc, Player target, ref float timer, ref float stateTracker, float enrage)
        {
            // Lock position at singularity center
            if (timer == 1)
            {
                singularityCenter = npc.Center;
                singularityAlpha = 0f;

                // Spawn initial orbital probe settings
                orbitalProbes.Clear();
                int probeCount = 4;
                for (int i = 0; i < probeCount; i++)
                {
                    float angle = TwoPi * i / probeCount;
                    orbitalProbes.Add(new OrbitalProbe(angle, 0.038f));
                }
            }

            // Sit down and remain anchored
            npc.localAI[0] = (float)AureusFrameType.SitAndRecharge;
            npc.velocity = Vector2.Zero;
            npc.noGravity = true;
            npc.noTileCollide = true;
            npc.defense = npc.defDefense + 80;

            // Fade in boundary circle
            singularityAlpha = MathHelper.Clamp(singularityAlpha + 0.04f, 0f, 1f);

            // Pulsing scale animation
            arenaPulseScale = 1.0f + 0.04f * (float)Math.Sin(timer * 0.08f);

            // Confine target inside singularity boundary
            ForceTargetInsideSingularity(target);

            // 1. Core comet rings
            if (timer % 48 == 0)
            {
                SoundEngine.PlaySound(SoundID.Item74, npc.Center);
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int count = 10;
                    int projCometType = GetCalamityProjectile(ProjComet, ProjectileID.HallowStar);
                    int damage = ScaleDamage(npc, 120);

                    float baseAngle = Main.rand.NextFloat(TwoPi);
                    for (int i = 0; i < count; i++)
                    {
                        float angle = baseAngle + (TwoPi * i / count);
                        Vector2 vel = angle.ToRotationVector2() * 6.8f;
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, vel, projCometType, damage, 1.5f, Main.myPlayer);
                    }
                }
            }

            // 2. Spawn constellation grid warnings and detonators
            UpdateLaserGrid(target, enrage);

            // 3. Update orbital probes
            UpdateOrbitalProbes(npc, target);

            // Desperation has no timeout; fight concludes only on boss death
        }
        #endregion

        #region Death and Retreat States
        /// <summary>
        /// AttackState 12: Triggers death explosions and camera shakes.
        /// </summary>
        private void ExecuteState_DeathAnimation(NPC npc, ref float timer)
        {
            npc.velocity *= 0.9f;
            npc.localAI[0] = (float)AureusFrameType.SitAndRecharge;
            npc.damage = 0;

            if (timer % 5 == 0)
            {
                SoundEngine.PlaySound(SoundID.Item14, npc.Center);
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Vector2 randOffset = Main.rand.NextVector2Circular(120f, 120f);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center + randOffset, Vector2.Zero, ProjectileID.Fireball, 0, 0f, Main.myPlayer);
                }
            }

            if (timer >= 140)
            {
                npc.life = 0;
                npc.HitEffect();
                npc.active = false;
                npc.netUpdate = true;
            }
        }

        /// <summary>
        /// AttackState 13: Decelerates and floats away into screen top upon target death/retreat.
        /// </summary>
        private void ExecuteVictoryDespawn(NPC npc)
        {
            npc.velocity.X *= 0.9f;
            npc.velocity.Y -= 0.35f;
            if (npc.velocity.Y < -15f)
                npc.velocity.Y = -15f;

            npc.noTileCollide = true;
            npc.Opacity = MathHelper.Clamp(npc.Opacity - 0.02f, 0f, 1f);

            if (npc.Opacity <= 0f)
            {
                npc.active = false;
                npc.netUpdate = true;
            }
        }
        #endregion

        #endregion

        #region Desperation Arena & Grid Updates
        /// <summary>
        /// Restricts target inside the 660f boundary, applying massive damage if crossed.
        /// </summary>
        private void ForceTargetInsideSingularity(Player target)
        {
            float distance = Vector2.Distance(target.Center, singularityCenter);
            if (distance > ArenaRadius)
            {
                Vector2 pullVector = SafeNormalize(singularityCenter - target.Center, Vector2.UnitY);
                target.velocity += pullVector * 1.5f;

                if (Main.GameUpdateCount % 8 == 0)
                {
                    // Deal tick damage to player
                    target.Hurt(PlayerDeathReason.ByCustomReason(NetworkText.FromLiteral(target.name + " crossed the Astral Event Horizon!")), 38, 0);

                    // Spawn barrier failure dusts
                    for (int i = 0; i < 12; i++)
                    {
                        Dust d = Dust.NewDustPerfect(target.Center, DustID.PinkCrystalShard, Main.rand.NextVector2Circular(6f, 6f));
                        d.noGravity = true;
                    }
                }
            }
        }

        /// <summary>
        /// Updates the intersecting warning lines, spawning real lasers when countdown ends.
        /// </summary>
        private void UpdateLaserGrid(Player target, float enrage)
        {
            gridSpawnCooldown--;

            // Periodically add intersecting lines centered on the player
            if (gridSpawnCooldown <= 0)
            {
                gridSpawnCooldown = (int)(105 / enrage);

                // Horizontal laser grid line
                Vector2 horStart = target.Center + new Vector2(-900f, Main.rand.NextFloat(-50f, 50f));
                Vector2 horEnd = target.Center + new Vector2(900f, Main.rand.NextFloat(-50f, 50f));
                laserGridLines.Add(new LaserGridLine(horStart, horEnd, 55, false));

                // Vertical laser grid line
                Vector2 verStart = target.Center + new Vector2(Main.rand.NextFloat(-50f, 50f), -700f);
                Vector2 verEnd = target.Center + new Vector2(Main.rand.NextFloat(-50f, 50f), 700f);
                laserGridLines.Add(new LaserGridLine(verStart, verEnd, 55, true));
            }

            // Update warning line timers
            for (int i = laserGridLines.Count - 1; i >= 0; i--)
            {
                LaserGridLine grid = laserGridLines[i];
                grid.Timer--;

                if (grid.Timer <= 0)
                {
                    // Trigger actual laser blast
                    SoundEngine.PlaySound(SoundID.Item92, grid.Start + (grid.End - grid.Start) * 0.5f);

                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        int projLaserType = GetCalamityProjectile(ProjLaser, ProjectileID.DeathLaser);
                        int damage = ScaleDamage(Main.npc[0], 120); // Fallback to safe scaling

                        Vector2 dir = SafeNormalize(grid.End - grid.Start, Vector2.UnitX);
                        float step = 60f;
                        float len = Vector2.Distance(grid.Start, grid.End);

                        // Spawn sequence of lasers along the line path
                        for (float d = 0; d < len; d += step)
                        {
                            Vector2 spot = grid.Start + dir * d;
                            // Only spawn if inside the singularity arena
                            if (Vector2.Distance(spot, singularityCenter) < ArenaRadius + 30f)
                            {
                                Projectile.NewProjectile(null, spot, dir * 8f, projLaserType, damage, 0.5f, Main.myPlayer);
                            }
                        }
                    }

                    laserGridLines.RemoveAt(i);
                }
                else
                {
                    laserGridLines[i] = grid; // Re-assign structure updates
                }
            }
        }

        /// <summary>
        /// Updates the orbit angles and firing timers of boundary orbital probe targets.
        /// </summary>
        private void UpdateOrbitalProbes(NPC npc, Player target)
        {
            for (int i = 0; i < orbitalProbes.Count; i++)
            {
                OrbitalProbe probe = orbitalProbes[i];

                // Orbit rotation
                probe.Angle += probe.Speed;
                if (probe.Angle > TwoPi)
                    probe.Angle -= TwoPi;

                probe.FireTimer++;
                Vector2 probePos = singularityCenter + probe.Angle.ToRotationVector2() * ArenaRadius;

                // Fire laser inward targeting player
                if (probe.FireTimer >= 80)
                {
                    probe.FireTimer = 0;
                    SoundEngine.PlaySound(SoundID.Item33, probePos);

                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        int projLaserType = GetCalamityProjectile(ProjLaser, ProjectileID.DeathLaser);
                        int damage = ScaleDamage(npc, 90);
                        Vector2 vel = SafeNormalize(target.Center - probePos, Vector2.UnitY) * 9f;

                        Projectile.NewProjectile(npc.GetSource_FromAI(), probePos, vel, projLaserType, damage, 0.5f, Main.myPlayer);
                    }
                }

                orbitalProbes[i] = probe; // Save struct state
            }
        }

        /// <summary>
        /// Updates positions of bouncing tachyon stars, handling screen border collision.
        /// </summary>
        private void UpdateBouncingStars()
        {
            Rectangle screenRect = new((int)Main.screenPosition.X, (int)Main.screenPosition.Y, Main.screenWidth, Main.screenHeight);

            for (int i = bouncingStars.Count - 1; i >= 0; i--)
            {
                BounceStar star = bouncingStars[i];
                star.Position += star.Velocity;

                // Check collision with screen boundaries
                bool bounced = false;
                if (star.Position.X < screenRect.Left || star.Position.X > screenRect.Right)
                {
                    star.Velocity.X = -star.Velocity.X;
                    bounced = true;
                }
                if (star.Position.Y < screenRect.Top || star.Position.Y > screenRect.Bottom)
                {
                    star.Velocity.Y = -star.Velocity.Y;
                    bounced = true;
                }

                if (bounced)
                {
                    star.Bounces++;
                    SoundEngine.PlaySound(SoundID.Item10 with { Volume = 0.5f }, star.Position);

                    // Kick off particles
                    for (int j = 0; j < 5; j++)
                    {
                        Dust d = Dust.NewDustPerfect(star.Position, DustID.OrangeTorch, Main.rand.NextVector2Circular(3f, 3f));
                        d.noGravity = true;
                    }
                }

                if (star.Bounces > 4 || Vector2.Distance(star.Position, singularityCenter) > 2000f)
                {
                    bouncingStars.RemoveAt(i);
                }
                else
                {
                    bouncingStars[i] = star;
                }
            }
        }
        #endregion

        #region Animation & Sound Controller
        /// <summary>
        /// Custom frame updates based on the current AureusFrameType.
        /// </summary>
        public override void FindFrame(NPC npc, int frameHeight)
        {
            npc.frameCounter++;

            AureusFrameType fType = (AureusFrameType)(int)npc.localAI[0];
            int interval = 8;

            if (fType == AureusFrameType.Walk)
            {
                // Speed up walking animation based on horizontal movement speed
                interval = Math.Clamp((int)(9 - Math.Abs(npc.velocity.X) * 0.7f), 1, 9);
            }

            if (npc.frameCounter >= interval)
            {
                npc.frame.Y += frameHeight;
                if (npc.frame.Y >= frameHeight * Main.npcFrameCount[npc.type])
                {
                    npc.frame.Y = 0;
                }
                npc.frameCounter = 0;
            }
        }

        /// <summary>
        /// Plays heavy mechanical walking sound effects matched with walk frames.
        /// </summary>
        private void PlayWalkSounds(NPC npc)
        {
            AureusFrameType fType = (AureusFrameType)(int)npc.localAI[0];
            if (fType != AureusFrameType.Walk)
            {
                footstepTimer = 0;
                return;
            }

            footstepTimer++;
            int stepInterval = Math.Clamp((int)(36 - Math.Abs(npc.velocity.X) * 2.2f), 12, 36);

            if (footstepTimer >= stepInterval)
            {
                footstepTimer = 0;
                SoundEngine.PlaySound(SoundID.Item53 with { Volume = 0.6f, Pitch = -0.4f }, npc.Center); // Heavy metal thud

                // Kick up dust perfect particles at feet
                for (int i = 0; i < 4; i++)
                {
                    Dust d = Dust.NewDustPerfect(npc.Bottom, DustID.Smoke, new Vector2(Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-1f, -3f)), 100, default, 1.2f);
                    d.noGravity = true;
                }
            }
        }

        /// <summary>
        /// Generates gravity well visual particles drawing inward to the core.
        /// </summary>
        private void UpdateGravityWellParticles(NPC npc)
        {
            gravityWellSpin += 0.08f;
            if (gravityWellParticles.Count < 30)
            {
                gravityWellParticles.Add(Main.rand.NextVector2CircularEdge(250f, 250f));
            }

            for (int i = gravityWellParticles.Count - 1; i >= 0; i--)
            {
                Vector2 posOffset = gravityWellParticles[i];
                float distance = posOffset.Length();
                distance -= 4.5f;

                if (distance <= 12f)
                {
                    // Particle reached core, trigger flash
                    Dust d = Dust.NewDustPerfect(npc.Center, DustID.OrangeTorch, Main.rand.NextVector2Circular(3f, 3f), 100, default, 1.4f);
                    d.noGravity = true;
                    gravityWellParticles.RemoveAt(i);
                }
                else
                {
                    // Rotate and pull inward
                    float angle = posOffset.ToRotation() + 0.03f;
                    gravityWellParticles[i] = angle.ToRotationVector2() * distance;

                    // Spawn visual dust at particle location
                    if (Main.rand.NextBool(3))
                    {
                        int dustType = Main.rand.NextBool() ? DustID.OrangeTorch : DustID.BlueTorch;
                        Dust d = Dust.NewDustPerfect(npc.Center + gravityWellParticles[i], dustType, Vector2.Zero, 100, default, 0.9f);
                        d.noGravity = true;
                    }
                }
            }
        }

        /// <summary>
        /// Calculates landing target and populates predictive projection paths.
        /// </summary>
        private void CalculateLeapTrajectory(NPC npc, Player target)
        {
            leapPredictionPath.Clear();
            drawPredictionPath = true;

            // Anticipate landing coordinates based on player's current speed vector
            Vector2 anticipatedPos = target.Center + target.velocity * 18f;
            Vector2 start = npc.Center;
            Vector2 apex = (start + anticipatedPos) * 0.5f - Vector2.UnitY * 390f;

            // Plot quadratic Bezier curve
            int segments = 16;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector2 point = Vector2.Lerp(Vector2.Lerp(start, apex, t), Vector2.Lerp(apex, anticipatedPos, t), t);
                leapPredictionPath.Add(new PredictionPoint(point, 1f - t * 0.5f));
            }
        }
        #endregion

        #region Visual Telegraph & Custom Drawing
        /// <summary>
        /// Overrides sprite drawing to handle frame replacements, afterimages, lasers, and arena circles.
        /// </summary>
        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            Texture2D mainTex = TextureAssets.Npc[npc.type].Value;
            Texture2D glowTex = TextureAssets.Npc[npc.type].Value;
            SpriteEffects effects = npc.spriteDirection == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            AureusFrameType fType = (AureusFrameType)(int)npc.localAI[0];

            // Request correct animation textures from Calamity Mod based on active frame flag
            switch (fType)
            {
                case AureusFrameType.Idle:
                    glowTex = ModContent.Request<Texture2D>("CalamityMod/NPCs/AstrumAureus/AstrumAureusGlow").Value;
                    break;

                case AureusFrameType.SitAndRecharge:
                    mainTex = ModContent.Request<Texture2D>("CalamityMod/NPCs/AstrumAureus/AstrumAureusRecharge").Value;
                    break;

                case AureusFrameType.Walk:
                    mainTex = ModContent.Request<Texture2D>("CalamityMod/NPCs/AstrumAureus/AstrumAureusWalk").Value;
                    glowTex = ModContent.Request<Texture2D>("CalamityMod/NPCs/AstrumAureus/AstrumAureusWalkGlow").Value;
                    break;

                case AureusFrameType.Jump:
                    mainTex = ModContent.Request<Texture2D>("CalamityMod/NPCs/AstrumAureus/AstrumAureusJump").Value;
                    glowTex = ModContent.Request<Texture2D>("CalamityMod/NPCs/AstrumAureus/AstrumAureusJumpGlow").Value;
                    break;

                case AureusFrameType.Stomp:
                    mainTex = ModContent.Request<Texture2D>("CalamityMod/NPCs/AstrumAureus/AstrumAureusStomp").Value;
                    glowTex = ModContent.Request<Texture2D>("CalamityMod/NPCs/AstrumAureus/AstrumAureusStompGlow").Value;
                    break;
            }

            Vector2 origin = npc.frame.Size() * 0.5f;
            Vector2 drawPos = npc.Center - Main.screenPosition + Vector2.UnitY * npc.gfxOffY;

            // 1. Draw glowing afterimages if config allows
            if (CalamityClientConfig.Instance.Afterimages)
            {
                for (int i = 1; i < 8; i += 2)
                {
                    Color afterColor = npc.GetAlpha(Color.Lerp(drawColor, Color.White, 0.4f)) * ((8 - i) / 16f);
                    Vector2 oldDrawPos = npc.oldPos[i] + npc.Size * 0.5f - Main.screenPosition + Vector2.UnitY * npc.gfxOffY;
                    spriteBatch.Draw(mainTex, oldDrawPos, npc.frame, afterColor, npc.rotation, origin, npc.scale, effects, 0f);
                }
            }

            // 2. Draw normal main texture
            spriteBatch.Draw(mainTex, drawPos, npc.frame, npc.GetAlpha(drawColor), npc.rotation, origin, npc.scale, effects, 0f);

            // 3. Draw glowmask texture if applicable
            if (fType != AureusFrameType.SitAndRecharge && glowTex != TextureAssets.Npc[npc.type].Value)
            {
                spriteBatch.Draw(glowTex, drawPos, npc.frame, Color.White, npc.rotation, origin, npc.scale, effects, 0f);
            }

            // 4. Draw Cyber Leap downward telegraph line
            if (telegraphAlpha > 0f)
            {
                Color telColor = Color.Cyan * telegraphAlpha * 0.5f;
                DrawLine(spriteBatch, npc.Center, npc.Center + Vector2.UnitY * 1800f, telColor, 8f);
            }

            // 5. Draw Dual Drill Laser sweeping preview guides
            if (telegraphLineAlpha > 0f)
            {
                Color telL = Color.DeepSkyBlue * telegraphLineAlpha * 0.6f;
                Color telR = Color.OrangeRed * telegraphLineAlpha * 0.6f;

                // Left beam telegraph
                Vector2 endL = npc.Center + (drillLaserAngleLeft + PiOver2).ToRotationVector2() * 1400f;
                DrawLine(spriteBatch, npc.Center, endL, telL, 5f);

                // Right beam telegraph
                Vector2 endR = npc.Center + (drillLaserAngleRight + PiOver2).ToRotationVector2() * 1400f;
                DrawLine(spriteBatch, npc.Center, endR, telR, 5f);
            }

            // 6. Draw desperation grid warning lines
            if (laserGridLines.Count > 0)
            {
                for (int i = 0; i < laserGridLines.Count; i++)
                {
                    LaserGridLine grid = laserGridLines[i];
                    Color warnColor = (grid.Timer % 10 < 5) ? Color.Purple * 0.7f : Color.White * 0.4f;
                    DrawLine(spriteBatch, grid.Start, grid.End, warnColor, 3f);
                }
            }

            // 7. Draw desperation singularity boundary and orbiting probes
            if (singularityAlpha > 0f)
            {
                DrawSingularityArena(spriteBatch);
            }

            // 8. Draw leap trajectory prediction path if enabled
            if (drawPredictionPath && leapPredictionPath.Count > 1)
            {
                for (int i = 0; i < leapPredictionPath.Count - 1; i++)
                {
                    PredictionPoint p1 = leapPredictionPath[i];
                    PredictionPoint p2 = leapPredictionPath[i + 1];
                    Color arcColor = Color.DeepSkyBlue * p1.Alpha * 0.5f;
                    DrawLine(spriteBatch, p1.Position, p2.Position, arcColor, 4f);
                }
            }

            // 9. Draw Star Furnace orbiting sparks
            if (furnaceSparks.Count > 0)
            {
                for (int i = 0; i < furnaceSparks.Count; i++)
                {
                    OrbitSpark spark = furnaceSparks[i];
                    Vector2 sparkPos = npc.Center + spark.Angle.ToRotationVector2() * spark.Distance;
                    Color c = spark.Type == 0 ? Color.Orange : Color.Cyan;
                    spriteBatch.Draw(TextureAssets.BlackTile.Value, sparkPos - Main.screenPosition - new Vector2(4f, 4f), new Rectangle(0, 0, 1, 1), c, ticksRunning * 0.1f, Vector2.Zero, new Vector2(8f, 8f), SpriteEffects.None, 0f);
                }
            }

            // 10. Draw bouncing tachyon stars
            if (bouncingStars.Count > 0)
            {
                for (int i = 0; i < bouncingStars.Count; i++)
                {
                    BounceStar star = bouncingStars[i];
                    spriteBatch.Draw(TextureAssets.BlackTile.Value, star.Position - Main.screenPosition - new Vector2(6f, 6f), new Rectangle(0, 0, 1, 1), Color.Gold, ticksRunning * 0.15f, Vector2.Zero, new Vector2(12f, 12f), SpriteEffects.None, 0f);
                }
            }

            return false;
        }

        /// <summary>
        /// Renders additively blended thruster and core aura effects.
        /// </summary>
        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);

            Vector2 drawPos = npc.Center - Main.screenPosition + Vector2.UnitY * npc.gfxOffY;
            Texture2D glowTex = ModContent.Request<Texture2D>("CalamityMod/NPCs/AstrumAureus/AstrumAureusGlow").Value;
            Vector2 origin = npc.frame.Size() * 0.5f;

            // Render a pulsing stellar aura
            float pulseScale = 1.0f + 0.05f * (float)Math.Sin(ticksRunning * 0.1f);
            Color auraColor = Color.Lerp(Color.Cyan, Color.Purple, 0.5f + 0.5f * (float)Math.Sin(ticksRunning * 0.04f)) * 0.35f;
            SpriteEffects effects = npc.spriteDirection == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            spriteBatch.Draw(glowTex, drawPos, npc.frame, auraColor, npc.rotation, origin, npc.scale * pulseScale, effects, 0f);

            // Draw leg mechanical steam/sparks
            AureusFrameType fType = (AureusFrameType)(int)npc.localAI[0];
            if (fType == AureusFrameType.Walk && Main.rand.NextBool(4))
            {
                Vector2 legPos = npc.Bottom + new Vector2(Main.rand.NextFloat(-80f, 80f), -10f);
                Dust d = Dust.NewDustPerfect(legPos, DustID.Electric, new Vector2(0f, -2f), 80, Color.DeepSkyBlue, 0.8f);
                d.noGravity = true;
            }

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
        }

        /// <summary>
        /// Draws the singularity boundary circle, pulsing event horizon, and orbital sentinels.
        /// </summary>
        private void DrawSingularityArena(SpriteBatch spriteBatch)
        {
            int segments = 120;
            float step = TwoPi / segments;
            Color boundaryColor = Color.Lerp(Color.Cyan, Color.Purple, 0.5f + 0.5f * (float)Math.Sin(ticksRunning * 0.05f)) * singularityAlpha * 0.6f;

            // Draw circular boundary segments
            Vector2 lastPos = singularityCenter + new Vector2(ArenaRadius * arenaPulseScale, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = step * i;
                Vector2 currentPos = singularityCenter + angle.ToRotationVector2() * ArenaRadius * arenaPulseScale;
                DrawLine(spriteBatch, lastPos, currentPos, boundaryColor, 4f);
                lastPos = currentPos;
            }

            // Draw orbital probe indicators on boundary
            for (int i = 0; i < orbitalProbes.Count; i++)
            {
                OrbitalProbe probe = orbitalProbes[i];
                Vector2 probePos = singularityCenter + probe.Angle.ToRotationVector2() * ArenaRadius * arenaPulseScale;
                Color probeColor = Color.Magenta * singularityAlpha;

                // Draw orbital sentinel square shape
                spriteBatch.Draw(TextureAssets.BlackTile.Value, 
                    probePos - Main.screenPosition - new Vector2(8f, 8f), 
                    new Rectangle(0, 0, 1, 1), 
                    probeColor, 
                    probe.Angle + ticksRunning * 0.05f, 
                    Vector2.Zero, 
                    new Vector2(16f, 16f), 
                    SpriteEffects.None, 
                    0f);
            }
        }

        /// <summary>
        /// Standard line drawing utility utilizing BlackTile scaling.
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
        /// Spawns a ring of perfect dust particles around a center.
        /// </summary>
        private static void SpawnDustPerfectRing(Vector2 center, float radius, int dustType, int count, float speed)
        {
            float step = TwoPi / count;
            for (int i = 0; i < count; i++)
            {
                float angle = step * i;
                Vector2 offset = angle.ToRotationVector2() * radius;
                Dust d = Dust.NewDustPerfect(center + offset, dustType, angle.ToRotationVector2() * speed);
                d.noGravity = true;
            }
        }

        /// <summary>
        /// Formats and broadcasts alerts to the game chat interface.
        /// </summary>
        private static void BroadcastAlert(string text)
        {
            if (Main.netMode == NetmodeID.Server)
            {
                Terraria.Chat.ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(text), new Color(175, 75, 255));
            }
            else if (Main.netMode == NetmodeID.SinglePlayer)
            {
                Main.NewText(text, new Color(175, 75, 255));
            }
        }
        #endregion
    }
}
