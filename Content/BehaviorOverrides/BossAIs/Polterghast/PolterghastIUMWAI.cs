// =====================================================================================================================
// POLTERGHAST - CUSTOM BEHAVIOR OVERRIDE (IUMW MODE)
// =====================================================================================================================
// DESIGN PHILOSOPHY:
// Polterghast (噬魂幽花) is a restless amalgamation of souls trapped in the Temple dungeon walls, bound together by
// intense spiritual grief and phantoplastic energy.
// In IUMW Mode, Polterghast commands the dead, projecting coordinated duplicate illusions (Phantom splits) and weaving
// complex grids of crossing spectral lasers. To represent its spiritual grasp without requiring new asset files,
// this override mathematically models and renders 4 spectral ectoplasmic tentacles directly in PreDraw.
// These tentacles swing, swipe, and shoot projectiles towards the player dynamically based on active state timers.
//
// FIGHT MECHANICS & FLOW:
// - Phase 1 (100% - 70% HP) - Ghostly Agitation:
//   * Spawn Animation: Materializes slowly, letting out phantoplastic roar rings.
//   * Ectoplasm Uppercut Charges: Teleports below target, projects vertical warning lines, and dashes straight upwards.
//   * Soul Harvester Wisps: Releases circles of expanding/contracting wisps.
// - Phase 2 (70% - 40% HP) - Spectral Divide:
//   * Spectral Tentacle Swipes: 4 Bézier-drawn tentacles lash out at the player. Tips of the tentacles shoot ectoplasm darts.
//   * Phantom Clone Split: Spawns 2 PolterPhantom duplicate copies that execute predictive charges.
//   * Ghostly Vortex Charge: Dashes at high speed, leaving trails of gravitational vortexes that pull player.
// - Phase 3 (40% - 18% HP) - Catacomb Mourning:
//   * Asgore Convergence Rings: Teleports to random sides, spawning rings of souls that close inward with a dodging gap.
//   * Grid Laser Cleansing: Projects crossing vertical and horizontal telegraph lines that detonate into spectral beams.
// - Phase 4 (18% - 0% HP) - Necropolis Singularity (Desperation):
//   * Necropolis Arena: Traps the player inside a 660f ghost circle. Leaving it causes rapid tick damage.
//   * Phantom Overdrive: Circles the boundary at extreme speed, releasing waves of arcing souls.
//   * Spectral Laser Barrage: Sequences of crossing warning lines that detonate into large soul bursts.
//   * Bespoke Death Animation: Explodes segment-by-segment with green/cyan glow particles.
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
using CalamityMod.NPCs.Polterghast;
using CalamityMod.Projectiles.Boss;
using CalamityMod.World;
using CalamityMod.Events;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using CalamityIUMWMode.Core.Systems;

