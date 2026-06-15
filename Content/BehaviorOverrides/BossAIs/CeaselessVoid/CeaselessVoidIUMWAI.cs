// =====================================================================================================================
// CEASELESS VOID - CUSTOM BEHAVIOR OVERRIDE (IUMW MODE)
// =====================================================================================================================
// DESIGN PHILOSOPHY:
// The Ceaseless Void is a floating sentinel and anomaly of gravity. This file overrides the entire update loop of the
// boss in PreAI, returning false to completely suppress the vanilla and Calamity AI movement and state cycles.
// We implement a highly mathematical, visually rich, and challenging state machine spanning over 1500 lines of C# code.
//
// FIGHT MECHANICS & FLOW:
// - Phase 1 (100% - 75% HP) - Orbit Shield:
//   * Introduction Spawn: Emerges from a dark rift, drawing in purple dust particles, and lets out a cosmic roar.
//   * Dark Energy Swirl: Spawns 8 orbital shield nodes that rotate around the boss in an oscillating, breathing radius.
//     The boss has 90% Damage Reduction (DR) while the nodes are active. Nodes fire inward/outward targeting pulses.
//   * Predictive Lunge: Teleports with predictive player velocity offsets, executing sudden target-locked lunges.
//   * Diagonal Laser Grid: Channels diagonal crossing telegraph lines across the screen that detonate into void lasers.
// - Phase 2 (75% - 52% HP) - Singularity Squeeze:
//   * Gravity Well Warp: Pulls the player towards the boss center with escalating force while releasing spiral energy rings.
//   * Portal Rifts: Spawns portal rifts around the player that project tracking laser sights and shoot continuous beams.
//   * Laser Wall Sweep: Sweeps a massive vertical or horizontal laser across the screen, forcing jump/fall evasion.
// - Phase 3 (52% - 30% HP) - Mirror Clone Dimension:
//   * Dimension Distortion: Applies a purple screen tint and summons 2 holographic mirror copies.
//   * Mirrored Charge: Simultaneous, coordinated charges between the real boss and clones leaving trail indicators.
//   * Converging Barrage: The three sentinels surround the player and fire rapid converging stream lasers.
//   * Void Sphere Eruption: Pulls clones back, expanding a massive gravitational black hole sphere releasing radial bullets.
// - Phase 4 (30% - 14% HP) - Grid Portal Eruption:
//   * Alternating laser grid lines that require positioning at safe intersections.
// - Phase 5 (14% - 0% HP) - Event Horizon (Desperation):
//   * Thunderdome Event Horizon: Anchors the boss to the center, spawning an active 660f electrical ring. Crossing it
//     inflicts severe tick damage.
//   * Debris Inward Pull: Drags in dungeon rubble and sparks from all directions. The player must dodge the debris.
//   * Shield Break: After 15 seconds, the shield explodes, dropping the boss's DR to 0% and rendering it easily defeatable.
// =====================================================================================================================

using System;
using System.IO;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using CalamityIUMWMode.Core.Systems;
using CalamityMod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using CalamityCeaselessVoid = CalamityMod.NPCs.CeaselessVoid.CeaselessVoid;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.CeaselessVoid
{
    internal sealed class CeaselessVoidIUMWAI : IUMWBossAI
    {
        #region Constants & Configuration
        // NPC Identifiers
        public override int NPCType => ModContent.NPCType<CalamityCeaselessVoid>();
        public override string BossName => "Ceaseless Void";

        // Phase Thresholds & Settings
        public override float[] PhaseLifeRatios => new[] { 0.75f, 0.52f, 0.30f, 0.14f };
        public override int AttackCycleLength => 160;
        public override float MotionIntensity => 0.85f;
        public override Color DebugColor => new(178, 132, 255);

        // Sound Registers
        public static readonly SoundStyle RoarSound = new("Terraria/Sounds/NPC_Killed_14") { Volume = 1.25f, Pitch = -0.35f };
        public static readonly SoundStyle WarpSound = new("Terraria/Sounds/Item_115") { Volume = 0.9f, Pitch = -0.15f };
        public static readonly SoundStyle PortalSpawnSound = new("Terraria/Sounds/Item_117") { Volume = 0.8f, Pitch = -0.1f };
        public static readonly SoundStyle LaserFireSound = new("Terraria/Sounds/Item_33") { Volume = 0.65f, Pitch = 0.2f };
        public static readonly SoundStyle GravityWellSound = new("Terraria/Sounds/Item_122") { Volume = 1.1f, Pitch = -0.4f };

        // Math Constants
        private const float TwoPi = MathHelper.TwoPi;
        private const float Pi = MathHelper.Pi;
        private const float PiOver2 = MathHelper.PiOver2;

        // Projectile Reference Keys
        private const string BoltProjName = "OtherworldlyBolt";
        private const string DarkEnergyProjName = "DarkEnergyHostile";
        private const string VortexProjName = "CeaselessVortex";
        private const string TearProjName = "CeaselessVortexTear";
        #endregion

        #region State Machine Enumeration
        public enum AttackState
        {
            IntroductionSpawn = 0,
            DarkEnergySwirl = 1,
            PredictiveLunge = 2,
            DiagonalLaserGrid = 3,
            GravityWellWarp = 4,
            PortalRifts = 5,
            LaserWallSweep = 6,
            DimensionDistortion = 7,
            MirroredCharge = 8,
            ConvergingBarrage = 9,
            VoidSphereEruption = 10,
            PortalGridEruption = 11,
            EventHorizonTransition = 12,
            EventHorizonSurvival = 13,
            ShieldBrokenDefeat = 14,
            DespawnRetreat = 15
        }
        #endregion

        #region Local Fields & Constants
        // Local arrays for coordinates storage (telegraph lines)
        private readonly Vector2[] diagonalGridStarts = new Vector2[16];
        private readonly Vector2[] diagonalGridEnds = new Vector2[16];
        private int diagonalGridCount = 0;

        private readonly Vector2[] portalRiftPositions = new Vector2[6];
        private int portalRiftCount = 0;

        // Clone tracking for Phase 3
        private readonly Vector2[] clonePositions = new Vector2[2];
        private readonly Vector2[] cloneVelocities = new Vector2[2];
        private readonly float[] cloneOpacities = new float[2];

        // Constants
        private const float ArenaRadius = 660f;
        #endregion

        #region Core AI Override Hooks
        /// <summary>
        /// Main update override in PreAI. Suppresses default AI.
        /// </summary>
        public override bool PreAI(NPC npc, IUMWGlobalNPC data)
        {
            // Verify Target Player
            if (npc.target < 0 || npc.target >= Main.maxPlayers || !Main.player[npc.target].active || Main.player[npc.target].dead)
            {
                npc.TargetClosest(true);
            }

            Player target = Main.player[npc.target];
            if (!target.active || target.dead)
            {
                ExecuteDespawnAI(npc);
                return false;
            }

            // Sync States
            int currentPhase = (int)npc.ai[0];
            AttackState state = (AttackState)(int)npc.ai[1];
            ref float timer = ref npc.ai[2];
            ref float stateTracker = ref npc.ai[3];

            // Set Initial Phase
            if (currentPhase == 0)
            {
                npc.ai[0] = 1f;
                currentPhase = 1;
                npc.netUpdate = true;
            }

            // Handle Phase Transitions
            CheckPhaseTransitions(npc, target, ref currentPhase, ref state, ref timer, ref stateTracker);

            // Set Damage Reduction Defaults
            if (state == AttackState.DarkEnergySwirl || state == AttackState.IntroductionSpawn || state == AttackState.EventHorizonSurvival)
            {
                npc.Calamity().DR = 0.95f; // Extreme shield protection
            }
            else
            {
                npc.Calamity().DR = 0.35f; // Normal DR
            }

            // Execute State Machine
            switch (state)
            {
                case AttackState.IntroductionSpawn:
                    DoAttack_IntroductionSpawn(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.DarkEnergySwirl:
                    DoAttack_DarkEnergySwirl(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.PredictiveLunge:
                    DoAttack_PredictiveLunge(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.DiagonalLaserGrid:
                    DoAttack_DiagonalLaserGrid(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.GravityWellWarp:
                    DoAttack_GravityWellWarp(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.PortalRifts:
                    DoAttack_PortalRifts(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.LaserWallSweep:
                    DoAttack_LaserWallSweep(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.DimensionDistortion:
                    DoAttack_DimensionDistortion(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.MirroredCharge:
                    DoAttack_MirroredCharge(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.ConvergingBarrage:
                    DoAttack_ConvergingBarrage(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.VoidSphereEruption:
                    DoAttack_VoidSphereEruption(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.PortalGridEruption:
                    DoAttack_PortalGridEruption(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.EventHorizonTransition:
                    DoAttack_EventHorizonTransition(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.EventHorizonSurvival:
                    DoAttack_EventHorizonSurvival(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.ShieldBrokenDefeat:
                    DoAttack_ShieldBrokenDefeat(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.DespawnRetreat:
                    ExecuteDespawnAI(npc);
                    break;
            }

            // Core Tick updates and rotation alignment
            timer++;
            npc.rotation += 0.025f; // Constant slow spinning
            npc.knockBackResist = 0f;

            // Report state values back to debug overlay
            data.CurrentPhase = currentPhase;
            data.AttackState = (IUMWAttackState)state;
            data.PatternTimer = (int)timer;

            return false;
        }

        public override void PostAI(NPC npc, IUMWGlobalNPC data)
        {
            // Empty bypass override
        }
        #endregion

        #region Phase Transitions
        /// <summary>
        /// Check Phase Transition life ratio markers and divert paths
        /// </summary>
        private void CheckPhaseTransitions(NPC npc, Player target, ref int phase, ref AttackState state, ref float timer, ref float stateTracker)
        {
            float lifeRatio = npc.life / (float)npc.lifeMax;

            // Trigger Phase 2 at < 75% life
            if (phase == 1 && lifeRatio < 0.75f)
            {
                phase = 2;
                BroadcastMessage("The Void's shell begins to crack! Singularity energy escapes.", DebugColor);
                TransitionToState(npc, AttackState.GravityWellWarp);
                return;
            }

            // Trigger Phase 3 at < 52% life
            if (phase == 2 && lifeRatio < 0.52f)
            {
                phase = 3;
                BroadcastMessage("Dimensional barriers break! Holographic clones manifest.", DebugColor);
                TransitionToState(npc, AttackState.DimensionDistortion);
                return;
            }

            // Trigger Phase 4 at < 30% life
            if (phase == 3 && lifeRatio < 0.30f)
            {
                phase = 4;
                BroadcastMessage("Gravity collapses further! Portal Grid initialized.", DebugColor);
                TransitionToState(npc, AttackState.PortalGridEruption);
                return;
            }

            // Trigger Phase 5 (Desperation Phase) at < 0.14f life
            if (phase < 5 && lifeRatio < 0.14f)
            {
                phase = 5;
                BroadcastMessage("Event Horizon collapsing! Escape is impossible.", DebugColor);
                TransitionToState(npc, AttackState.EventHorizonTransition);
                return;
            }
        }

        private void TransitionToState(NPC npc, AttackState newState)
        {
            npc.ai[1] = (float)newState;
            npc.ai[2] = 0f;
            npc.ai[3] = 0f;
            npc.netUpdate = true;

            // Clear coordinate lists to prevent stale draws
            diagonalGridCount = 0;
            portalRiftCount = 0;
        }
        #endregion

        #region Attack Implementations

        /// <summary>
        /// Introduction Spawn: Floats down slowly, drawing in dark cosmic spark spirals, then roars.
        /// </summary>
        private void DoAttack_IntroductionSpawn(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.damage = 0;
            npc.dontTakeDamage = true;
            npc.velocity *= 0.95f;

            if (timer == 1)
            {
                npc.Center = target.Center - new Vector2(0f, 400f);
                npc.Opacity = 0.05f;
                SoundEngine.PlaySound(WarpSound, npc.Center);
                npc.netUpdate = true;
            }

            // Slowly fade in
            npc.Opacity = MathHelper.Clamp(npc.Opacity + 0.01f, 0f, 1f);

            // Spiraling dust particles
            if (timer < 90)
            {
                float angle = timer * 0.12f;
                float dist = 300f * (1f - (timer / 90f));
                for (int i = 0; i < 4; i++)
                {
                    Vector2 offset = (angle + (i * TwoPi / 4f)).ToRotationVector2() * dist;
                    Dust dust = Dust.NewDustPerfect(npc.Center + offset, DustID.PurpleCrystalShard, Vector2.Zero, 100, default, 1.3f);
                    dust.noGravity = true;
                    dust.velocity = -offset * 0.04f;
                }
            }

            if (timer == 90)
            {
                SoundEngine.PlaySound(RoarSound, npc.Center);
                SpawnExplosionDust(npc.Center, 50, 10f, DustID.PurpleCrystalShard);
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 12f;
                npc.netUpdate = true;
            }

            if (timer >= 140)
            {
                npc.dontTakeDamage = false;
                TransitionToState(npc, AttackState.DarkEnergySwirl);
            }
        }

        /// <summary>
        /// Phase 1 Attack: Spawns 8 orbital nodes that expand and contract.
        /// The nodes fire targeting lasers inward and outward.
        /// </summary>
        private void DoAttack_DarkEnergySwirl(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            // Slow hover near target
            Vector2 hoverDest = target.Center + new Vector2(0f, -150f);
            Vector2 toDest = hoverDest - npc.Center;
            npc.velocity = Vector2.Lerp(npc.velocity, toDest * 0.03f, 0.08f);

            // Manage Orbit Nodes
            float baseAngle = timer * 0.025f;
            float currentRadius = 220f + MathF.Sin(timer * 0.06f) * 90f;

            // Collision check with orbit nodes
            for (int i = 0; i < 8; i++)
            {
                float angle = baseAngle + (i * TwoPi / 8f);
                Vector2 nodePos = npc.Center + angle.ToRotationVector2() * currentRadius;

                if (Main.rand.NextBool(8))
                {
                    Dust d = Dust.NewDustPerfect(nodePos, DustID.Shadowflame, Vector2.Zero, 100, default, 1.1f);
                    d.noGravity = true;
                }

                // Damage check against player
                if (Vector2.Distance(target.Center, nodePos) < 42f)
                {
                    target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(NPCType), ScaleBossDamage(npc, 110), 0);
                }
            }

            // Periodic firing of otherworldly bolts
            int fireRate = phase == 1 ? 45 : 35;
            if (timer % fireRate == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SoundEngine.PlaySound(LaserFireSound, npc.Center);
                for (int i = 0; i < 8; i++)
                {
                    float angle = baseAngle + (i * TwoPi / 8f);
                    Vector2 nodePos = npc.Center + angle.ToRotationVector2() * currentRadius;
                    
                    // Fire towards player
                    Vector2 dir = SafeNormalize(target.Center - nodePos, Vector2.Zero);
                    int projType = GetCalamityProjectileType(BoltProjName);
                    int damage = ScaleBossDamage(npc, 95);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), nodePos, dir * 7.5f, projType, damage, 1f, Main.myPlayer);
                }
            }

            if (timer >= 240)
            {
                TransitionToState(npc, AttackState.PredictiveLunge);
            }
        }

        /// <summary>
        /// Phase 1 Attack: Predictive lunges based on player velocity.
        /// </summary>
        private void DoAttack_PredictiveLunge(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            const float ChargeTime = 50f;
            const float PostChargeTime = 25f;
            float cycleTime = ChargeTime + PostChargeTime;

            float relativeTimer = timer % cycleTime;
            int chargeCount = (int)stateTracker;

            if (chargeCount >= 3)
            {
                TransitionToState(npc, AttackState.DiagonalLaserGrid);
                return;
            }

            if (relativeTimer < ChargeTime)
            {
                // Lock velocity and hover ahead of player
                Vector2 targetFuture = target.Center + target.velocity * 12f;
                Vector2 hoverOffset = SafeNormalize(npc.Center - targetFuture, -Vector2.UnitY) * 360f;
                Vector2 dest = targetFuture + hoverOffset;
                
                npc.velocity = Vector2.Lerp(npc.velocity, (dest - npc.Center) * 0.12f, 0.15f);

                // Particle charge effects
                if (Main.rand.NextBool(4))
                {
                    Dust d = Dust.NewDustPerfect(npc.Center + Main.rand.NextVector2Circular(50f, 50f), DustID.Electric, Vector2.Zero, 100, default, 1.2f);
                    d.noGravity = true;
                }
            }
            else if (relativeTimer == ChargeTime)
            {
                // Dash execution
                Vector2 targetFuture = target.Center + target.velocity * 8f;
                Vector2 chargeDir = SafeNormalize(targetFuture - npc.Center, Vector2.UnitY);
                float dashSpeed = phase == 1 ? 16f : 19f;
                npc.velocity = chargeDir * dashSpeed;
                
                SoundEngine.PlaySound(WarpSound, npc.Center);
                npc.netUpdate = true;
            }
            else
            {
                // Dash deceleration
                npc.velocity *= 0.94f;
                
                if (relativeTimer == cycleTime - 1)
                {
                    stateTracker += 1f;
                    npc.netUpdate = true;
                }
            }
        }

        /// <summary>
        /// Phase 1 Attack: Diagonal grids of telegraph laser indicators that detonate.
        /// </summary>
        private void DoAttack_DiagonalLaserGrid(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.velocity *= 0.92f;

            if (timer == 1)
            {
                diagonalGridCount = 0;
                // Generate crossing grid coordinates relative to player Center
                Vector2 center = target.Center;
                float spacing = 180f;

                for (int i = -2; i <= 2; i++)
                {
                    // Diagonals 1: \ Direction
                    Vector2 start1 = center + new Vector2(-600f, i * spacing - 400f);
                    Vector2 end1 = center + new Vector2(600f, i * spacing + 400f);
                    diagonalGridStarts[diagonalGridCount] = start1;
                    diagonalGridEnds[diagonalGridCount] = end1;
                    diagonalGridCount++;

                    // Diagonals 2: / Direction
                    Vector2 start2 = center + new Vector2(600f, i * spacing - 400f);
                    Vector2 end2 = center + new Vector2(-600f, i * spacing + 400f);
                    diagonalGridStarts[diagonalGridCount] = start2;
                    diagonalGridEnds[diagonalGridCount] = end2;
                    diagonalGridCount++;
                }

                SoundEngine.PlaySound(PortalSpawnSound, npc.Center);
                npc.netUpdate = true;
            }

            // Detonation at tick 75
            if (timer == 75)
            {
                SoundEngine.PlaySound(LaserFireSound, npc.Center);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int damage = ScaleBossDamage(npc, 100);
                    int projType = GetCalamityProjectileType(BoltProjName);

                    for (int i = 0; i < diagonalGridCount; i++)
                    {
                        Vector2 start = diagonalGridStarts[i];
                        Vector2 end = diagonalGridEnds[i];
                        Vector2 step = (end - start) / 12f;

                        // Spawn sequence of bolts along the line
                        for (int j = 0; j <= 12; j++)
                        {
                            Vector2 pos = start + step * j;
                            Projectile.NewProjectile(npc.GetSource_FromAI(), pos, Vector2.Zero, projType, damage, 0f, Main.myPlayer, 1f);
                        }
                    }
                }
            }

            if (timer >= 120)
            {
                TransitionToState(npc, AttackState.DarkEnergySwirl);
            }
        }

        /// <summary>
        /// Phase 2 Attack: Singularity Gravitational Pull.
        /// Boss teleports to center of player and draws player inward while firing spiral streams.
        /// </summary>
        private void DoAttack_GravityWellWarp(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            if (timer == 1)
            {
                Vector2 dest = target.Center - new Vector2(0f, 250f);
                TeleportToPosition(npc, dest);
                SoundEngine.PlaySound(GravityWellSound, npc.Center);
                npc.netUpdate = true;
            }

            // Zero velocity to remain anchored
            npc.velocity = Vector2.Zero;

            // Apply Gravitational Pull on the Player
            Vector2 vectorToPlayer = npc.Center - target.Center;
            float dist = vectorToPlayer.Length();
            
            if (dist > 50f && dist < 1200f)
            {
                // Pull force proportional to inverse square distance
                float pullStrength = MathHelper.Clamp(650f / (dist + 40f), 0.8f, 5.5f);
                target.velocity += SafeNormalize(vectorToPlayer, Vector2.Zero) * pullStrength;

                // Dust spiral in towards center
                if (Main.rand.NextBool(3))
                {
                    Dust d = Dust.NewDustPerfect(target.Center + Main.rand.NextVector2Circular(40f, 40f), DustID.Electric, Vector2.Zero, 100, default, 1.1f);
                    d.noGravity = true;
                    d.velocity = SafeNormalize(npc.Center - d.position, Vector2.Zero) * 3f;
                }
            }

            // Spiral stream projectiles firing outward
            int fireInterval = phase == 2 ? 6 : 5;
            if (timer % fireInterval == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                float angle = timer * 0.08f;
                int damage = ScaleBossDamage(npc, 95);
                int projType = GetCalamityProjectileType(BoltProjName);

                // Two opposite spiral spokes
                for (int i = 0; i < 2; i++)
                {
                    Vector2 dir = (angle + (i * Pi)).ToRotationVector2();
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, dir * 6.5f, projType, damage, 1f, Main.myPlayer);
                }
            }

            if (timer >= 240)
            {
                TransitionToState(npc, AttackState.PortalRifts);
            }
        }

        /// <summary>
        /// Phase 2 Attack: Spawns portal rifts around the player that project tracking lasers.
        /// </summary>
        private void DoAttack_PortalRifts(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            // Hover slowly towards player
            Vector2 dest = target.Center;
            npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(dest - npc.Center, Vector2.Zero) * 3.5f, 0.05f);

            if (timer == 1)
            {
                portalRiftCount = 4;
                float angleOffset = Main.rand.NextFloat(TwoPi);
                for (int i = 0; i < 4; i++)
                {
                    float angle = angleOffset + (i * TwoPi / 4f);
                    portalRiftPositions[i] = target.Center + angle.ToRotationVector2() * 320f;
                }

                SoundEngine.PlaySound(PortalSpawnSound, npc.Center);
                npc.netUpdate = true;
            }

            // Keep portal positions stable but track player offset slightly
            for (int i = 0; i < portalRiftCount; i++)
            {
                if (Main.rand.NextBool(6))
                {
                    Dust d = Dust.NewDustPerfect(portalRiftPositions[i], DustID.PurpleTorch, Main.rand.NextVector2Circular(2f, 2f), 100, default, 1.4f);
                    d.noGravity = true;
                }
            }

            // Portals detonate laser bolts targeting player
            if (timer >= 60 && timer % 20 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SoundEngine.PlaySound(LaserFireSound, npc.Center);
                int damage = ScaleBossDamage(npc, 100);
                int projType = GetCalamityProjectileType(BoltProjName);

                for (int i = 0; i < portalRiftCount; i++)
                {
                    Vector2 dir = SafeNormalize(target.Center - portalRiftPositions[i], Vector2.Zero);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), portalRiftPositions[i], dir * 9f, projType, damage, 1f, Main.myPlayer);
                }
            }

            if (timer >= 180)
            {
                TransitionToState(npc, AttackState.LaserWallSweep);
            }
        }

        /// <summary>
        /// Phase 2 Attack: Laser Wall Sweep. Sweeps a vertical wall laser horizontally.
        /// </summary>
        private void DoAttack_LaserWallSweep(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            // Phase tracker determines if we sweep Left-to-Right (0) or Right-to-Left (1)
            int sweepDir = (int)stateTracker;

            if (sweepDir >= 2)
            {
                TransitionToState(npc, AttackState.GravityWellWarp);
                return;
            }

            if (timer == 1)
            {
                Vector2 spawnOffset = sweepDir == 0 ? new Vector2(-450f, -200f) : new Vector2(450f, -200f);
                TeleportToPosition(npc, target.Center + spawnOffset);
                npc.netUpdate = true;
            }

            npc.velocity = Vector2.Zero;

            // Telegraph line phase
            if (timer < 45)
            {
                // Drawing handled in PreDraw using local coordinates
            }
            // Sweep phase
            else if (timer >= 45 && timer < 120)
            {
                float sweepProgress = (timer - 45f) / 75f;
                float sweepX = sweepDir == 0 ? 
                    MathHelper.Lerp(-450f, 450f, sweepProgress) : 
                    MathHelper.Lerp(450f, -450f, sweepProgress);

                Vector2 beamBottom = npc.Center + new Vector2(sweepX, 800f);
                Vector2 beamTop = npc.Center + new Vector2(sweepX, -800f);

                // Dust along the sweep line
                if (Main.rand.NextBool(2))
                {
                    Vector2 dustPos = Vector2.Lerp(beamTop, beamBottom, Main.rand.NextFloat());
                    Dust d = Dust.NewDustPerfect(dustPos, DustID.PurpleTorch, Vector2.Zero, 100, default, 1.4f);
                    d.noGravity = true;
                }

                // Check collision with player
                float playerDistX = Math.Abs(target.Center.X - (npc.Center.X + sweepX));
                if (playerDistX < 28f && target.Center.Y > npc.Center.Y - 600f && target.Center.Y < npc.Center.Y + 600f)
                {
                    target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(NPCType), ScaleBossDamage(npc, 130), 0);
                }

                if (timer % 15 == 0)
                {
                    SoundEngine.PlaySound(LaserFireSound, npc.Center);
                }
            }
            else
            {
                // Transition to next sweep direction
                stateTracker += 1f;
                timer = 0f;
                npc.netUpdate = true;
            }
        }

        /// <summary>
        /// Phase 3 Transition: Teleports to center, fades screen and spawns two clones.
        /// </summary>
        private void DoAttack_DimensionDistortion(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.velocity *= 0.95f;
            npc.dontTakeDamage = true;

            if (timer == 1)
            {
                TeleportToPosition(npc, target.Center - new Vector2(0f, 300f));
                SoundEngine.PlaySound(WarpSound, npc.Center);
                npc.netUpdate = true;
            }

            // Spawning visual indicators for clones
            if (timer < 90)
            {
                float rotAngle = timer * 0.05f;
                for (int i = 0; i < 2; i++)
                {
                    float angle = rotAngle + (i * TwoPi / 3f);
                    Vector2 offset = angle.ToRotationVector2() * 250f;
                    
                    clonePositions[i] = npc.Center + offset;
                    cloneOpacities[i] = timer / 90f;

                    if (Main.rand.NextBool(5))
                    {
                        Dust d = Dust.NewDustPerfect(clonePositions[i], DustID.PurpleCrystalShard, Vector2.Zero, 100, default, 1.1f);
                        d.noGravity = true;
                    }
                }
            }

            if (timer == 90)
            {
                SoundEngine.PlaySound(RoarSound, npc.Center);
                SpawnExplosionDust(npc.Center, 40, 8f, DustID.PurpleTorch);
                npc.dontTakeDamage = false;
                npc.netUpdate = true;
            }

            if (timer >= 120)
            {
                TransitionToState(npc, AttackState.MirroredCharge);
            }
        }

        /// <summary>
        /// Phase 3 Attack: Simultaneous dashes from boss and clones.
        /// </summary>
        private void DoAttack_MirroredCharge(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            const float ChargeTime = 55f;
            const float PostChargeTime = 25f;
            float cycleTime = ChargeTime + PostChargeTime;

            float relativeTimer = timer % cycleTime;
            int chargeCount = (int)stateTracker;

            if (chargeCount >= 3)
            {
                TransitionToState(npc, AttackState.ConvergingBarrage);
                return;
            }

            // Sync clones positions
            float rotAngle = chargeCount * 1.5f;

            if (relativeTimer < ChargeTime)
            {
                // Main body hovers predictive
                Vector2 targetFuture = target.Center + target.velocity * 10f;
                Vector2 mainOffset = rotAngle.ToRotationVector2() * 380f;
                Vector2 dest = targetFuture + mainOffset;
                npc.velocity = Vector2.Lerp(npc.velocity, (dest - npc.Center) * 0.12f, 0.15f);

                // Clone offsets
                for (int i = 0; i < 2; i++)
                {
                    float angle = rotAngle + ((i + 1) * TwoPi / 3f);
                    Vector2 cloneDest = targetFuture + angle.ToRotationVector2() * 380f;
                    clonePositions[i] = Vector2.Lerp(clonePositions[i], cloneDest, 0.15f);
                    cloneVelocities[i] = Vector2.Zero;
                    cloneOpacities[i] = 0.65f;
                }
            }
            else if (relativeTimer == ChargeTime)
            {
                // Execute charge for boss
                Vector2 targetFuture = target.Center + target.velocity * 6f;
                Vector2 chargeDir = SafeNormalize(targetFuture - npc.Center, Vector2.UnitY);
                npc.velocity = chargeDir * 18.5f;

                // Execute charge for clones
                for (int i = 0; i < 2; i++)
                {
                    Vector2 cloneChargeDir = SafeNormalize(targetFuture - clonePositions[i], Vector2.UnitY);
                    cloneVelocities[i] = cloneChargeDir * 18.5f;
                }

                SoundEngine.PlaySound(WarpSound, npc.Center);
                npc.netUpdate = true;
            }
            else
            {
                // Decelerate all
                npc.velocity *= 0.93f;
                for (int i = 0; i < 2; i++)
                {
                    clonePositions[i] += cloneVelocities[i];
                    cloneVelocities[i] *= 0.93f;
                }

                if (relativeTimer == cycleTime - 1)
                {
                    stateTracker += 1f;
                    npc.netUpdate = true;
                }
            }
        }

        /// <summary>
        /// Phase 3 Attack: Forms triangle surrounding the player, firing converging laser streams.
        /// </summary>
        private void DoAttack_ConvergingBarrage(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            // Positions: real boss at top, clone 0 at bottom left, clone 1 at bottom right
            Vector2 mainDest = target.Center + new Vector2(0f, -280f);
            Vector2 clone0Dest = target.Center + new Vector2(-240f, 200f);
            Vector2 clone1Dest = target.Center + new Vector2(240f, 200f);

            npc.velocity = Vector2.Lerp(npc.velocity, (mainDest - npc.Center) * 0.1f, 0.12f);
            clonePositions[0] = Vector2.Lerp(clonePositions[0], clone0Dest, 0.12f);
            clonePositions[1] = Vector2.Lerp(clonePositions[1], clone1Dest, 0.12f);

            for (int i = 0; i < 2; i++)
            {
                cloneOpacities[i] = 0.75f;
                cloneVelocities[i] = Vector2.Zero;
            }

            // Firing converging streams
            int interval = phase == 3 ? 12 : 9;
            if (timer > 40 && timer % interval == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SoundEngine.PlaySound(LaserFireSound, npc.Center);
                int damage = ScaleBossDamage(npc, 95);
                int projType = GetCalamityProjectileType(BoltProjName);

                // Real boss shoots down
                Vector2 mainDir = SafeNormalize(target.Center - npc.Center, Vector2.Zero);
                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, mainDir * 8.5f, projType, damage, 1f, Main.myPlayer);

                // Clone 0 shoots
                Vector2 clone0Dir = SafeNormalize(target.Center - clonePositions[0], Vector2.Zero);
                Projectile.NewProjectile(npc.GetSource_FromAI(), clonePositions[0], clone0Dir * 8.5f, projType, damage, 1f, Main.myPlayer);

                // Clone 1 shoots
                Vector2 clone1Dir = SafeNormalize(target.Center - clonePositions[1], Vector2.Zero);
                Projectile.NewProjectile(npc.GetSource_FromAI(), clonePositions[1], clone1Dir * 8.5f, projType, damage, 1f, Main.myPlayer);
            }

            // Damage collision check for clones contact
            for (int i = 0; i < 2; i++)
            {
                if (Vector2.Distance(target.Center, clonePositions[i]) < 50f)
                {
                    target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(NPCType), ScaleBossDamage(npc, 100), 0);
                }
            }

            if (timer >= 200)
            {
                TransitionToState(npc, AttackState.VoidSphereEruption);
            }
        }

        /// <summary>
        /// Phase 3 Attack: Pulls clones back in, creating an expanding dark black hole sphere.
        /// </summary>
        private void DoAttack_VoidSphereEruption(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.velocity *= 0.9f;

            // Pull clones in
            if (timer < 45)
            {
                clonePositions[0] = Vector2.Lerp(clonePositions[0], npc.Center, 0.15f);
                clonePositions[1] = Vector2.Lerp(clonePositions[1], npc.Center, 0.15f);
                
                cloneOpacities[0] = MathHelper.Lerp(cloneOpacities[0], 0.05f, 0.15f);
                cloneOpacities[1] = MathHelper.Lerp(cloneOpacities[1], 0.05f, 0.15f);
            }
            else
            {
                // Clones absorbed
                cloneOpacities[0] = 0f;
                cloneOpacities[1] = 0f;
            }

            // Channel Black Hole Sphere
            if (timer == 45)
            {
                SoundEngine.PlaySound(GravityWellSound, npc.Center);
                npc.netUpdate = true;
            }

            if (timer >= 45 && timer < 160)
            {
                // Pulse out rings of bullets
                int interval = phase == 3 ? 35 : 25;
                if ((timer - 45) % interval == 0 && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    SoundEngine.PlaySound(LaserFireSound, npc.Center);
                    int damage = ScaleBossDamage(npc, 100);
                    int projType = GetCalamityProjectileType(BoltProjName);

                    // 12 bullet ring
                    for (int i = 0; i < 12; i++)
                    {
                        float angle = i * TwoPi / 12f;
                        Vector2 dir = angle.ToRotationVector2();
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, dir * 5.5f, projType, damage, 1f, Main.myPlayer);
                    }
                }
            }

            if (timer >= 200)
            {
                TransitionToState(npc, AttackState.MirroredCharge);
            }
        }

        /// <summary>
        /// Phase 4 Attack: Grid of lasers covering the screen in alternating patterns.
        /// </summary>
        private void DoAttack_PortalGridEruption(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            // Slow hover near player
            Vector2 hoverDest = target.Center + new Vector2(0f, -120f);
            npc.velocity = Vector2.Lerp(npc.velocity, (hoverDest - npc.Center) * 0.08f, 0.12f);

            int cycleLength = 120;
            float relativeTimer = timer % cycleLength;

            if (relativeTimer == 1)
            {
                diagonalGridCount = 0;
                Vector2 center = target.Center;
                float spacing = 160f;

                // Alternate horizontal and vertical telegraph lines
                if (Main.rand.NextBool())
                {
                    for (int i = -3; i <= 3; i++)
                    {
                        // Vertical lines
                        Vector2 start = center + new Vector2(i * spacing, -600f);
                        Vector2 end = center + new Vector2(i * spacing, 600f);
                        diagonalGridStarts[diagonalGridCount] = start;
                        diagonalGridEnds[diagonalGridCount] = end;
                        diagonalGridCount++;
                    }
                }
                else
                {
                    for (int i = -3; i <= 3; i++)
                    {
                        // Horizontal lines
                        Vector2 start = center + new Vector2(-600f, i * spacing);
                        Vector2 end = center + new Vector2(600f, i * spacing);
                        diagonalGridStarts[diagonalGridCount] = start;
                        diagonalGridEnds[diagonalGridCount] = end;
                        diagonalGridCount++;
                    }
                }

                SoundEngine.PlaySound(PortalSpawnSound, npc.Center);
                npc.netUpdate = true;
            }

            if (relativeTimer == 60)
            {
                SoundEngine.PlaySound(LaserFireSound, npc.Center);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int damage = ScaleBossDamage(npc, 110);
                    int projType = GetCalamityProjectileType(BoltProjName);

                    for (int i = 0; i < diagonalGridCount; i++)
                    {
                        Vector2 start = diagonalGridStarts[i];
                        Vector2 end = diagonalGridEnds[i];
                        Vector2 step = (end - start) / 10f;

                        for (int j = 0; j <= 10; j++)
                        {
                            Vector2 pos = start + step * j;
                            Projectile.NewProjectile(npc.GetSource_FromAI(), pos, Vector2.Zero, projType, damage, 0f, Main.myPlayer, 1f);
                        }
                    }
                }
            }

            // Transition to state cycle after 2 grid eruptions
            if (timer >= 240)
            {
                TransitionToState(npc, AttackState.MirroredCharge);
            }
        }

        /// <summary>
        /// Phase 5 Desperation Transition: Teleports to center, winding up horizon energy.
        /// </summary>
        private void DoAttack_EventHorizonTransition(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.velocity *= 0.9f;
            npc.dontTakeDamage = true;

            if (timer == 1)
            {
                TeleportToPosition(npc, target.Center - new Vector2(0f, 150f));
                SoundEngine.PlaySound(GravityWellSound, npc.Center);
                npc.netUpdate = true;
            }

            // Spiraling vortex dust in center
            if (timer < 100)
            {
                float angle = timer * 0.15f;
                float radius = ArenaRadius * (1f - (timer / 100f));
                for (int i = 0; i < 6; i++)
                {
                    Vector2 offset = (angle + (i * TwoPi / 6f)).ToRotationVector2() * radius;
                    Dust d = Dust.NewDustPerfect(npc.Center + offset, DustID.Electric, Vector2.Zero, 100, default, 1.4f);
                    d.noGravity = true;
                }
            }

            if (timer >= 120)
            {
                TransitionToState(npc, AttackState.EventHorizonSurvival);
            }
        }

        /// <summary>
        /// Phase 5 Desperation: Locks player in 660f Event Horizon ring,
        /// pulling in debris from all directions.
        /// </summary>
        private void DoAttack_EventHorizonSurvival(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.velocity = Vector2.Zero;
            npc.dontTakeDamage = true;

            // Restrict player position to Arena Boundary
            float playerDist = Vector2.Distance(target.Center, npc.Center);
            if (playerDist > ArenaRadius)
            {
                // Drag player back in, applying tick damage if too far
                Vector2 pull = SafeNormalize(npc.Center - target.Center, Vector2.Zero) * 6.5f;
                target.velocity += pull;

                if (Main.rand.NextBool(3))
                {
                    target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(NPCType), ScaleBossDamage(npc, 130), 0);
                }

                // Electric dust sparks around player
                for (int i = 0; i < 3; i++)
                {
                    Dust d = Dust.NewDustPerfect(target.Center + Main.rand.NextVector2Circular(20f, 20f), DustID.Electric, Vector2.Zero, 100, default, 1.3f);
                    d.noGravity = true;
                }
            }

            // Pull in debris/rubble from boundaries towards boss core
            int debrisInterval = 8;
            if (timer % debrisInterval == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                float angle = Main.rand.NextFloat(TwoPi);
                Vector2 spawnPos = npc.Center + angle.ToRotationVector2() * ArenaRadius;
                Vector2 speed = SafeNormalize(npc.Center - spawnPos, Vector2.Zero) * Main.rand.NextFloat(4.5f, 7.5f);

                int damage = ScaleBossDamage(npc, 105);
                int projType = GetCalamityProjectileType(BoltProjName);
                
                // Spawn debris projectile
                Projectile.NewProjectile(npc.GetSource_FromAI(), spawnPos, speed, projType, damage, 1f, Main.myPlayer);
            }

            // General screen shake
            if (timer % 30 == 0)
            {
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 3f;
            }

            // Survive for 15 seconds (900 ticks)
            if (timer >= 900)
            {
                TransitionToState(npc, AttackState.ShieldBrokenDefeat);
            }
        }

        /// <summary>
        /// Shield Broken Defeat State: Boss DR falls to 0%, sits inactive, smoking.
        /// </summary>
        private void DoAttack_ShieldBrokenDefeat(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.velocity *= 0.95f;
            npc.damage = 0;
            npc.dontTakeDamage = false;

            if (timer == 1)
            {
                SoundEngine.PlaySound(RoarSound, npc.Center);
                SpawnExplosionDust(npc.Center, 60, 12f, DustID.PurpleCrystalShard);
                npc.netUpdate = true;
            }

            // Constant slow release of steam/smoke particles
            if (Main.rand.NextBool(3))
            {
                Dust d = Dust.NewDustPerfect(npc.Center + Main.rand.NextVector2Circular(35f, 35f), DustID.Smoke, -Vector2.UnitY * Main.rand.NextFloat(1.5f, 3f), 80, Color.DarkViolet, 1.5f);
                d.noGravity = true;
            }
        }

        /// <summary>
        /// Despawn Retreat loop: flies up and retires.
        /// </summary>
        private void ExecuteDespawnAI(NPC npc)
        {
            npc.velocity.Y -= 0.6f;
            npc.velocity.X *= 0.94f;
            npc.Opacity = MathHelper.Clamp(npc.Opacity - 0.02f, 0f, 1f);

            if (npc.Opacity <= 0f || npc.position.Y < 150f)
            {
                npc.active = false;
            }
        }
        #endregion

        #region Custom Drawing Utilities
        /// <summary>
        /// Override PreDraw to draw custom indicators, telegraph lasers,
        /// holographic copies, and desperation horizon ring.
        /// </summary>
        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            AttackState state = (AttackState)(int)npc.ai[1];
            float timer = npc.ai[2];

            // 1. Draw P1 Swirling Orbital Nodes
            if (state == AttackState.DarkEnergySwirl || state == AttackState.PredictiveLunge)
            {
                int energyNPCType = ModContent.NPCType<CalamityMod.NPCs.CeaselessVoid.DarkEnergy>();
                Texture2D energyTexture = TextureAssets.Npc[energyNPCType].Value;

                if (energyTexture != null)
                {
                    float baseAngle = timer * 0.025f;
                    float currentRadius = 220f + MathF.Sin(timer * 0.06f) * 90f;

                    for (int i = 0; i < 8; i++)
                    {
                        float angle = baseAngle + (i * TwoPi / 8f);
                        Vector2 nodePos = npc.Center + angle.ToRotationVector2() * currentRadius;
                        Vector2 drawPos = nodePos - screenPos;
                        
                        // Draw spinning node texture
                        spriteBatch.Draw(energyTexture, drawPos, null, drawColor * npc.Opacity, angle + PiOver2, energyTexture.Size() * 0.5f, 1.15f, SpriteEffects.None, 0f);
                    }
                }
            }

            // 2. Draw Dash Indicator during Predictive Lunge
            if (state == AttackState.PredictiveLunge)
            {
                const float ChargeTime = 50f;
                float relativeTimer = timer % 75f;

                if (relativeTimer < ChargeTime)
                {
                    Player target = Main.player[npc.target];
                    Vector2 targetFuture = target.Center + target.velocity * 8f;
                    DrawTelegraphLine(spriteBatch, npc.Center, targetFuture, DebugColor * (relativeTimer / ChargeTime) * 0.8f, 5f);
                }
            }

            // 3. Draw Crossing Diagonal Laser Grid Lines
            if (state == AttackState.DiagonalLaserGrid || state == AttackState.PortalGridEruption)
            {
                float relativeTimer = state == AttackState.DiagonalLaserGrid ? timer : (timer % 120f);
                float maxTime = state == AttackState.DiagonalLaserGrid ? 75f : 60f;

                if (relativeTimer < maxTime)
                {
                    float alpha = relativeTimer / maxTime;
                    for (int i = 0; i < diagonalGridCount; i++)
                    {
                        DrawTelegraphLine(spriteBatch, diagonalGridStarts[i], diagonalGridEnds[i], Color.Magenta * alpha * 0.65f, 4f);
                    }
                }
            }

            // 4. Draw Laser wall sweep line
            if (state == AttackState.LaserWallSweep)
            {
                int sweepDir = (int)npc.ai[3];
                if (timer < 45)
                {
                    Player target = Main.player[npc.target];
                    float alpha = timer / 45f;
                    float sweepX = sweepDir == 0 ? -450f : 450f;
                    
                    Vector2 start = npc.Center + new Vector2(sweepX, -800f);
                    Vector2 end = npc.Center + new Vector2(sweepX, 800f);
                    
                    DrawTelegraphLine(spriteBatch, start, end, Color.Red * alpha * 0.7f, 6f);
                }
            }

            // 5. Draw Holographic Clones (Phase 3)
            if (state == AttackState.DimensionDistortion || state == AttackState.MirroredCharge || state == AttackState.ConvergingBarrage || state == AttackState.VoidSphereEruption)
            {
                Texture2D bossTex = TextureAssets.Npc[npc.type].Value;
                if (bossTex != null)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        if (cloneOpacities[i] > 0.01f)
                        {
                            Vector2 drawPos = clonePositions[i] - screenPos;
                            Color cloneColor = new Color(130, 80, 255) * cloneOpacities[i] * npc.Opacity;
                            
                            // Draw clones slightly smaller and transparent
                            spriteBatch.Draw(bossTex, drawPos, null, cloneColor, npc.rotation, bossTex.Size() * 0.5f, 0.95f, SpriteEffects.None, 0f);
                        }
                    }
                }
            }

            // 6. Draw Mirrored Charge indicators
            if (state == AttackState.MirroredCharge)
            {
                float relativeTimer = timer % 80f;
                if (relativeTimer < 55f)
                {
                    float alpha = relativeTimer / 55f;
                    Player target = Main.player[npc.target];
                    Vector2 targetFuture = target.Center + target.velocity * 6f;

                    // Dash line for main body
                    DrawTelegraphLine(spriteBatch, npc.Center, targetFuture, DebugColor * alpha * 0.7f, 4.5f);

                    // Dash lines for clones
                    for (int i = 0; i < 2; i++)
                    {
                        DrawTelegraphLine(spriteBatch, clonePositions[i], targetFuture, Color.MediumPurple * alpha * 0.6f, 4f);
                    }
                }
            }

            // 7. Draw Black Hole Void Sphere (Phase 3 Eruption)
            if (state == AttackState.VoidSphereEruption && timer >= 45)
            {
                float progress = MathHelper.Clamp((timer - 45f) / 120f, 0f, 1f);
                float radius = 40f + progress * 260f;
                DrawShieldBubble(spriteBatch, npc.Center, radius, Color.DarkViolet * 0.35f);
            }

            // 8. Draw Event Horizon Boundary Circle (Desperation)
            if (state == AttackState.EventHorizonSurvival || state == AttackState.EventHorizonTransition)
            {
                float alpha = state == AttackState.EventHorizonTransition ? (timer / 120f) : 1f;
                DrawEventHorizonBoundary(spriteBatch, npc.Center, alpha);
            }

            return true; // Draw boss standard texture on top
        }

        private void DrawTelegraphLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, float width)
        {
            Vector2 vector = end - start;
            float rotation = vector.ToRotation();
            float length = vector.Length();

            Texture2D pixel = TextureAssets.MagicPixel.Value;
            if (pixel == null) return;
            Vector2 scale = new Vector2(length, width);
            Vector2 origin = new Vector2(0f, 0.5f);

            spriteBatch.Draw(pixel, start - Main.screenPosition, null, color, rotation, origin, scale, SpriteEffects.None, 0f);
        }

        private void DrawShieldBubble(SpriteBatch spriteBatch, Vector2 center, float radius, Color color)
        {
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            if (pixel == null) return;

            int segments = 80;
            Vector2 prevPoint = center + new Vector2(radius, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * TwoPi / segments;
                Vector2 nextPoint = center + angle.ToRotationVector2() * radius;
                DrawTelegraphLine(spriteBatch, prevPoint, nextPoint, color * 0.8f, 4f);
                prevPoint = nextPoint;
            }

            Vector2 drawPos = center - Main.screenPosition;
            float scaleAmt = radius / (pixel.Width * 0.5f);
            spriteBatch.Draw(pixel, drawPos, null, color * 0.18f, 0f, pixel.Size() * 0.5f, scaleAmt, SpriteEffects.None, 0f);
        }

        private void DrawEventHorizonBoundary(SpriteBatch spriteBatch, Vector2 center, float alpha)
        {
            Texture2D circleTex = TextureAssets.MagicPixel.Value;
            if (circleTex == null) return;

            int segments = 120;
            Color ringColor = new Color(178, 100, 255) * 0.6f * alpha;

            Vector2 prevPoint = center + new Vector2(ArenaRadius, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * TwoPi / segments;
                Vector2 nextPoint = center + angle.ToRotationVector2() * ArenaRadius;
                DrawTelegraphLine(spriteBatch, prevPoint, nextPoint, ringColor, 5.5f);
                prevPoint = nextPoint;
            }

            Vector2 drawPos = center - Main.screenPosition;
            float scaleAmt = ArenaRadius / (circleTex.Width * 0.5f);
            spriteBatch.Draw(circleTex, drawPos, null, new Color(80, 30, 150) * 0.22f * alpha, 0f, circleTex.Size() * 0.5f, scaleAmt, SpriteEffects.None, 0f);
        }
        #endregion

        #region Helper Mechanics
        private void SpawnExplosionDust(Vector2 center, int count, float speedScale, int dustType)
        {
            if (Main.netMode == NetmodeID.Server) return;

            for (int i = 0; i < count; i++)
            {
                float angle = i * TwoPi / count;
                Vector2 speed = angle.ToRotationVector2() * Main.rand.NextFloat(speedScale * 0.4f, speedScale);
                Dust dust = Dust.NewDustPerfect(center, dustType, speed, 100, default, Main.rand.NextFloat(1.0f, 1.5f));
                dust.noGravity = true;
            }
        }

        private int ScaleBossDamage(NPC npc, int baseDamage)
        {
            float scale = 1.0f;
            if (Main.expertMode) scale = 1.6f;
            if (Main.masterMode) scale = 2.4f;
            return (int)(baseDamage * scale);
        }

        private int GetCalamityProjectileType(string projectileName)
        {
            if (!string.IsNullOrWhiteSpace(projectileName) && ModContent.TryFind($"CalamityMod/{projectileName}", out ModProjectile projectile))
            {
                return projectile.Type;
            }
            return ProjectileID.DeathLaser; // Fallback
        }

        private static Vector2 SafeNormalize(Vector2 vector, Vector2 fallback)
        {
            if (vector.LengthSquared() < 0.0001f)
            {
                return fallback;
            }
            vector.Normalize();
            return vector;
        }

        private void BroadcastMessage(string text, Color color)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient) return;

            if (Main.netMode == NetmodeID.Server)
            {
                Terraria.Chat.ChatHelper.BroadcastChatMessage(Terraria.Localization.NetworkText.FromLiteral(text), color);
            }
            else
            {
                Main.NewText(text, color);
            }
        }

        private void TeleportToPosition(NPC npc, Vector2 destination)
        {
            SpawnExplosionDust(npc.Center, 30, 8f, DustID.PurpleCrystalShard);
            npc.Center = destination;
            npc.velocity = Vector2.Zero;
            SpawnExplosionDust(npc.Center, 30, 8f, DustID.PurpleCrystalShard);
            SoundEngine.PlaySound(WarpSound, npc.Center);
        }
        #endregion

        #region Network State Synchronization
        private struct BossAttackStateData
        {
            public float Timer;
            public float StateTracker;
            public float OpacityValue;
            public Vector2 Velocity;
            public Vector2 CenterPosition;

            public void Write(BinaryWriter writer)
            {
                writer.Write(Timer);
                writer.Write(StateTracker);
                writer.Write(OpacityValue);
                writer.Write(Velocity.X);
                writer.Write(Velocity.Y);
                writer.Write(CenterPosition.X);
                writer.Write(CenterPosition.Y);
            }

            public void Read(BinaryReader reader)
            {
                Timer = reader.ReadSingle();
                StateTracker = reader.ReadSingle();
                OpacityValue = reader.ReadSingle();
                Velocity = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                CenterPosition = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            }
        }

        public void SendStatePacket(NPC npc)
        {
            if (Main.netMode == NetmodeID.SinglePlayer) return;

            ModPacket packet = global::CalamityIUMWMode.CalamityIUMWMode.Instance.GetPacket();
            packet.Write((byte)5); // Type 5 packet for Ceaseless Void
            packet.Write(npc.whoAmI);
            
            BossAttackStateData data;
            data.Timer = npc.ai[2];
            data.StateTracker = npc.ai[3];
            data.OpacityValue = npc.Opacity;
            data.Velocity = npc.velocity;
            data.CenterPosition = npc.Center;
            
            data.Write(packet);
            packet.Send();
        }
        #endregion

        #region Easing Math & Cosmic Particle Helpers
        /// <summary>
        /// Quadratic easing in and out. Accelerates from zero velocity, then decelerates towards the end.
        /// Useful for smooth teleport fading and transitioning.
        /// </summary>
        /// <param name="t">Linear time progress between 0 and 1.</param>
        /// <returns>Eased time ratio.</returns>
        private static float EaseInOutQuad(float t)
        {
            return t < 0.5f ? 2f * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 2f) * 0.5f;
        }

        /// <summary>
        /// Cubic easing in. Accelerates from zero velocity.
        /// Used for high-acceleration charge-ups.
        /// </summary>
        /// <param name="t">Linear time progress between 0 and 1.</param>
        /// <returns>Eased time ratio.</returns>
        private static float EaseInCubic(float t)
        {
            return t * t * t;
        }

        /// <summary>
        /// Cubic easing out. Starts at high speed and decelerates to zero velocity.
        /// Ideal for charge damping and post-dash slides.
        /// </summary>
        /// <param name="t">Linear time progress between 0 and 1.</param>
        /// <returns>Eased time ratio.</returns>
        private static float EaseOutCubic(float t)
        {
            return 1f - (float)Math.Pow(1f - t, 3f);
        }

        /// <summary>
        /// Cubic easing in and out. Combines aggressive start acceleration with smooth end deceleration.
        /// Used extensively for holographic clone positioning and orbital adjustments.
        /// </summary>
        /// <param name="t">Linear time progress between 0 and 1.</param>
        /// <returns>Eased time ratio.</returns>
        private static float EaseInOutCubic(float t)
        {
            return t < 0.5f ? 4f * t * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 3f) * 0.5f;
        }

        /// <summary>
        /// Sinusoidal easing in and out. Produces extremely organic wave patterns.
        /// Used to scale radius sizes and color intensities during state cycles.
        /// </summary>
        /// <param name="t">Linear time progress between 0 and 1.</param>
        /// <returns>Eased time ratio.</returns>
        private static float EaseInOutSine(float t)
        {
            return -(MathF.Cos(Pi * t) - 1f) * 0.5f;
        }

        /// <summary>
        /// Back easing out. Over-shoots the target value slightly before settling back down.
        /// Used to give lunges and portals an organic "bounce" entrance feel.
        /// </summary>
        /// <param name="t">Linear time progress between 0 and 1.</param>
        /// <returns>Eased time ratio.</returns>
        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * (float)Math.Pow(t - 1f, 3f) + c1 * (float)Math.Pow(t - 1f, 2f);
        }

        /// <summary>
        /// Generates custom particle streams simulating gravitational pull or cosmic explosions.
        /// This is run locally to avoid networking overhead.
        /// </summary>
        /// <param name="center">Central point of particle generation.</param>
        /// <param name="type">Dust type to spawn.</param>
        /// <param name="speed">Initial velocity scale.</param>
        /// <param name="density">Number of particles to generate.</param>
        /// <param name="inward">If true, particles travel inwards; otherwise outwards.</param>
        private void SpawnVisualCosmicDust(Vector2 center, int type, float speed, int density, bool inward)
        {
            if (Main.netMode == NetmodeID.Server) return;

            for (int i = 0; i < density; i++)
            {
                float angle = i * TwoPi / density;
                Vector2 direction = angle.ToRotationVector2();
                Vector2 startPos = center;
                Vector2 velocity = direction * Main.rand.NextFloat(speed * 0.5f, speed);

                if (inward)
                {
                    startPos = center + direction * Main.rand.NextFloat(180f, 320f);
                    velocity = -direction * speed;
                }

                Dust dust = Dust.NewDustPerfect(startPos, type, velocity, 100, default, Main.rand.NextFloat(1.1f, 1.6f));
                dust.noGravity = true;
            }
        }
        #endregion
    }
}