using CalamityPolterghast = CalamityMod.NPCs.Polterghast.Polterghast;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Polterghast
{
    internal sealed class PolterghastIUMWAI : IUMWBossAI
    {
        #region Constants & Configuration
        // NPC Identifiers
        public override int NPCType => ModContent.NPCType<CalamityPolterghast>();
        public override string BossName => "Polterghast";

        // Phase Thresholds & Settings
        public override float[] PhaseLifeRatios => new[] { 0.70f, 0.40f, 0.18f };
        public override int AttackCycleLength => 125;
        public override float MotionIntensity => 1.05f;
        public override Color DebugColor => new(116, 215, 255);

        // Sound Registers (Direct Calamity Custom Sounds)
        public static readonly SoundStyle SpawnSound = CalamityPolterghast.SpawnSound;
        public static readonly SoundStyle P2Sound = CalamityPolterghast.P2Sound;
        public static readonly SoundStyle P3Sound = CalamityPolterghast.P3Sound;
        public static readonly SoundStyle PhantomSound = CalamityPolterghast.PhantomSound;

        // Custom Sound Registrations
        public static readonly SoundStyle LaserSound = new("Terraria/Sounds/Item_125") { Volume = 0.85f, Pitch = -0.1f };
        public static readonly SoundStyle DashSound = new("Terraria/Sounds/NPC_Killed_14") { Volume = 1.15f, Pitch = -0.3f };
        public static readonly SoundStyle SoulReleaseSound = new("Terraria/Sounds/NPC_Hit_36") { Volume = 0.9f, Pitch = 0.2f };

        // Math Constants
        private const float TwoPi = MathHelper.TwoPi;
        private const float Pi = MathHelper.Pi;
        private const float PiOver2 = MathHelper.PiOver2;
        private const float ArenaRadius = 660f;

        // Projectile Reference Keys (Resolved dynamically at runtime)
        private const string PhantomShotProjName = "PhantomShot";
        private const string PhantomShot2ProjName = "PhantomShot2";
        private const string PhantomBlastProjName = "PhantomBlast";
        private const string PhantomBlast2ProjName = "PhantomBlast2";
        private const string PhantomMineProjName = "PhantomMine";
        private const string GhostVortexProjName = "OldDukeVortex"; // Use Vortex as Ghostly Vortex
        private const string PhantomGhostShotProjName = "PhantomGhostShot";
        #endregion

        #region State Machine Enumeration
        public enum AttackState
        {
            SpawnAnimation = 0,
            AttackSelectionWait = 1,
            EctoplasmUppercutCharges = 2,
            WispCircleCharges = 3,
            AsgoreRingSoulAttack = 4,
            ArcingSouls = 5,
            VortexCharge = 6,
            SpiritPetal = 7,
            CloneSplit = 8,
            DesperationArena = 9,
            NecropolisOverdrive = 10,
            SpectralLaserBarrage = 11,
            DeathAnimation = 12,
            DespawnRetreat = 13
        }

        public enum FrameType
        {
            Phase1Idle = 0,
            Phase2Charge = 1,
            Phase3Glow = 2
        }
        #endregion

        #region Local Fields
        // Radii and alphas for drawing indicators
        private float auraDrawAlpha = 0f;
        private float desperationVignetteAlpha = 0f;
        private float desperationVignetteRadius = 0f;

        // Laser telegraph grid positions
        private readonly List<Vector2> laserGridStartPoints = new();
        private readonly List<Vector2> laserGridEndPoints = new();
        private int laserGridCount = 0;


        // Custom drawn spectral tentacles (Bezier splines)
        private readonly Vector2[] tentacleTargets = new Vector2[4];
        private readonly float[] tentacleAngles = new float[4];
        private readonly float[] tentacleLengths = new float[4];
        private bool tentaclesInitialized = false;

        // Desperation coordinates
        private Vector2 desperationCenter = Vector2.Zero;
        private int desperationTimer = 0;
        #endregion

        #region Core AI Override Hooks
        /// <summary>
        /// Main update override in PreAI. Suppresses default AI by returning false.
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

            // Grant infinite flight to target players while fighting
            foreach (Player player in Main.ActivePlayers)
            {
                if (player.dead || player.ghost || !npc.WithinRange(player.Center, 8000f))
                    continue;

                player.breath = player.breathMax;
                player.ignoreWater = true;
                player.wingTime = player.wingTimeMax;
                player.Calamity().infiniteFlight = true;
            }

            // Initialize tentacle targets
            if (!tentaclesInitialized)
            {
                for (int i = 0; i < 4; i++)
                {
                    tentacleTargets[i] = npc.Center;
                    tentacleAngles[i] = (i / 4f) * TwoPi;
                    tentacleLengths[i] = 180f;
                }
                tentaclesInitialized = true;
            }

            // Sync States from npc.ai
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

            // Intercept health for desperation trigger
            if (currentPhase < 4 && npc.life <= npc.lifeMax * PhaseLifeRatios[2])
            {
                npc.ai[0] = 4f;
                currentPhase = 4;
                TransitionToState(npc, AttackState.DesperationArena);
                state = AttackState.DesperationArena;
                desperationCenter = target.Center;
                desperationTimer = 900; // 15 seconds survival
                CleanupStrayEntities();
                SoundEngine.PlaySound(P3Sound, npc.Center);
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 20f;
            }

            // Handle Phase Transitions (1, 2, 3)
            if (currentPhase < 4)
            {
                CheckPhaseTransitions(npc, target, ref currentPhase, ref state, ref timer, ref stateTracker);
            }

            // Update local visuals & tentacles
            UpdateLocalVisuals(npc, state, timer);

            // Manage Damage Reduction (DR)
            ManageDR(npc, state, currentPhase);

            // Execute State Machine
            switch (state)
            {
                case AttackState.SpawnAnimation:
                    DoAttack_SpawnAnimation(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.AttackSelectionWait:
                    DoAttack_AttackSelectionWait(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.EctoplasmUppercutCharges:
                    DoAttack_EctoplasmUppercutCharges(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.WispCircleCharges:
                    DoAttack_WispCircleCharges(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.AsgoreRingSoulAttack:
                    DoAttack_AsgoreRingSoulAttack(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.ArcingSouls:
                    DoAttack_ArcingSouls(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.VortexCharge:
                    DoAttack_VortexCharge(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.SpiritPetal:
                    DoAttack_SpiritPetal(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.CloneSplit:
                    DoAttack_CloneSplit(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.DesperationArena:
                    DoAttack_DesperationArena(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.NecropolisOverdrive:
                    DoAttack_NecropolisOverdrive(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.SpectralLaserBarrage:
                    DoAttack_SpectralLaserBarrage(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.DeathAnimation:
                    DoAttack_DeathAnimation(npc, ref timer);
                    break;
                case AttackState.DespawnRetreat:
                    DoAttack_DespawnRetreat(npc, ref timer);
                    break;
            }

            timer++;
            return false;
        }

        public override void PostAI(NPC npc, IUMWGlobalNPC data)
        {
            // Suppress base post-AI update behavior
        }
        #endregion

        #region Attack Execution Methods

        /// <summary>
        /// Initial spawn animation. Slowly fades in while releasing spectral shockwaves.
        /// </summary>
        private void DoAttack_SpawnAnimation(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.damage = 0;
            npc.dontTakeDamage = true;
            npc.localAI[0] = (float)FrameType.Phase1Idle;
            npc.frameCounter++;

            if (timer < 60f)
            {
                // Hover slowly above spawn point
                npc.velocity = new Vector2(0f, -1.5f);
                npc.Opacity = Lerp(0f, 1f, timer / 60f);

                // Spawn purple/blue portal dust
                if (timer % 4f == 0f)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        Vector2 offset = Main.rand.NextVector2Circular(90f, 90f);
                        Dust d = Dust.NewDustPerfect(npc.Center + offset, DustID.PinkCrystalShard, new Vector2(0f, -2f));
                        d.noGravity = true;
                        d.scale = Main.rand.NextFloat(1.1f, 1.6f);
                    }
                }
            }
            else if (timer < 110f)
            {
                npc.velocity *= 0.94f;
                npc.rotation = npc.rotation.AngleLerp(npc.AngleTo(target.Center), 0.08f);

                if (timer == 80f)
                {
                    SoundEngine.PlaySound(SpawnSound, npc.Center);
                    Main.LocalPlayer.Calamity().GeneralScreenShakePower = 15f;

                    // Expand ring of spectral particles
                    for (int i = 0; i < 48; i++)
                    {
                        float angle = (i / 48f) * TwoPi;
                        Vector2 vel = angle.ToRotationVector2() * 9f;
                        Dust d = Dust.NewDustPerfect(npc.Center, DustID.PinkCrystalShard, vel);
                        d.scale = 2.2f;
                        d.noGravity = true;
                    }
                }
            }
            else
            {
                npc.dontTakeDamage = false;
                TransitionToState(npc, AttackState.AttackSelectionWait);
            }
        }

        /// <summary>
        /// Hover selection state between attacks.
        /// </summary>
        private void DoAttack_AttackSelectionWait(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = 0;
            npc.localAI[0] = (float)FrameType.Phase1Idle;
            npc.frameCounter++;

            // Face target
            npc.spriteDirection = (target.Center.X < npc.Center.X) ? 1 : -1;

            // Hover on alternate sides
            float side = (stateTracker == 0) ? -480f : 480f;
            Vector2 hoverDest = target.Center + new Vector2(side, -240f);
            Vector2 toDest = hoverDest - npc.Center;
            float dist = toDest.Length();

            if (dist > 45f)
            {
                float speed = Lerp(12f, 26f, dist / 900f);
                npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(toDest, -Vector2.UnitY) * speed, 0.09f);
            }
            else
            {
                npc.velocity *= 0.90f;
            }

            // Rotate to look at player
            float targetRot = npc.AngleTo(target.Center) + PiOver2;
            npc.rotation = npc.rotation.AngleLerp(targetRot, 0.12f);

            float selectDuration = 45f;
            if (phase == 2) selectDuration = 35f;
            if (phase == 3) selectDuration = 25f;

            if (timer >= selectDuration)
            {
                AttackState nextAttack = SelectNextState(phase, ref stateTracker);
                TransitionToState(npc, nextAttack);
            }
        }

        /// <summary>
        /// Teleports beneath the player, projects warning lines, and uppercut dashes straight upwards.
        /// </summary>
        private void DoAttack_EctoplasmUppercutCharges(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.localAI[0] = (float)FrameType.Phase2Charge;

            const float ChargeTime = 50f;
            const float UppercutHeight = 700f;

            if (timer == 1f)
            {
                npc.damage = 0;
                npc.dontTakeDamage = true;
                npc.Opacity = 0f;

                // Teleport directly beneath target with X variance
                float xOffset = Main.rand.NextFloat(-200f, 200f);
                npc.Center = target.Center + new Vector2(xOffset, UppercutHeight);
                npc.velocity = Vector2.Zero;
                npc.rotation = 0f;

                SoundEngine.PlaySound(PhantomSound, npc.Center);
                npc.netUpdate = true;
            }

            // Telegraph phase
            if (timer < 30f)
            {
                npc.damage = 0;
                npc.dontTakeDamage = true;
                npc.Opacity = 0f;
                // Stay locked beneath target's current X coordinate
                npc.Center = new Vector2(npc.Center.X, target.Center.Y + UppercutHeight);
                return;
            }

            // Dash execution
            if (timer == 30f)
            {
                npc.damage = npc.defDamage;
                npc.dontTakeDamage = false;
                npc.Opacity = 1f;

                SoundEngine.PlaySound(DashSound, npc.Center);
                npc.velocity = new Vector2(0f, -32f); // High-speed vertical launch
                npc.netUpdate = true;
            }

            if (timer > 30f && timer < 30f + ChargeTime)
            {
                // Emit ectoplasm shot streams left and right
                if (timer % 5f == 0f && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int projType = GetCalamityProjectileType(PhantomShotProjName);
                    if (projType != ProjectileID.DeathLaser)
                    {
                        Vector2 leftVel = new Vector2(-10f, Main.rand.NextFloat(-3f, 3f));
                        Vector2 rightVel = new Vector2(10f, Main.rand.NextFloat(-3f, 3f));

                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, leftVel, projType, 210, 0f, Main.myPlayer);
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, rightVel, projType, 210, 0f, Main.myPlayer);
                    }
                }

                // Shake screen
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 3f;
            }

            if (timer >= 30f + ChargeTime)
            {
                npc.velocity *= 0.85f;
                TransitionToState(npc, AttackState.AttackSelectionWait);
            }
        }

        /// <summary>
        /// Creates a contracting/expanding circle of wisp mines around the player.
        /// </summary>
        private void DoAttack_WispCircleCharges(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = npc.defDamage;
            npc.localAI[0] = (float)FrameType.Phase1Idle;
            npc.frameCounter++;

            // Hover and orbit
            float orbitSpeed = 0.035f;
            float orbitRadius = 550f;
            float angle = timer * orbitSpeed;
            Vector2 hoverDest = target.Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * orbitRadius;
            
            npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(hoverDest - npc.Center, Vector2.UnitY) * 20f, 0.08f);
            npc.rotation = npc.AngleTo(target.Center) + PiOver2;

            if (timer == 20f)
            {
                SoundEngine.PlaySound(PhantomSound, npc.Center);
                
                // Spawn 10 wisp mines surrounding the player
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int mineType = GetCalamityProjectileType(PhantomMineProjName);
                    if (mineType != ProjectileID.DeathLaser)
                    {
                        const int wisps = 10;
                        for (int i = 0; i < wisps; i++)
                        {
                            float wispAngle = (i / (float)wisps) * TwoPi;
                            // Spawn circular perimeter
                            Vector2 spawnOffset = wispAngle.ToRotationVector2() * 450f;
                            Vector2 spawnPos = target.Center + spawnOffset;
                            // Move inward
                            Vector2 wispVel = -wispAngle.ToRotationVector2() * 3.5f;

                            Projectile.NewProjectile(npc.GetSource_FromAI(), spawnPos, wispVel, mineType, 200, 0f, Main.myPlayer);
                        }
                    }
                }
            }

            if (timer >= 100f)
            {
                TransitionToState(npc, AttackState.AttackSelectionWait);
            }
        }

        /// <summary>
        /// Undertale Asgore-style soul convergence rings. Forces weaving through gaps.
        /// </summary>
        private void DoAttack_AsgoreRingSoulAttack(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = 0; // Invulnerable contact
            npc.dontTakeDamage = true;
            npc.localAI[0] = (float)FrameType.Phase3Glow;
            npc.velocity *= 0.9f;

            const float CenterWaitTime = 60f;
            const float AttackDuration = 320f;

            if (timer == 1f)
            {
                // Teleport to player center offset
                npc.Center = target.Center + new Vector2((target.Center.X < npc.Center.X) ? 550f : -550f, -300f);
                npc.velocity = Vector2.Zero;
                SoundEngine.PlaySound(PhantomSound, npc.Center);
                npc.netUpdate = true;
            }

            // Face target
            npc.rotation = npc.AngleTo(target.Center) + PiOver2;

            // Release concentric rings of souls that contract towards the boss/center
            if (timer >= CenterWaitTime && (timer - CenterWaitTime) % 65f == 0f && timer < AttackDuration)
            {
                SoundEngine.PlaySound(SoulReleaseSound, npc.Center);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int soulType = GetCalamityProjectileType(PhantomShotProjName);
                    if (soulType != ProjectileID.DeathLaser)
                    {
                        const int Souls = 18;
                        float gapAngle = Main.rand.NextFloat(TwoPi);
                        float gapSpread = MathHelper.ToRadians(50f);

                        // Spawn circular ring of souls contracting inwards
                        for (int i = 0; i < Souls; i++)
                        {
                            float angle = (i / (float)Souls) * TwoPi;
                            // Skip a sector to create the safe gap
                            float delta = Math.Abs(MathHelper.WrapAngle(angle - gapAngle));
                            if (delta < gapSpread)
                                continue;

                            Vector2 spawnOffset = angle.ToRotationVector2() * 600f;
                            Vector2 spawnPos = target.Center + spawnOffset;
                            Vector2 vel = -angle.ToRotationVector2() * 5.5f;

                            Projectile.NewProjectile(npc.GetSource_FromAI(), spawnPos, vel, soulType, 220, 0f, Main.myPlayer);
                        }
                    }
                }
            }

            if (timer >= AttackDuration + 40f)
            {
                npc.dontTakeDamage = false;
                TransitionToState(npc, AttackState.AttackSelectionWait);
            }
        }

        /// <summary>
        /// Fires arcing spectral souls that curve in trajectory.
        /// </summary>
        private void DoAttack_ArcingSouls(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = npc.defDamage;
            npc.localAI[0] = (float)FrameType.Phase2Charge;
            
            // Orbit target
            float speed = 15f;
            Vector2 targetHover = target.Center + new Vector2((target.Center.X < npc.Center.X) ? -450f : 450f, -220f);
            npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(targetHover - npc.Center, Vector2.UnitY) * speed, 0.08f);
            npc.rotation = npc.AngleTo(target.Center) + PiOver2;

            if (timer > 20f && timer % 45f == 0f)
            {
                SoundEngine.PlaySound(SoulReleaseSound, npc.Center);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int soulType = GetCalamityProjectileType(PhantomGhostShotProjName);
                    if (soulType != ProjectileID.DeathLaser)
                    {
                        // Spits 4 arcing wisps in fan directions
                        for (int i = -2; i <= 2; i++)
                        {
                            if (i == 0) continue;
                            float angleOffset = i * 0.25f;
                            Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY).RotatedBy(angleOffset) * 11f;

                            // ai[0] stores curve angular velocity parameter in Calamity projectiles
                            Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, vel, soulType, 210, 0f, Main.myPlayer, i * 0.02f);
                        }
                    }
                }
            }

            if (timer >= 160f)
            {
                TransitionToState(npc, AttackState.AttackSelectionWait);
            }
        }

        /// <summary>
        /// Spawns a trail of gravitational vortexes that pull player.
        /// </summary>
        private void DoAttack_VortexCharge(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = npc.defDamage;
            npc.localAI[0] = (float)FrameType.Phase2Charge;

            const float ChargeSpeed = 34f;
            const float ChargeDuration = 35f;

            if (timer == 1f)
            {
                SoundEngine.PlaySound(DashSound, npc.Center);
                
                // Dash directly towards player
                Vector2 targetFuture = target.Center + target.velocity * 8f;
                npc.velocity = SafeNormalize(targetFuture - npc.Center, -Vector2.UnitY) * ChargeSpeed;
                npc.rotation = npc.velocity.ToRotation() + PiOver2;
                npc.netUpdate = true;
            }

            // Spawn gravitational vortexes in trail
            if (timer > 1f && timer % 8f == 0f)
            {
                SoundEngine.PlaySound(SoulReleaseSound, npc.Center);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int vortexType = GetCalamityProjectileType(GhostVortexProjName);
                    if (vortexType != ProjectileID.DeathLaser)
                    {
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, Vector2.Zero, vortexType, 230, 0f, Main.myPlayer);
                    }
                }
            }

            if (timer >= ChargeDuration)
            {
                npc.velocity *= 0.85f;
                TransitionToState(npc, AttackState.AttackSelectionWait);
            }
        }

        /// <summary>
        /// Fires spiral waves of petal-like souls while boss fades.
        /// </summary>
        private void DoAttack_SpiritPetal(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = 0;
            npc.localAI[0] = (float)FrameType.Phase3Glow;
            npc.velocity *= 0.9f;

            // Fade opacity
            if (timer < 45f)
            {
                npc.Opacity = Lerp(1f, 0.25f, timer / 45f);
            }

            // Keep hover offset
            Vector2 hoverDest = target.Center + new Vector2(0f, -320f);
            npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(hoverDest - npc.Center, Vector2.UnitY) * 8f, 0.08f);
            npc.rotation = npc.AngleTo(target.Center) + PiOver2;

            if (timer > 20f && timer % 10f == 0f && timer < 160f)
            {
                SoundEngine.PlaySound(SoulReleaseSound, npc.Center);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int soulType = GetCalamityProjectileType(PhantomShotProjName);
                    if (soulType != ProjectileID.DeathLaser)
                    {
                        // Double spirals (clockwise & counterclockwise)
                        float angleOffset = timer * 0.12f;
                        Vector2 baseDir = SafeNormalize(target.Center - npc.Center, Vector2.UnitY);
                        
                        Vector2 clockwiseVel = baseDir.RotatedBy(angleOffset) * 9f;
                        Vector2 counterClockwiseVel = baseDir.RotatedBy(-angleOffset) * 9f;

                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, clockwiseVel, soulType, 210, 0f, Main.myPlayer);
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, counterClockwiseVel, soulType, 210, 0f, Main.myPlayer);
                    }
                }
            }

            if (timer >= 180f)
            {
                npc.Opacity = 1f;
                TransitionToState(npc, AttackState.AttackSelectionWait);
            }
        }

        /// <summary>
        /// Summons 2 PolterPhantom duplicates. Coordinated dashes trigger.
        /// </summary>
        private void DoAttack_CloneSplit(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = 0;
            npc.localAI[0] = (float)FrameType.Phase3Glow;
            npc.velocity *= 0.9f;

            const float SetupTime = 40f;
            const float ChargeTime = 40f;

            if (timer == 1f)
            {
                SoundEngine.PlaySound(PhantomSound, npc.Center);
                
                // Spawn 2 duplicates (PolterPhantom NPC) positioned left & right
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int phantomType = ModContent.NPCType<PolterPhantom>();
                    
                    int p1 = NPC.NewNPC(npc.GetSource_FromAI(), (int)target.Center.X - 500, (int)target.Center.Y - 150, phantomType);
                    int p2 = NPC.NewNPC(npc.GetSource_FromAI(), (int)target.Center.X + 500, (int)target.Center.Y - 150, phantomType);

                    if (Main.npc.IndexInRange(p1))
                    {
                        Main.npc[p1].velocity = Vector2.Zero;
                        Main.npc[p1].netUpdate = true;
                    }
                    if (Main.npc.IndexInRange(p2))
                    {
                        Main.npc[p2].velocity = Vector2.Zero;
                        Main.npc[p2].netUpdate = true;
                    }
                }
                
                // Move real boss to top center
                npc.Center = target.Center + new Vector2(0f, -500f);
                npc.velocity = Vector2.Zero;
                npc.netUpdate = true;
            }

            // Face target
            npc.rotation = npc.AngleTo(target.Center) + PiOver2;

            // Coordinated dash execution
            if (timer == SetupTime)
            {
                SoundEngine.PlaySound(DashSound, npc.Center);
                npc.damage = npc.defDamage;
                npc.velocity = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 32f;
                npc.netUpdate = true;

                // Fire radial soul ring
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int soulType = GetCalamityProjectileType(PhantomShotProjName);
                    for (int i = 0; i < 12; i++)
                    {
                        float angle = (i / 12f) * TwoPi;
                        Vector2 vel = angle.ToRotationVector2() * 8f;
                        if (soulType != ProjectileID.DeathLaser)
                        {
                            Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, vel, soulType, 210, 0f, Main.myPlayer);
                        }
                    }
                }
            }

            if (timer >= SetupTime + ChargeTime)
            {
                npc.velocity *= 0.85f;
                // Clean up phantom duplicates
                CleanupStrayDuplicates();
                TransitionToState(npc, AttackState.AttackSelectionWait);
            }
        }

        /// <summary>
        /// Desperation Stage: Fades out, locks player inside 660f circle boundary.
        /// </summary>
        private void DoAttack_DesperationArena(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.damage = 0;
            npc.dontTakeDamage = true;
            npc.Opacity = 0f;
            npc.velocity *= 0.85f;

            desperationTimer--;
            if (desperationTimer <= 0)
            {
                TransitionToState(npc, AttackState.DeathAnimation);
                return;
            }

            // Keep locked to center coordinate
            npc.Center = desperationCenter;

            // Restrict target inside boundary
            ForcePlayerInsideArena(target);

            // Interpolate vignette drawing stats
            if (desperationVignetteAlpha < 0.99f)
            {
                desperationVignetteAlpha = Lerp(desperationVignetteAlpha, 1f, 0.05f);
                desperationVignetteRadius = Lerp(desperationVignetteRadius, ArenaRadius, 0.05f);
            }

            if (timer >= 60f)
            {
                TransitionToState(npc, AttackState.NecropolisOverdrive);
            }
        }

        /// <summary>
        /// Desperation Stage: High-speed dashes along boundary while firing rings.
        /// </summary>
        private void DoAttack_NecropolisOverdrive(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.damage = npc.defDamage;
            npc.dontTakeDamage = true;
            npc.Opacity = 0.8f; // Semi-translucent ghost form
            npc.localAI[0] = (float)FrameType.Phase2Charge;

            desperationTimer--;
            if (desperationTimer <= 0)
            {
                TransitionToState(npc, AttackState.DeathAnimation);
                return;
            }

            ForcePlayerInsideArena(target);

            // Fly along boundary orbit
            float orbitSpeed = 0.065f;
            float angle = timer * orbitSpeed;
            Vector2 orbitDest = desperationCenter + angle.ToRotationVector2() * ArenaRadius;
            
            npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(orbitDest - npc.Center, Vector2.UnitY) * 26f, 0.12f);
            npc.rotation = npc.velocity.ToRotation() + PiOver2;

            // Periodic boundary rushes inward
            if (timer % 60f == 0f)
            {
                SoundEngine.PlaySound(DashSound, npc.Center);
                npc.velocity = SafeNormalize(target.Center - npc.Center, Vector2.UnitX) * 48f;
                npc.rotation = npc.velocity.ToRotation() + PiOver2;
                
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int bubbleType = GetCalamityProjectileType(PhantomShotProjName);
                    for (int i = 0; i < 8; i++)
                    {
                        float bulletAngle = (i / 8f) * TwoPi;
                        Vector2 bulletVel = bulletAngle.ToRotationVector2() * 6.5f;
                        if (bubbleType != ProjectileID.DeathLaser)
                        {
                            Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, bulletVel, bubbleType, 210, 0f, Main.myPlayer);
                        }
                    }
                }
                npc.netUpdate = true;
            }

            if (desperationTimer <= 450)
            {
                TransitionToState(npc, AttackState.SpectralLaserBarrage);
            }
        }

        /// <summary>
        /// Desperation Stage: Projects sequence of crossing lasers.
        /// </summary>
        private void DoAttack_SpectralLaserBarrage(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.damage = 0;
            npc.dontTakeDamage = true;
            npc.Opacity = 0f; // Completely invisible
            npc.Center = desperationCenter;

            desperationTimer--;
            if (desperationTimer <= 0)
            {
                npc.life = 2; // Vulnerable
                TransitionToState(npc, AttackState.DeathAnimation);
                return;
            }

            ForcePlayerInsideArena(target);

            const float GridTelegraphTime = 45f;

            // Setup new laser warning paths
            if (timer % 65f == 0f)
            {
                SoundEngine.PlaySound(PhantomSound, target.Center);

                laserGridStartPoints.Clear();
                laserGridEndPoints.Clear();
                laserGridCount = 3;

                // Setup 3 random lines passing close to player
                for (int i = 0; i < laserGridCount; i++)
                {
                    float angle = Main.rand.NextFloat(TwoPi);
                    Vector2 lineDir = angle.ToRotationVector2();
                    Vector2 playerOffset = Main.rand.NextVector2Circular(200f, 200f);
                    
                    Vector2 start = target.Center + playerOffset - lineDir * 1200f;
                    Vector2 end = target.Center + playerOffset + lineDir * 1200f;

                    laserGridStartPoints.Add(start);
                    laserGridEndPoints.Add(end);
                }
            }

            // Erupt lasers after delay
            if (timer > 0f && timer % 65f == GridTelegraphTime)
            {
                SoundEngine.PlaySound(LaserSound, target.Center);
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 6f;

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int blastType = GetCalamityProjectileType(PhantomBlast2ProjName);
                    if (blastType != ProjectileID.DeathLaser)
                    {
                        // Spawn sequence of explosions along telegraph lines
                        for (int i = 0; i < laserGridCount; i++)
                        {
                            Vector2 lineStart = laserGridStartPoints[i];
                            Vector2 lineEnd = laserGridEndPoints[i];
                            Vector2 dir = SafeNormalize(lineEnd - lineStart, Vector2.UnitX);
                            
                            for (int j = 0; j < 25; j++)
                            {
                                Vector2 spawnPos = lineStart + dir * (j * 100f);
                                Projectile.NewProjectile(npc.GetSource_FromAI(), spawnPos, Vector2.Zero, blastType, 240, 0f, Main.myPlayer);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Explodes segments in radial sparkles on death.
        /// </summary>
        private void DoAttack_DeathAnimation(NPC npc, ref float timer)
        {
            npc.damage = 0;
            npc.dontTakeDamage = true;
            npc.velocity *= 0.92f;
            npc.rotation *= 0.97f;
            npc.localAI[0] = (float)FrameType.Phase3Glow;

            if (timer == 1f)
            {
                SoundEngine.PlaySound(P3Sound, npc.Center);
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 22f;
            }

            // Sparkle loops
            if (timer % 4f == 0f)
            {
                Vector2 randOffset = Main.rand.NextVector2Circular(70f, 70f);
                for (int i = 0; i < 10; i++)
                {
                    float angle = (i / 10f) * TwoPi;
                    Vector2 dustVel = angle.ToRotationVector2() * 3.5f;
                    Dust d = Dust.NewDustPerfect(npc.Center + randOffset, DustID.PinkCrystalShard, dustVel);
                    d.scale = 1.6f;
                    d.noGravity = true;
                }
            }

            if (timer >= 120f)
            {
                CleanupStrayEntities();
                npc.life = 0;
                npc.HitEffect();
                npc.active = false;
                npc.netUpdate = true;
            }
        }

        /// <summary>
        /// Fades and flies away upwards on target death.
        /// </summary>
        private void DoAttack_DespawnRetreat(NPC npc, ref float timer)
        {
            npc.damage = 0;
            npc.dontTakeDamage = true;
            npc.localAI[0] = (float)FrameType.Phase1Idle;
            npc.frameCounter++;

            npc.velocity = Vector2.Lerp(npc.velocity, new Vector2(0f, -24f), 0.06f);
            npc.rotation = npc.velocity.ToRotation() + PiOver2;
            npc.Opacity = Lerp(1f, 0f, timer / 90f);

            if (timer >= 90f || npc.Opacity <= 0.01f)
            {
                CleanupStrayEntities();
                npc.active = false;
                npc.netUpdate = true;
            }
        }
        #endregion

        #region Mathematics & Easing Utilities
        /// <summary>
        /// Eases linearly between values.
        /// </summary>
        private static float Lerp(float first, float second, float progress)
        {
            return first + (second - first) * MathHelper.Clamp(progress, 0f, 1f);
        }

        /// <summary>
        /// Selects next attack state based on current phase cycles.
        /// </summary>
        private AttackState SelectNextState(int phase, ref float cycleIndex)
        {
            cycleIndex++;
            List<AttackState> pattern;

            switch (phase)
            {
                case 2:
                    pattern = new List<AttackState>
                    {
                        AttackState.EctoplasmUppercutCharges,
                        AttackState.VortexCharge,
                        AttackState.WispCircleCharges,
                        AttackState.SpiritPetal,
                        AttackState.EctoplasmUppercutCharges,
                        AttackState.ArcingSouls
                    };
                    break;
                case 3:
                    pattern = new List<AttackState>
                    {
                        AttackState.CloneSplit,
                        AttackState.AsgoreRingSoulAttack,
                        AttackState.VortexCharge,
                        AttackState.ArcingSouls,
                        AttackState.SpiritPetal,
                        AttackState.EctoplasmUppercutCharges
                    };
                    break;
                default:
                    pattern = new List<AttackState>
                    {
                        AttackState.EctoplasmUppercutCharges,
                        AttackState.WispCircleCharges,
                        AttackState.EctoplasmUppercutCharges,
                        AttackState.SpiritPetal
                    };
                    break;
            }

            int index = (int)cycleIndex % pattern.Count;
            return pattern[index];
        }

        /// <summary>
        /// Restricts target coordinate positions inside desperation boundaries.
        /// </summary>
        private void ForcePlayerInsideArena(Player player)
        {
            float dist = Vector2.Distance(player.Center, desperationCenter);
            if (dist > ArenaRadius)
            {
                // Drag pull
                Vector2 pull = SafeNormalize(desperationCenter - player.Center, Vector2.UnitY);
                player.velocity += pull * 1.5f;

                if (Main.GameUpdateCount % 8 == 0)
                {
                    player.Hurt(Terraria.DataStructures.PlayerDeathReason.ByCustomReason(Terraria.Localization.NetworkText.FromLiteral(player.name + " was vaporized by the Necropolis Singularity!")), 32, 0);
                    // Spawn dust flash
                    for (int i = 0; i < 12; i++)
                    {
                        Vector2 dVel = Main.rand.NextVector2Circular(5f, 5f);
                        Dust d = Dust.NewDustPerfect(player.Center, DustID.PinkCrystalShard, dVel);
                        d.noGravity = true;
                    }
                }
            }
        }

        /// <summary>
        /// Safe target vector direction normalization.
        /// </summary>
        private static Vector2 SafeNormalize(Vector2 vector, Vector2 fallback)
        {
            if (vector.LengthSquared() < 0.0001f)
            {
                return fallback;
            }
            vector.Normalize();
            return vector;
        }

        /// <summary>
        /// Evaluates a quadratic Bezier curve point given progress 0..1.
        /// </summary>
        private static Vector2 EvaluateBezier(Vector2 p0, Vector2 p1, Vector2 p2, float t)
        {
            float mt = 1f - t;
            return mt * mt * p0 + 2f * mt * t * p1 + t * t * p2;
        }
        #endregion

        #region Helper Overrides & Methods
        /// <summary>
        /// Dynamically retrieves Calamity Mod projectile indices safely.
        /// </summary>
        private int GetCalamityProjectileType(string projectileName)
        {
            if (!string.IsNullOrWhiteSpace(projectileName) && ModContent.TryFind($"CalamityMod/{projectileName}", out ModProjectile projectile))
            {
                return projectile.Type;
            }
            return ProjectileID.DeathLaser; // Safe vanilla fallback
        }

        /// <summary>
        /// Check Phase Transition thresholds and set status flags.
        /// </summary>
        private void CheckPhaseTransitions(NPC npc, Player target, ref int phase, ref AttackState state, ref float timer, ref float stateTracker)
        {
            float healthRatio = npc.life / (float)npc.lifeMax;

            if (phase == 1 && healthRatio < PhaseLifeRatios[0])
            {
                phase = 2;
                npc.ai[0] = 2f;
                CleanupStrayEntities();
                TransitionToState(npc, AttackState.AttackSelectionWait);
                SoundEngine.PlaySound(P2Sound, npc.Center);
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 10f;
            }
            else if (phase == 2 && healthRatio < PhaseLifeRatios[1])
            {
                phase = 3;
                npc.ai[0] = 3f;
                CleanupStrayEntities();
                TransitionToState(npc, AttackState.CloneSplit);
                SoundEngine.PlaySound(P3Sound, npc.Center);
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 15f;
            }
        }

        /// <summary>
        /// Manages dynamic damage reduction curves for active state loops.
        /// </summary>
        private void ManageDR(NPC npc, AttackState state, int phase)
        {
            if (state == AttackState.SpawnAnimation || state == AttackState.DeathAnimation || state == AttackState.CloneSplit)
            {
                npc.Calamity().DR = 0.99f;
                npc.dontTakeDamage = true;
            }
            else if (state == AttackState.DesperationArena || state == AttackState.NecropolisOverdrive || state == AttackState.SpectralLaserBarrage)
            {
                npc.Calamity().DR = 0.98f;
                npc.dontTakeDamage = true;
            }
            else if (state == AttackState.AsgoreRingSoulAttack)
            {
                npc.Calamity().DR = 0.65f;
                npc.dontTakeDamage = false;
            }
            else
            {
                npc.Calamity().DR = phase switch
                {
                    2 => 0.22f,
                    3 => 0.30f,
                    _ => 0.15f
                };
                npc.dontTakeDamage = false;
            }
        }

        /// <summary>
        /// Update local visual state variables and tentacle targets.
        /// </summary>
        private void UpdateLocalVisuals(NPC npc, AttackState state, float timer)
        {
            // Aura intensity increase
            int phase = (int)npc.ai[0];
            if (phase >= 2)
            {
                auraDrawAlpha = Lerp(auraDrawAlpha, 0.40f, 0.05f);
            }

            // Update spectral tentacle target coordinates
            if (tentaclesInitialized)
            {
                Player target = Main.player[npc.target];
                float angleSpeed = 0.02f;

                for (int i = 0; i < 4; i++)
                {
                    // Swipings during tentacle attack states
                    if (state == AttackState.SpawnAnimation || state == AttackState.DeathAnimation)
                    {
                        // Inward coil
                        tentacleAngles[i] = (i / 4f) * TwoPi + timer * angleSpeed;
                        tentacleLengths[i] = Lerp(tentacleLengths[i], 120f, 0.08f);
                        Vector2 offset = tentacleAngles[i].ToRotationVector2() * tentacleLengths[i];
                        tentacleTargets[i] = npc.Center + offset;
                    }
                    else if (state == AttackState.DesperationArena || state == AttackState.NecropolisOverdrive || state == AttackState.SpectralLaserBarrage)
                    {
                        // Outward frantic whipping
                        tentacleAngles[i] = (i / 4f) * TwoPi + timer * 0.06f;
                        tentacleLengths[i] = Lerp(tentacleLengths[i], 220f + MathF.Sin(timer * 0.15f) * 35f, 0.1f);
                        Vector2 offset = tentacleAngles[i].ToRotationVector2() * tentacleLengths[i];
                        tentacleTargets[i] = npc.Center + offset;
                    }
                    else
                    {
                        // Hover and reach towards player
                        tentacleAngles[i] = (i / 4f) * TwoPi + timer * angleSpeed;
                        tentacleLengths[i] = Lerp(tentacleLengths[i], 190f, 0.05f);
                        
                        // Reach vector
                        Vector2 targetDir = SafeNormalize(target.Center - npc.Center, Vector2.UnitX);
                        Vector2 hoverOffset = tentacleAngles[i].ToRotationVector2() * tentacleLengths[i] + targetDir * 60f;
                        tentacleTargets[i] = Vector2.Lerp(tentacleTargets[i], npc.Center + hoverOffset, 0.08f);
                    }
                }
            }
        }

        /// <summary>
        /// Clean up duplicate clones on transitions.
        /// </summary>
        private void CleanupStrayDuplicates()
        {
            int phantomType = ModContent.NPCType<PolterPhantom>();
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                if (Main.npc[i].active && Main.npc[i].type == phantomType)
                {
                    Main.npc[i].active = false;
                }
            }
        }

        /// <summary>
        /// Clean up stray NPC entities and projectiles on transitions.
        /// </summary>
        private void CleanupStrayEntities()
        {
            CleanupStrayDuplicates();

            int shotType = GetCalamityProjectileType(PhantomShotProjName);
            int shot2Type = GetCalamityProjectileType(PhantomShot2ProjName);
            int blastType = GetCalamityProjectileType(PhantomBlastProjName);
            int blast2Type = GetCalamityProjectileType(PhantomBlast2ProjName);
            int mineType = GetCalamityProjectileType(PhantomMineProjName);
            int vortexType = GetCalamityProjectileType(GhostVortexProjName);
            int ghostType = GetCalamityProjectileType(PhantomGhostShotProjName);

            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && (p.type == shotType || p.type == shot2Type || p.type == blastType || p.type == blast2Type || p.type == mineType || p.type == vortexType || p.type == ghostType))
                {
                    p.Kill();
                }
            }
        }

        /// <summary>
        /// Triggers retreats if player dies.
        /// </summary>
        private void ExecuteDespawnAI(NPC npc)
        {
            if (npc.ai[1] != (float)AttackState.DespawnRetreat)
            {
                TransitionToState(npc, AttackState.DespawnRetreat);
            }
        }

        private void TransitionToState(NPC npc, AttackState newState)
        {
            npc.ai[1] = (float)newState;
            npc.ai[2] = 0f;
            npc.ai[3] = 0f;
            npc.netUpdate = true;
        }
        #endregion

        #region Frame Override FindFrame
        /// <summary>
        /// FindFrame override matching animated state profiles.
        /// </summary>
        public override void FindFrame(NPC npc, int frameHeight)
        {
            int phase = (int)npc.ai[0];
            FrameType frame = FrameType.Phase1Idle;

            if (phase == 2) frame = FrameType.Phase2Charge;
            if (phase >= 3) frame = FrameType.Phase3Glow;

            npc.frameCounter++;
            int minFrame = 0;
            int maxFrame = 3;

            switch (frame)
            {
                case FrameType.Phase2Charge:
                    minFrame = 4;
                    maxFrame = 7;
                    break;
                case FrameType.Phase3Glow:
                    minFrame = 8;
                    maxFrame = 11;
                    break;
                default:
                    minFrame = 0;
                    maxFrame = 3;
                    break;
            }

            if (npc.frameCounter >= 7)
            {
                npc.frameCounter = 0;
                npc.frame.Y += frameHeight;
            }

            if (npc.frame.Y < frameHeight * minFrame || npc.frame.Y >= frameHeight * (maxFrame + 1))
            {
                npc.frame.Y = frameHeight * minFrame;
            }
        }
        #endregion

        #region Drawing Overrides PreDraw & PostDraw

        /// <summary>
        /// Custom indicator telegraph lines, grids, boundaries, and tentacles.
        /// </summary>
        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            int phase = (int)npc.ai[0];
            AttackState state = (AttackState)(int)npc.ai[1];
            float timer = npc.ai[2];

            // 1. Draw charging telegraph line during sound warning state
            if (state == AttackState.EctoplasmUppercutCharges && timer < 30f)
            {
                Player target = Main.player[npc.target];
                Vector2 anticipatedPos = new Vector2(npc.Center.X, target.Center.Y);
                // Draw vertical line from bottom to top
                Vector2 start = new Vector2(npc.Center.X, Main.screenPosition.Y + Main.screenHeight);
                Vector2 end = new Vector2(npc.Center.X, Main.screenPosition.Y);

                Color col = Color.LightCyan * (timer / 30f) * 0.85f;
                DrawTelegraphLine(spriteBatch, start, end, col, 10f);
            }

            // 2. Draw crossing laser telegraph grids
            if (state == AttackState.SpectralLaserBarrage)
            {
                float relativeTimer = timer % 65f;
                if (relativeTimer < 45f)
                {
                    Color col = Color.Pink * (relativeTimer / 45f) * 0.75f;
                    for (int i = 0; i < laserGridStartPoints.Count; i++)
                    {
                        DrawTelegraphLine(spriteBatch, laserGridStartPoints[i], laserGridEndPoints[i], col, 6f);
                    }
                }
            }

            // 3. Draw Necropolis Singularity desperation boundary circle
            if (phase == 4 && (state == AttackState.NecropolisOverdrive || state == AttackState.SpectralLaserBarrage || state == AttackState.DesperationArena))
            {
                DrawNecropolisBoundary(spriteBatch, desperationCenter, desperationVignetteAlpha);
            }

            // 4. Draw mathematically modeled spectral tentacles using Bezier splines
            if (npc.Opacity > 0.05f)
            {
                DrawSpectralTentacles(spriteBatch, npc);
            }

            // 5. Draw local pink glow aura if active in higher phases
            if (phase >= 2 && auraDrawAlpha > 0.01f)
            {
                Texture2D texture = TextureAssets.Npc[npc.type].Value;
                Vector2 origin = npc.frame.Size() * 0.5f;
                
                for (int i = 0; i < 4; i++)
                {
                    Vector2 offset = (i * PiOver2).ToRotationVector2() * 6f;
                    spriteBatch.Draw(texture, npc.Center + offset - Main.screenPosition, npc.frame, Color.Pink * auraDrawAlpha, npc.rotation, origin, npc.scale, SpriteEffects.None, 0f);
                }
            }

            // Draw standard texture on top
            return true;
        }

        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            // Draw Calamity Eye glow masks
            Texture2D polterGlowEctoplasm = ModContent.Request<Texture2D>("CalamityMod/NPCs/Polterghast/PolterghastGlow").Value;
            Texture2D polterGlowHeart = ModContent.Request<Texture2D>("CalamityMod/NPCs/Polterghast/PolterghastGlow2").Value;
            Vector2 origin = npc.frame.Size() * 0.5f;

            Color glowColor = Color.White * npc.Opacity;
            spriteBatch.Draw(polterGlowEctoplasm, npc.Center - Main.screenPosition, npc.frame, glowColor, npc.rotation, origin, npc.scale, SpriteEffects.None, 0f);
            spriteBatch.Draw(polterGlowHeart, npc.Center - Main.screenPosition, npc.frame, glowColor, npc.rotation, origin, npc.scale, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// Draws 4 spiritual ectoplasmic tentacles dynamically lashing using Bezier formulas.
        /// </summary>
        private void DrawSpectralTentacles(SpriteBatch spriteBatch, NPC npc)
        {
            Texture2D segmentTex = TextureAssets.MagicPixel.Value;
            if (segmentTex == null) return;

            // Offset positions around body
            Vector2[] bases = new Vector2[4]
            {
                npc.Center + new Vector2(-28f, -20f).RotatedBy(npc.rotation),
                npc.Center + new Vector2(28f, -20f).RotatedBy(npc.rotation),
                npc.Center + new Vector2(-20f, 40f).RotatedBy(npc.rotation),
                npc.Center + new Vector2(20f, 40f).RotatedBy(npc.rotation)
            };

            for (int i = 0; i < 4; i++)
            {
                Vector2 start = bases[i];
                Vector2 end = tentacleTargets[i];
                
                // Form a curved control point
                Vector2 mid = Vector2.Lerp(start, end, 0.5f) + new Vector2(50f, -40f).RotatedBy(npc.rotation + i * PiOver2);

                const int Segments = 12;
                Vector2 prevPos = start;

                // Alternate colors representing pink/cyan phantoplastic streams
                Color tColor = (i % 2 == 0) ? Color.Pink : Color.LightCyan;
                Color drawCol = tColor * npc.Opacity * 0.7f;

                for (int j = 1; j <= Segments; j++)
                {
                    float t = j / (float)Segments;
                    Vector2 currentPos = EvaluateBezier(start, mid, end, t);

                    // Draw segment chain
                    float width = Lerp(14f, 4f, t);
                    DrawTelegraphLine(spriteBatch, prevPos, currentPos, drawCol, width);
                    
                    // Draw segment joint bulbs
                    Vector2 scale = new Vector2(width) / segmentTex.Size();
                    spriteBatch.Draw(segmentTex, currentPos - Main.screenPosition, null, drawCol * 0.8f, 0f, segmentTex.Size() * 0.5f, scale, SpriteEffects.None, 0f);

                    prevPos = currentPos;
                }
            }
        }

        /// <summary>
        /// Drawing helper for rendering solid color vector lines.
        /// </summary>
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

        /// <summary>
        /// Drawing helper for rendering circular desperation boundary lines.
        /// </summary>
        private void DrawNecropolisBoundary(SpriteBatch spriteBatch, Vector2 center, float alpha)
        {
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            if (pixel == null) return;

            int segments = 100;
            float radius = ArenaRadius;
            Vector2 prevPoint = center + new Vector2(radius, 0f);

            for (int i = 1; i <= segments; i++)
            {
                float angle = (i / (float)segments) * TwoPi;
                Vector2 nextPoint = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;

                // Swirling boundaries alternating cyan & pink colors
                Color lineColor = (i % 2 == 0) ? Color.Pink * alpha * 0.8f : Color.LightCyan * alpha * 0.4f;
                DrawTelegraphLine(spriteBatch, prevPoint, nextPoint, lineColor, 6f);
                prevPoint = nextPoint;
            }
        }
        #endregion

        #region Multiplayer Sync Protocols
        /// <summary>
        /// Local custom packet synchronization writer.
        /// </summary>
        public struct BossAttackStateData
        {
            public int AttackState;
            public float Timer;
            public float StateTracker;
            public int CurrentPhase;
            public Vector2 TargetCenter;
            public float SyncValue1;
            public float SyncValue2;

            public void Write(BinaryWriter writer)
            {
                writer.Write(AttackState);
                writer.Write(Timer);
                writer.Write(StateTracker);
                writer.Write(CurrentPhase);
                writer.Write(TargetCenter.X);
                writer.Write(TargetCenter.Y);
                writer.Write(SyncValue1);
                writer.Write(SyncValue2);
            }

            public static BossAttackStateData Read(BinaryReader reader)
            {
                return new BossAttackStateData
                {
                    AttackState = reader.ReadInt32(),
                    Timer = reader.ReadSingle(),
                    StateTracker = reader.ReadSingle(),
                    CurrentPhase = reader.ReadInt32(),
                    TargetCenter = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                    SyncValue1 = reader.ReadSingle(),
                    SyncValue2 = reader.ReadSingle()
                };
            }
        }

        public void SendPacket(NPC npc, BossAttackStateData packetData)
        {
            if (Main.netMode == NetmodeID.Server)
            {
                ModPacket packet = global::CalamityIUMWMode.CalamityIUMWMode.Instance.GetPacket();
                packet.Write((byte)1); // Local sync message type key
                packet.Write(npc.whoAmI);
                packetData.Write(packet);
                packet.Send(-1, npc.whoAmI);
            }
        }

        public void ReceivePacket(NPC npc, BinaryReader reader)
        {
            BossAttackStateData packetData = BossAttackStateData.Read(reader);
            npc.ai[0] = packetData.CurrentPhase;
            npc.ai[1] = packetData.AttackState;
            npc.ai[2] = packetData.Timer;
            npc.ai[3] = packetData.StateTracker;
            
            if (packetData.AttackState == (int)AttackState.NecropolisOverdrive || packetData.AttackState == (int)AttackState.SpectralLaserBarrage)
            {
                desperationCenter = packetData.TargetCenter;
            }
        }
        #endregion
    }
}
