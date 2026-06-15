// =====================================================================================================================
// THE OLD DUKE - CUSTOM BEHAVIOR OVERRIDE (IUMW MODE)
// =====================================================================================================================
// DESIGN PHILOSOPHY:
// The Old Duke is an ancient, mutated Duke Fishron dwelling in the acidic Sulphurous Sea. He has grown scarred, vicious,
// and commands toxic storms, nuclear clouds, and gravity-distorting marine currents.
// This override suppresses the default update loops of the Old Duke (NPC.PreAI returns false), taking absolute authority
// over all movement physics, targeting, phase transitions, and visual drawings.
//
// FIGHT MECHANICS & FLOW:
// - Phase 1 (100% - 76% HP) - Sulphur Sea Tyrant:
//   * Spawn Animation: Breaks out of a toxic storm, creating sulfuric splash explosions.
//   * Predictive Charges: Consecutive dashes using eased acceleration, aiming ahead of the player using predicted vectors.
//   * Sulphuric Vapor Belch: Spits green gas clouds that block flight paths.
//   * Tooth Ball Vomit: Spits floating tooth balls that detonate into radial sprays of tooth needles.
// - Phase 2 (76% - 48% HP) - Nuclear Mutation:
//   * Fast Speedway Charges: Faster, aggressive dashes leaving trailing afterimages.
//   * Sharkron Spin Summon: Spins in a large circular orbit, spawning a central vortex and raining down sulphurous sharkrons.
//   * Acid Geyser Columns: Summons vertical columns of acid rising from the bottom of the screen, preceded by red vertical warning lines.
// - Phase 3 (48% - 22% HP) - Ancient Scars:
//   * Teleport Mirage: Fades out, teleports behind the player, and spawns 3 phantom shadow copies that dash in sequence before the real Duke strikes.
//   * Sulphur Meteor Rain: Vomits a heavy spray of acid blood and homing blobs while meteors fall from the sky.
// - Phase 4 (22% - 0% HP) - Sulphur Maelstrom (Desperation):
//   * Maelstrom Arena: Locks the player in a 660f Sulphuric Maelstrom ring. Crossing the boundary causes rapid damage.
//   * Maelstrom Rage Dash: Charges along the ring boundary at extreme speeds while firing concentric circles of toxic bubbles.
//   * Nuclear Core Pulse: Remains invulnerable at the center, pulsing expanding rings of radioactive energy that players must weave through.
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
using CalamityMod.NPCs.OldDuke;
using CalamityMod.Projectiles.Boss;
using CalamityMod.World;
using CalamityMod.Events;
using CalamityMod.Buffs.StatDebuffs;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using CalamityIUMWMode.Core.Systems;

using CalamityOldDuke = CalamityMod.NPCs.OldDuke.OldDuke;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.OldDuke
{
    internal sealed class OldDukeIUMWAI : IUMWBossAI
    {
        #region Constants & Configuration
        // NPC Identifiers
        public override int NPCType => ModContent.NPCType<CalamityOldDuke>();
        public override string BossName => "The Old Duke";

        // Phase Thresholds & Settings
        public override float[] PhaseLifeRatios => new[] { 0.76f, 0.48f, 0.22f };
        public override int AttackCycleLength => 120;
        public override float MotionIntensity => 1.25f;
        public override Color DebugColor => new(122, 255, 82);

        // Sound Registers (Direct Calamity Custom Sounds)
        public static readonly SoundStyle RoarSound = CalamityOldDuke.RoarSound;
        public static readonly SoundStyle VomitSound = CalamityOldDuke.VomitSound;
        public static readonly SoundStyle VortexSpawnSound = CalamityOldDuke.VortexSpawnSound;
        public static readonly SoundStyle DashSound = CalamityOldDuke.DashSound;
        public static readonly SoundStyle DashSoundP3 = CalamityOldDuke.DashSoundP3;
        public static readonly SoundStyle HuffSound = CalamityOldDuke.HuffSound;

        // Math Constants
        private const float TwoPi = MathHelper.TwoPi;
        private const float Pi = MathHelper.Pi;
        private const float PiOver2 = MathHelper.PiOver2;
        private const float ArenaRadius = 660f;

        // Projectile Reference Keys (Resolved dynamically at runtime)
        private const string AcidBubbleProjName = "SulphuricAcidBubble";
        private const string AcidMistProjName = "SulphuricAcidMist";
        private const string AcidDropProjName = "SulphuricDrop";
        private const string AcidNukeProjName = "SulphuricNukesplosion";
        private const string ToothSpikeProjName = "OldDukeToothBallSpike";
        private const string VortexProjName = "OldDukeVortex";
        private const string PoisonCloudProjName = "SandPoisonCloudOldDuke";
        private const string GoreProjName = "OldDukeGore";
        #endregion

        #region State Machine Enumeration
        public enum AttackState
        {
            SpawnAnimation = 0,
            AttackSelectionWait = 1,
            ChargeIndicatorSound = 2,
            Charge = 3,
            FastRegularCharge = 4,
            SulphuricVaporBelch = 5,
            SharkronSpinSummon = 6,
            ToothBallVomit = 7,
            GoreAndAcidSpit = 8,
            TeleportPause = 9,
            AcidGeyserRain = 10,
            MaelstromRageDash = 11,
            NuclearDetonation = 12,
            DeathAnimation = 13,
            DespawnRetreat = 14
        }

        public enum FrameType
        {
            FlapWings = 0,
            Charge = 1,
            Roar = 2,
            Tired = 3
        }
        #endregion

        #region Local Fields
        // Radii and alphas for drawing indicators
        private float auraDrawAlpha = 0f;
        private float nuclearPulseRadius = 0f;
        private float nuclearPulseAlpha = 0f;

        // Geyser position tracking
        private readonly Vector2[] geyserPositions = new Vector2[6];
        private int geyserCount = 0;

        // Teleport Clones / Mirage shadow copies
        private readonly Vector2[] miragePositions = new Vector2[4];
        private readonly float[] mirageRotations = new float[4];
        private readonly float[] mirageAlphas = new float[4];
        private int mirageCount = 0;

        // Stamina & exhaustion fields
        private bool isExhausted = false;
        private int exhaustionTimer = 0;
        private int consecutiveDashes = 0;

        // Desperation survival timer
        private int desperationTimer = 0;
        private Vector2 desperationCenter = Vector2.Zero;
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

            // Cleanse Irradiated debuff to make the fight fair
            if (target.HasBuff(ModContent.BuffType<Irradiated>()))
            {
                target.ClearBuff(ModContent.BuffType<Irradiated>());
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
                TransitionToState(npc, AttackState.MaelstromRageDash);
                state = AttackState.MaelstromRageDash;
                desperationCenter = target.Center;
                desperationTimer = 900; // 15 seconds survival
                CleanupStrayEntities();
                SoundEngine.PlaySound(RoarSound, npc.Center);
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 20f;
            }

            // Handle Phase Transitions (1, 2, 3)
            if (currentPhase < 4)
            {
                CheckPhaseTransitions(npc, target, ref currentPhase, ref state, ref timer, ref stateTracker);
            }

            // Update local visuals
            UpdateLocalVisuals(npc, state, timer);

            // Execute Stamina cooldown
            UpdateStaminaCooldown(npc);

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
                case AttackState.ChargeIndicatorSound:
                    DoAttack_ChargeIndicatorSound(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.Charge:
                    DoAttack_Charge(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.FastRegularCharge:
                    DoAttack_FastRegularCharge(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.SulphuricVaporBelch:
                    DoAttack_SulphuricVaporBelch(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.SharkronSpinSummon:
                    DoAttack_SharkronSpinSummon(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.ToothBallVomit:
                    DoAttack_ToothBallVomit(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.GoreAndAcidSpit:
                    DoAttack_GoreAndAcidSpit(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.TeleportPause:
                    DoAttack_TeleportPause(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.AcidGeyserRain:
                    DoAttack_AcidGeyserRain(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.MaelstromRageDash:
                    DoAttack_MaelstromRageDash(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.NuclearDetonation:
                    DoAttack_NuclearDetonation(npc, target, ref timer, ref stateTracker);
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
        /// Initial spawn animation. Rise from a toxic sulfuric cloud with radial dust effects.
        /// </summary>
        private void DoAttack_SpawnAnimation(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.damage = 0; // Invulnerable contact
            npc.dontTakeDamage = true;
            npc.localAI[0] = (float)FrameType.FlapWings;
            npc.frameCounter++;

            if (timer < 45f)
            {
                // Ascend slowly from bottom
                npc.velocity = new Vector2(0f, -4f);
                npc.Opacity = Lerp(0f, 1f, timer / 45f);

                // Spawn dense acid sea bubbles/dust
                if (timer % 5f == 0f)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        Vector2 offset = Main.rand.NextVector2Circular(80f, 40f);
                        Dust d = Dust.NewDustPerfect(npc.Center + offset, DustID.GreenFairy, new Vector2(0f, -3f));
                        d.noGravity = true;
                        d.scale = Main.rand.NextFloat(1.2f, 1.8f);
                    }
                }
            }
            else if (timer < 90f)
            {
                // Slow to hover and face target
                npc.velocity *= 0.95f;
                npc.spriteDirection = (target.Center.X < npc.Center.X) ? 1 : -1;
                npc.rotation = npc.rotation.AngleLerp(npc.AngleTo(target.Center), 0.08f);

                if (timer == 60f)
                {
                    // Unleash a massive splash roar
                    SoundEngine.PlaySound(RoarSound, npc.Center);
                    Main.LocalPlayer.Calamity().GeneralScreenShakePower = 12f;

                    // Release a circle of decorative toxic splashes
                    for (int i = 0; i < 36; i++)
                    {
                        float angle = (i / 36f) * TwoPi;
                        Vector2 vel = angle.ToRotationVector2() * 8f;
                        Dust d = Dust.NewDustPerfect(npc.Center, DustID.GreenFairy, vel);
                        d.scale = 2f;
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
        /// Standard selection state between attacks. Hovers predictively.
        /// </summary>
        private void DoAttack_AttackSelectionWait(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = 0; // Suppress contact damage while thinking
            npc.localAI[0] = (float)FrameType.FlapWings;
            npc.frameCounter++;

            // Face the target
            npc.spriteDirection = (target.Center.X < npc.Center.X) ? 1 : -1;
            
            // Predictive hover position
            float hoverSide = (stateTracker == 0) ? -550f : 550f;
            Vector2 hoverDest = target.Center + new Vector2(hoverSide, -320f);
            Vector2 toDest = hoverDest - npc.Center;
            float dist = toDest.Length();

            // Hover movement
            if (dist > 40f)
            {
                float speed = Lerp(10f, 25f, dist / 1000f);
                npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(toDest, -Vector2.UnitY) * speed, 0.08f);
            }
            else
            {
                npc.velocity *= 0.92f;
            }

            // Interpolate rotation to face player
            float targetRot = npc.AngleTo(target.Center);
            if (npc.spriteDirection == 1)
                targetRot += Pi;
            npc.rotation = npc.rotation.AngleLerp(targetRot, 0.12f);

            // Determine upcoming attack selection wait duration
            float selectDuration = 50f;
            if (phase == 2) selectDuration = 40f;
            if (phase == 3) selectDuration = 30f;

            if (timer >= selectDuration)
            {
                // Select next attack based on phase patterns
                AttackState nextAttack = SelectNextState(phase, ref stateTracker);
                TransitionToState(npc, nextAttack);
            }
        }

        /// <summary>
        /// Plays a loud rumble indicating a heavy incoming dash.
        /// </summary>
        private void DoAttack_ChargeIndicatorSound(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.damage = 0;
            npc.velocity *= 0.85f;
            npc.localAI[0] = (float)FrameType.Roar;

            if (timer == 1f)
            {
                SoundEngine.PlaySound(VomitSound, npc.Center);
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 6f;
            }

            // Rotate towards target anticipated trajectory
            Vector2 anticipatedPos = target.Center + target.velocity * 12f;
            float targetRot = npc.AngleTo(anticipatedPos);
            if (npc.spriteDirection == 1)
                targetRot += Pi;
            npc.rotation = npc.rotation.AngleLerp(targetRot, 0.2f);

            if (timer >= 20f)
            {
                TransitionToState(npc, AttackState.Charge);
            }
        }

        /// <summary>
        /// Basic Predictive Dash. Moves along calculated vector aiming ahead of player.
        /// </summary>
        private void DoAttack_Charge(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = npc.defDamage;
            npc.localAI[0] = (float)FrameType.Charge;

            float dashDuration = 25f;
            float dashSpeed = 36f;
            float aimAhead = 1.1f;

            if (phase == 2) { dashDuration = 22f; dashSpeed = 42f; aimAhead = 1.3f; }
            if (phase == 3) { dashDuration = 18f; dashSpeed = 48f; aimAhead = 1.5f; }

            if (timer == 1f)
            {
                // Execute dash vector calculation
                SoundEngine.PlaySound(DashSound, npc.Center);
                consecutiveDashes++;

                Vector2 targetFuture = target.Center + target.velocity * aimAhead * 10f;
                Vector2 dashVel = SafeNormalize(targetFuture - npc.Center, -Vector2.UnitY) * dashSpeed;
                npc.velocity = dashVel;

                npc.spriteDirection = (npc.velocity.X < 0f) ? 1 : -1;
                npc.rotation = npc.velocity.ToRotation();
                if (npc.spriteDirection == 1)
                    npc.rotation += Pi;

                npc.netUpdate = true;
            }

            // Apply quadratic speed decay towards end of charge
            if (timer > dashDuration * 0.7f)
            {
                npc.velocity *= 0.94f;
            }
            else
            {
                // Emit trailing green acid cloud particles
                if (timer % 3f == 0f)
                {
                    int cloudType = GetCalamityProjectileType(PoisonCloudProjName);
                    if (Main.netMode != NetmodeID.MultiplayerClient && cloudType != ProjectileID.DeathLaser)
                    {
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, Vector2.Zero, cloudType, 180, 0f, Main.myPlayer);
                    }
                }
            }

            if (timer >= dashDuration)
            {
                // If exhausted after too many dashes, trigger fatigue
                if (consecutiveDashes >= 5)
                {
                    isExhausted = true;
                    exhaustionTimer = 180;
                    consecutiveDashes = 0;
                }
                TransitionToState(npc, AttackState.AttackSelectionWait);
            }
        }

        /// <summary>
        /// Rapid dash sequence with minimal start lag.
        /// </summary>
        private void DoAttack_FastRegularCharge(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = npc.defDamage;
            npc.localAI[0] = (float)FrameType.Charge;

            float dashDuration = 18f;
            float dashSpeed = 54f;

            if (timer == 1f)
            {
                SoundEngine.PlaySound(DashSoundP3, npc.Center);
                consecutiveDashes++;

                // Lock straight onto target center
                Vector2 dashVel = SafeNormalize(target.Center - npc.Center, -Vector2.UnitY) * dashSpeed;
                npc.velocity = dashVel;

                npc.spriteDirection = (npc.velocity.X < 0f) ? 1 : -1;
                npc.rotation = npc.velocity.ToRotation();
                if (npc.spriteDirection == 1)
                    npc.rotation += Pi;

                npc.netUpdate = true;
            }

            // Dust generation along trail
            for (int i = 0; i < 3; i++)
            {
                Dust d = Dust.NewDustPerfect(npc.Center + Main.rand.NextVector2Circular(60f, 60f), DustID.GreenFairy, -npc.velocity * 0.2f);
                d.noLight = true;
                d.noGravity = true;
            }

            if (timer >= dashDuration)
            {
                TransitionToState(npc, AttackState.AttackSelectionWait);
            }
        }

        /// <summary>
        /// Circles target predictively and releases lingering green poison gas clouds.
        /// </summary>
        private void DoAttack_SulphuricVaporBelch(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = 0; // Contact damage disabled
            npc.localAI[0] = (float)FrameType.Roar;

            // Circle flight path
            float orbitRadius = 600f;
            float orbitSpeed = 0.04f;
            if (phase == 2) orbitSpeed = 0.055f;

            float currentAngle = timer * orbitSpeed;
            Vector2 orbitCenter = target.Center;
            Vector2 dest = orbitCenter + new Vector2(MathF.Cos(currentAngle), MathF.Sin(currentAngle)) * orbitRadius;
            
            npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(dest - npc.Center, Vector2.UnitX) * 22f, 0.08f);

            // Face target
            npc.spriteDirection = (target.Center.X < npc.Center.X) ? 1 : -1;
            float targetRot = npc.AngleTo(target.Center);
            if (npc.spriteDirection == 1)
                targetRot += Pi;
            npc.rotation = npc.rotation.AngleLerp(targetRot, 0.1f);

            // Periodically belch clouds from mouth
            if (timer > 30f && timer % 10f == 0f && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SoundEngine.PlaySound(HuffSound, npc.Center);
                Vector2 mouthPos = GetMouthPosition(npc);
                Vector2 spitVel = SafeNormalize(target.Center - mouthPos, Vector2.UnitX) * 12f;

                int cloudType = GetCalamityProjectileType(PoisonCloudProjName);
                if (cloudType != ProjectileID.DeathLaser)
                {
                    Projectile.NewProjectile(npc.GetSource_FromAI(), mouthPos, spitVel, cloudType, 200, 0f, Main.myPlayer);
                }
            }

            if (timer >= 150f)
            {
                TransitionToState(npc, AttackState.AttackSelectionWait);
            }
        }

        /// <summary>
        /// Spins in a fast circle, creating a vacuum vortex pulling player and drops sharkrons.
        /// </summary>
        private void DoAttack_SharkronSpinSummon(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = 0;
            npc.localAI[0] = (float)FrameType.Charge;

            float spinSpeed = 30f;
            float totalRotations = 2f;
            float spinDuration = 90f;

            if (timer == 1f)
            {
                SoundEngine.PlaySound(VortexSpawnSound, npc.Center);
                
                // Align starting velocity
                npc.velocity = SafeNormalize(target.Center - npc.Center, Vector2.UnitX) * spinSpeed;
                npc.spriteDirection = (npc.velocity.X < 0f) ? 1 : -1;
                
                // Spawn a central vortex projectile
                int vortexType = GetCalamityProjectileType(VortexProjName);
                if (Main.netMode != NetmodeID.MultiplayerClient && vortexType != ProjectileID.DeathLaser)
                {
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, Vector2.Zero, vortexType, 260, 0f, Main.myPlayer);
                }
            }

            // Circular rotation calculations
            float rotationalOffset = npc.spriteDirection * TwoPi / spinDuration * totalRotations;
            npc.velocity = npc.velocity.RotatedBy(rotationalOffset);
            npc.rotation = npc.velocity.ToRotation();
            if (npc.spriteDirection == 1)
                npc.rotation += Pi;

            // Spawn falling sharkrons
            if (timer % 15f == 0f && Main.netMode != NetmodeID.MultiplayerClient)
            {
                Vector2 spawnPos = target.Center + new Vector2(Main.rand.NextFloatDirection() * 800f, -650f);
                int sharkType = ModContent.NPCType<SulphurousSharkron>();
                int sharkIdx = NPC.NewNPC(npc.GetSource_FromAI(), (int)spawnPos.X, (int)spawnPos.Y, sharkType);
                if (Main.npc.IndexInRange(sharkIdx))
                {
                    Main.npc[sharkIdx].velocity = new Vector2(Main.rand.NextFloat(-6f, 6f), Main.rand.NextFloat(8f, 15f));
                    Main.npc[sharkIdx].netUpdate = true;
                }
            }

            if (timer >= spinDuration)
            {
                TransitionToState(npc, AttackState.AttackSelectionWait);
            }
        }

        /// <summary>
        /// Vomits tooth balls that float in place and detonate into crossing needles.
        /// </summary>
        private void DoAttack_ToothBallVomit(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = 0;
            npc.localAI[0] = (float)FrameType.Roar;
            
            // Slow down while vomiting
            npc.velocity *= 0.92f;

            // Face player
            npc.spriteDirection = (target.Center.X < npc.Center.X) ? 1 : -1;
            float targetRot = npc.AngleTo(target.Center);
            if (npc.spriteDirection == 1)
                targetRot += Pi;
            npc.rotation = npc.rotation.AngleLerp(targetRot, 0.15f);

            int vomitCount = (phase == 1) ? 3 : 5;
            int vomitInterval = (phase == 1) ? 25 : 15;

            if (timer > 20f && (timer - 20f) % vomitInterval == 0f)
            {
                SoundEngine.PlaySound(VomitSound, npc.Center);
                
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Vector2 mouthPos = GetMouthPosition(npc);
                    Vector2 vomitVel = SafeNormalize(target.Center - mouthPos, Vector2.UnitX).RotatedByRandom(0.25f) * 11f;

                    int toothBallType = ModContent.NPCType<OldDukeToothBall>();
                    int toothBallIdx = NPC.NewNPC(npc.GetSource_FromAI(), (int)mouthPos.X, (int)mouthPos.Y, toothBallType);
                    if (Main.npc.IndexInRange(toothBallIdx))
                    {
                        Main.npc[toothBallIdx].velocity = vomitVel;
                        Main.npc[toothBallIdx].netUpdate = true;
                    }
                }
            }

            if (timer >= 20f + vomitInterval * vomitCount)
            {
                TransitionToState(npc, AttackState.AttackSelectionWait);
            }
        }

        /// <summary>
        /// Heavy vomiting attack spraying acid blood gore and homing needles.
        /// </summary>
        private void DoAttack_GoreAndAcidSpit(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = 0;
            npc.localAI[0] = (float)FrameType.Roar;
            npc.velocity *= 0.9f;

            // Lock onto player vector
            npc.spriteDirection = (target.Center.X < npc.Center.X) ? 1 : -1;
            float targetRot = npc.AngleTo(target.Center);
            if (npc.spriteDirection == 1)
                targetRot += Pi;
            npc.rotation = npc.rotation.AngleLerp(targetRot, 0.18f);

            if (timer == 25f)
            {
                SoundEngine.PlaySound(VomitSound, npc.Center);
                
                // Spawn wide fan of gore/droplets
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Vector2 mouthPos = GetMouthPosition(npc);
                    Vector2 baseDir = SafeNormalize(target.Center - mouthPos, Vector2.UnitX);
                    int goreType = GetCalamityProjectileType(GoreProjName);

                    for (int i = 0; i < 24; i++)
                    {
                        Vector2 velocity = baseDir.RotatedByRandom(0.5f) * Main.rand.NextFloat(8f, 18f);
                        if (goreType != ProjectileID.DeathLaser)
                        {
                            Projectile.NewProjectile(npc.GetSource_FromAI(), mouthPos, velocity, goreType, 220, 0f, Main.myPlayer);
                        }
                    }
                }
            }

            // Continuous stream of homing acid needles following initial gore burst
            if (timer > 25f && timer < 55f && timer % 3f == 0f)
            {
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Vector2 mouthPos = GetMouthPosition(npc);
                    Vector2 baseDir = SafeNormalize(target.Center - mouthPos, Vector2.UnitX);
                    Vector2 vel = baseDir.RotatedByRandom(0.3f) * 14f;

                    int spikeType = GetCalamityProjectileType(ToothSpikeProjName);
                    if (spikeType != ProjectileID.DeathLaser)
                    {
                        Projectile.NewProjectile(npc.GetSource_FromAI(), mouthPos, vel, spikeType, 190, 0f, Main.myPlayer);
                    }
                }
            }

            if (timer >= 75f)
            {
                TransitionToState(npc, AttackState.AttackSelectionWait);
            }
        }

        /// <summary>
        /// Teleports behind the player, leaves mirages, then dashes predictive.
        /// </summary>
        private void DoAttack_TeleportPause(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = 0;
            npc.dontTakeDamage = true;

            const float FadeTime = 40f;

            if (timer <= FadeTime)
            {
                // Fade out
                npc.Opacity = Lerp(1f, 0f, timer / FadeTime);
                npc.velocity *= 0.9f;
                npc.localAI[0] = (float)FrameType.FlapWings;
            }
            else if (timer == FadeTime + 1f)
            {
                // Execute teleport
                SoundEngine.PlaySound(RoarSound, target.Center);
                
                // Select a predictive teleport point behind target
                Vector2 targetVelDir = SafeNormalize(target.velocity, Vector2.UnitX);
                Vector2 teleportOffset = -targetVelDir * 600f + new Vector2(0f, -200f);
                if (teleportOffset.Length() < 400f)
                {
                    teleportOffset = new Vector2((target.Center.X < npc.Center.X) ? 600f : -600f, -200f);
                }

                npc.Center = target.Center + teleportOffset;
                npc.velocity = Vector2.Zero;
                npc.spriteDirection = (target.Center.X < npc.Center.X) ? 1 : -1;
                npc.rotation = npc.rotation.AngleLerp(npc.AngleTo(target.Center), 1f);
                if (npc.spriteDirection == 1)
                    npc.rotation += Pi;

                // Setup mirage phantom shadow coordinates
                mirageCount = 3;
                for (int i = 0; i < mirageCount; i++)
                {
                    float offsetAngle = (i / (float)mirageCount) * TwoPi;
                    miragePositions[i] = target.Center + offsetAngle.ToRotationVector2() * 500f;
                    mirageRotations[i] = miragePositions[i].AngleTo(target.Center);
                    mirageAlphas[i] = 0.8f;
                }

                npc.netUpdate = true;
            }
            else if (timer <= FadeTime * 2f)
            {
                // Fade back in
                float progress = (timer - FadeTime) / FadeTime;
                npc.Opacity = Lerp(0f, 1f, progress);
                npc.localAI[0] = (float)FrameType.Roar;

                // Rotational decay of clones
                for (int i = 0; i < mirageCount; i++)
                {
                    mirageAlphas[i] = Lerp(0.8f, 0f, progress);
                }
            }
            else
            {
                npc.dontTakeDamage = false;
                mirageCount = 0;
                // Transition straight into a fast dash
                TransitionToState(npc, AttackState.FastRegularCharge);
            }
        }

        /// <summary>
        /// Erupts geyser columns from bottom of the screen. Preceded by drawing grids.
        /// </summary>
        private void DoAttack_AcidGeyserRain(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.damage = 0;
            npc.localAI[0] = (float)FrameType.FlapWings;
            npc.frameCounter++;
            npc.velocity *= 0.95f;

            // Align to top of target screen
            Vector2 targetHover = target.Center + new Vector2(0f, -380f);
            npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(targetHover - npc.Center, Vector2.UnitY) * 14f, 0.08f);

            // Face target
            npc.spriteDirection = (target.Center.X < npc.Center.X) ? 1 : -1;

            if (timer == 1f)
            {
                // Establish columns based on target's X positions
                geyserCount = (phase == 2) ? 4 : 5;
                float startX = target.Center.X - 500f;
                float stepX = 1000f / (geyserCount - 1);

                for (int i = 0; i < geyserCount; i++)
                {
                    geyserPositions[i] = new Vector2(startX + i * stepX + Main.rand.NextFloat(-50f, 50f), target.Center.Y);
                }
                
                SoundEngine.PlaySound(VomitSound, npc.Center);
                npc.netUpdate = true;
            }

            // Erupt geysers after 60 frames of warning grids
            if (timer == 60f)
            {
                SoundEngine.PlaySound(VortexSpawnSound, npc.Center);
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 8f;

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int cloudType = GetCalamityProjectileType(PoisonCloudProjName);
                    int goreType = GetCalamityProjectileType(GoreProjName);

                    for (int i = 0; i < geyserCount; i++)
                    {
                        // Spawn upward erupting column particles
                        float xPos = geyserPositions[i].X;
                        float bottomY = target.Center.Y + 600f;

                        for (int j = 0; j < 12; j++)
                        {
                            Vector2 spawn = new Vector2(xPos, bottomY - j * 90f);
                            Vector2 velocity = new Vector2(0f, -14f - Main.rand.NextFloat(0f, 4f));
                            
                            if (cloudType != ProjectileID.DeathLaser)
                            {
                                Projectile.NewProjectile(npc.GetSource_FromAI(), spawn, velocity, cloudType, 220, 0f, Main.myPlayer);
                            }
                            if (goreType != ProjectileID.DeathLaser)
                            {
                                Projectile.NewProjectile(npc.GetSource_FromAI(), spawn, velocity * 1.2f, goreType, 180, 0f, Main.myPlayer);
                            }
                        }
                    }
                }
            }

            if (timer >= 105f)
            {
                geyserCount = 0;
                TransitionToState(npc, AttackState.AttackSelectionWait);
            }
        }

        /// <summary>
        /// Phase 4 Desperation: Dash sequence along border edges of Sulphur Maelstrom arena.
        /// </summary>
        private void DoAttack_MaelstromRageDash(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.damage = npc.defDamage;
            npc.localAI[0] = (float)FrameType.Charge;

            desperationTimer--;
            if (desperationTimer <= 0)
            {
                TransitionToState(npc, AttackState.DeathAnimation);
                return;
            }

            // Keep Duke on the boundary orbit
            float orbitSpeed = 0.07f;
            float currentAngle = timer * orbitSpeed;
            Vector2 orbitDest = desperationCenter + new Vector2(MathF.Cos(currentAngle), MathF.Sin(currentAngle)) * ArenaRadius;

            // Push player inside boundary
            ForcePlayerInsideArena(target);

            // Execute rapid lunges inwards
            if (timer % 50f == 0f)
            {
                SoundEngine.PlaySound(DashSoundP3, npc.Center);
                
                // Dash across the circle towards target
                npc.velocity = SafeNormalize(target.Center - npc.Center, Vector2.UnitX) * 52f;
                npc.spriteDirection = (npc.velocity.X < 0f) ? 1 : -1;
                npc.rotation = npc.velocity.ToRotation();
                if (npc.spriteDirection == 1)
                    npc.rotation += Pi;

                // Spit circles of bubbles
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int bubbleType = GetCalamityProjectileType(AcidBubbleProjName);
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = (i / 8f) * TwoPi;
                        Vector2 bubbleVel = angle.ToRotationVector2() * 6f;
                        if (bubbleType != ProjectileID.DeathLaser)
                        {
                            Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, bubbleVel, bubbleType, 180, 0f, Main.myPlayer);
                        }
                    }
                }
                npc.netUpdate = true;
            }
            else
            {
                // Slide back onto boundary
                npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(orbitDest - npc.Center, Vector2.UnitY) * 28f, 0.15f);
                npc.rotation = npc.velocity.ToRotation();
                if (npc.spriteDirection == 1)
                    npc.rotation += Pi;
            }

            // Transition to central pulse detonation after half survival timer
            if (desperationTimer <= 450)
            {
                TransitionToState(npc, AttackState.NuclearDetonation);
            }
        }

        /// <summary>
        /// Phase 4 Desperation: Lock at center, invulnerable, pulses expanding radioactive rings.
        /// </summary>
        private void DoAttack_NuclearDetonation(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.damage = 0;
            npc.localAI[0] = (float)FrameType.Roar;

            desperationTimer--;
            if (desperationTimer <= 0)
            {
                npc.life = 2; // Break shield protection
                TransitionToState(npc, AttackState.DeathAnimation);
                return;
            }

            // Pull to central lock coordinates
            npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(desperationCenter - npc.Center, Vector2.UnitY) * 12f, 0.12f);
            
            // Spin slowly
            npc.rotation += 0.08f;
            npc.spriteDirection = 1;

            // Restrict player inside boundary
            ForcePlayerInsideArena(target);

            // Pulse expanding visual warning ring circles
            if (timer % 90f == 0f)
            {
                SoundEngine.PlaySound(RoarSound, npc.Center);
                nuclearPulseRadius = 10f;
                nuclearPulseAlpha = 1f;

                // Spawn radial projectiles
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int spikeType = GetCalamityProjectileType(ToothSpikeProjName);
                    for (int i = 0; i < 16; i++)
                    {
                        float angle = (i / 16f) * TwoPi;
                        Vector2 velocity = angle.ToRotationVector2() * 8f;
                        if (spikeType != ProjectileID.DeathLaser)
                        {
                            Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, velocity, spikeType, 210, 0f, Main.myPlayer);
                        }
                    }
                }
            }

            // Expand drawing circle
            if (nuclearPulseAlpha > 0.01f)
            {
                nuclearPulseRadius += 8f;
                nuclearPulseAlpha -= 0.015f;
            }
        }

        /// <summary>
        /// Bespoke segment detonation death sequence.
        /// </summary>
        private void DoAttack_DeathAnimation(NPC npc, ref float timer)
        {
            npc.damage = 0;
            npc.dontTakeDamage = true;
            npc.velocity *= 0.94f;
            npc.rotation *= 0.98f;
            npc.localAI[0] = (float)FrameType.Tired;

            if (timer == 1f)
            {
                SoundEngine.PlaySound(RoarSound, npc.Center);
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 25f;
            }

            // Spawn exploding circles of toxic smoke/dust
            if (timer % 5f == 0f)
            {
                Vector2 randomOffset = Main.rand.NextVector2Circular(80f, 80f);
                for (int i = 0; i < 12; i++)
                {
                    float angle = (i / 12f) * TwoPi;
                    Vector2 dustVel = angle.ToRotationVector2() * 4f;
                    Dust d = Dust.NewDustPerfect(npc.Center + randomOffset, DustID.GreenFairy, dustVel);
                    d.scale = 1.8f;
                    d.noGravity = true;
                }
            }

            if (timer >= 120f)
            {
                // Delete clean up and kill boss
                CleanupStrayEntities();
                npc.life = 0;
                npc.HitEffect();
                npc.active = false;
                npc.netUpdate = true;
            }
        }

        /// <summary>
        /// Escapes straight up out of target's view when player dies.
        /// </summary>
        private void DoAttack_DespawnRetreat(NPC npc, ref float timer)
        {
            npc.damage = 0;
            npc.dontTakeDamage = true;
            npc.localAI[0] = (float)FrameType.FlapWings;
            npc.frameCounter++;

            // Fly vertically upwards at accelerating speed
            npc.velocity = Vector2.Lerp(npc.velocity, new Vector2(0f, -26f), 0.05f);
            npc.rotation = npc.velocity.ToRotation();
            if (npc.spriteDirection == 1)
                npc.rotation += Pi;

            npc.Opacity = Lerp(1f, 0f, timer / 120f);

            if (timer >= 120f || npc.Opacity <= 0.01f)
            {
                CleanupStrayEntities();
                npc.active = false;
                npc.netUpdate = true;
            }
        }
        #endregion

        #region Mathematics & Easing Utilities
        /// <summary>
        /// Linearly interpolates between two float values.
        /// </summary>
        private static float Lerp(float first, float second, float progress)
        {
            return first + (second - first) * MathHelper.Clamp(progress, 0f, 1f);
        }

        /// <summary>
        /// Selects next attack state from local phase queues.
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
                        AttackState.FastRegularCharge,
                        AttackState.FastRegularCharge,
                        AttackState.SharkronSpinSummon,
                        AttackState.AcidGeyserRain,
                        AttackState.GoreAndAcidSpit,
                        AttackState.ChargeIndicatorSound,
                        AttackState.Charge,
                        AttackState.SulphuricVaporBelch
                    };
                    break;
                case 3:
                    pattern = new List<AttackState>
                    {
                        AttackState.TeleportPause,
                        AttackState.ChargeIndicatorSound,
                        AttackState.Charge,
                        AttackState.FastRegularCharge,
                        AttackState.ToothBallVomit,
                        AttackState.AcidGeyserRain,
                        AttackState.GoreAndAcidSpit
                    };
                    break;
                default:
                    pattern = new List<AttackState>
                    {
                        AttackState.ChargeIndicatorSound,
                        AttackState.Charge,
                        AttackState.Charge,
                        AttackState.SulphuricVaporBelch,
                        AttackState.ChargeIndicatorSound,
                        AttackState.Charge,
                        AttackState.ToothBallVomit
                    };
                    break;
            }

            int index = (int)cycleIndex % pattern.Count;
            return pattern[index];
        }

        /// <summary>
        /// Enforces boundaries inside Phase 4 Sulphur Maelstrom ring.
        /// </summary>
        private void ForcePlayerInsideArena(Player player)
        {
            float dist = Vector2.Distance(player.Center, desperationCenter);
            if (dist > ArenaRadius)
            {
                // Apply drag force pull and tick damage
                Vector2 pullDirection = SafeNormalize(desperationCenter - player.Center, Vector2.UnitY);
                player.velocity += pullDirection * 1.6f;

                if (Main.GameUpdateCount % 8 == 0)
                {
                    player.Hurt(Terraria.DataStructures.PlayerDeathReason.ByCustomReason(Terraria.Localization.NetworkText.FromLiteral(player.name + " was vaporized by the Sulphur Maelstrom!")), 35, 0);
                    // Spawn warning circle flash around player
                    for (int i = 0; i < 16; i++)
                    {
                        Vector2 dVel = Main.rand.NextVector2Circular(6f, 6f);
                        Dust d = Dust.NewDustPerfect(player.Center, DustID.GreenFairy, dVel);
                        d.noGravity = true;
                    }
                }
            }
        }

        /// <summary>
        /// Helper to determine the mouth position vector offset by rotation and sprite direction.
        /// </summary>
        private Vector2 GetMouthPosition(NPC npc)
        {
            float direction = (npc.spriteDirection == 1) ? -1f : 1f;
            Vector2 offset = new Vector2(direction * 72f, 26f).RotatedBy(npc.rotation);
            return npc.Center + offset;
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
                SoundEngine.PlaySound(RoarSound, npc.Center);
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 10f;
            }
            else if (phase == 2 && healthRatio < PhaseLifeRatios[1])
            {
                phase = 3;
                npc.ai[0] = 3f;
                CleanupStrayEntities();
                TransitionToState(npc, AttackState.TeleportPause);
                SoundEngine.PlaySound(RoarSound, npc.Center);
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 15f;
            }
        }

        /// <summary>
        /// Manages dynamic damage reduction curves for active state loops.
        /// </summary>
        private void ManageDR(NPC npc, AttackState state, int phase)
        {
            if (state == AttackState.SpawnAnimation || state == AttackState.DeathAnimation || state == AttackState.TeleportPause)
            {
                npc.Calamity().DR = 0.99f; // Max defense shield
                npc.dontTakeDamage = true;
            }
            else if (state == AttackState.NuclearDetonation)
            {
                npc.Calamity().DR = 0.95f; // Shield survival protection
                npc.dontTakeDamage = false;
            }
            else if (isExhausted)
            {
                npc.Calamity().DR = 0.05f; // Extreme vulnerability during fatigue
                npc.dontTakeDamage = false;
            }
            else
            {
                // Scaled base DR
                npc.Calamity().DR = phase switch
                {
                    2 => 0.28f,
                    3 => 0.35f,
                    4 => 0.45f,
                    _ => 0.20f
                };
                npc.dontTakeDamage = false;
            }
        }

        /// <summary>
        /// Update local visuals (Auras, pulses, alphas).
        /// </summary>
        private void UpdateLocalVisuals(NPC npc, AttackState state, float timer)
        {
            // Update aura draw intensity in high phases
            int phase = (int)npc.ai[0];
            if (phase >= 2)
            {
                auraDrawAlpha = Lerp(auraDrawAlpha, 0.45f, 0.05f);
            }

            // Slow down rotation if fatigued
            if (isExhausted)
            {
                npc.rotation *= 0.92f;
            }
        }

        /// <summary>
        /// Manages the stamina decay and fatigue states.
        /// </summary>
        private void UpdateStaminaCooldown(NPC npc)
        {
            if (isExhausted)
            {
                exhaustionTimer--;
                npc.damage = 0; // Can't hurt player while tired
                
                // Spawn huff smoke dust
                if (exhaustionTimer % 15 == 0)
                {
                    SoundEngine.PlaySound(HuffSound, npc.Center);
                    for (int i = 0; i < 6; i++)
                    {
                        Dust d = Dust.NewDustPerfect(npc.Center + Main.rand.NextVector2Circular(50f, 50f), DustID.Smoke, new Vector2(0f, -2f));
                        d.scale = 1.5f;
                    }
                }

                if (exhaustionTimer <= 0)
                {
                    isExhausted = false;
                    consecutiveDashes = 0;
                }
            }
        }

        /// <summary>
        /// Clear stray NPC entities and projectiles on transitions.
        /// </summary>
        private void CleanupStrayEntities()
        {
            int sharkronType = ModContent.NPCType<SulphurousSharkron>();
            int toothBallType = ModContent.NPCType<OldDukeToothBall>();

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                if (Main.npc[i].active && (Main.npc[i].type == sharkronType || Main.npc[i].type == toothBallType))
                {
                    Main.npc[i].active = false;
                }
            }

            int bubbleType = GetCalamityProjectileType(AcidBubbleProjName);
            int spikeType = GetCalamityProjectileType(ToothSpikeProjName);
            int cloudType = GetCalamityProjectileType(PoisonCloudProjName);
            int vortexType = GetCalamityProjectileType(VortexProjName);
            int goreType = GetCalamityProjectileType(GoreProjName);

            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && (p.type == bubbleType || p.type == spikeType || p.type == cloudType || p.type == vortexType || p.type == goreType))
                {
                    p.Kill();
                }
            }
        }

        /// <summary>
        /// Retracts and triggers retreat sequence if target dies.
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
            FrameType frame = (FrameType)(int)npc.localAI[0];

            if (isExhausted)
            {
                frame = FrameType.Tired;
            }

            switch (frame)
            {
                case FrameType.Charge:
                    // Lock to charge frame index
                    npc.frame.Y = frameHeight * 2;
                    npc.frameCounter = 0;
                    break;
                case FrameType.Roar:
                    // Lock to roar frame index
                    npc.frame.Y = frameHeight * 6;
                    npc.frameCounter = 0;
                    break;
                case FrameType.Tired:
                    // Tired/fatigued frames
                    npc.frame.Y = frameHeight * 4;
                    npc.frameCounter = 0;
                    break;
                default:
                    // Standard flap wings animations
                    if (npc.frameCounter >= 6)
                    {
                        npc.frameCounter = 0;
                        npc.frame.Y += frameHeight;
                        if (npc.frame.Y >= frameHeight * 6)
                        {
                            npc.frame.Y = 0;
                        }
                    }
                    break;
            }
        }
        #endregion

        #region Drawing Overrides PreDraw & PostDraw

        /// <summary>
        /// Custom indicator telegraph lines, circles, boundaries, and clones.
        /// </summary>
        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            int phase = (int)npc.ai[0];
            AttackState state = (AttackState)(int)npc.ai[1];
            float timer = npc.ai[2];

            // 1. Draw charging telegraph line during sound warning state
            if (state == AttackState.ChargeIndicatorSound)
            {
                Player target = Main.player[npc.target];
                Vector2 anticipatedPos = target.Center + target.velocity * 12f;
                DrawTelegraphLine(spriteBatch, npc.Center, anticipatedPos, Color.Red * (timer / 20f) * 0.8f, 6f);
            }

            // 2. Draw Geyser Warning Grids before eruption
            if (state == AttackState.AcidGeyserRain && timer < 60f)
            {
                for (int i = 0; i < geyserCount; i++)
                {
                    Vector2 start = new Vector2(geyserPositions[i].X, Main.screenPosition.Y);
                    Vector2 end = new Vector2(geyserPositions[i].X, Main.screenPosition.Y + Main.screenHeight);
                    Color col = Color.Lime * (timer / 60f) * 0.45f;
                    DrawTelegraphLine(spriteBatch, start, end, col, 45f);
                }
            }

            // 3. Draw Teleport Mirage Phantom shadow copies
            if (state == AttackState.TeleportPause && mirageCount > 0)
            {
                Texture2D texture = TextureAssets.Npc[npc.type].Value;
                Vector2 origin = new Vector2(texture.Width / 2, texture.Height / Main.npcFrameCount[npc.type] / 2);
                SpriteEffects effects = npc.spriteDirection == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

                for (int i = 0; i < mirageCount; i++)
                {
                    Color col = Color.Lime * mirageAlphas[i] * 0.7f;
                    spriteBatch.Draw(texture, miragePositions[i] - Main.screenPosition, npc.frame, col, mirageRotations[i], origin, npc.scale, effects, 0f);
                }
            }

            // 4. Draw Sulphur Maelstrom Desperation boundary ring
            if (phase == 4 && (state == AttackState.MaelstromRageDash || state == AttackState.NuclearDetonation))
            {
                DrawMaelstromBoundary(spriteBatch, desperationCenter, 1f);
            }

            // 5. Draw Nuclear Core pulses inside the arena center
            if (state == AttackState.NuclearDetonation && nuclearPulseAlpha > 0.01f)
            {
                DrawRadioactivePulse(spriteBatch, desperationCenter, nuclearPulseRadius, Color.Lime * nuclearPulseAlpha * 0.5f);
            }

            // 6. Draw trailing afterimages if dashing
            if (state == AttackState.Charge || state == AttackState.FastRegularCharge || state == AttackState.MaelstromRageDash)
            {
                Texture2D texture = TextureAssets.Npc[npc.type].Value;
                Vector2 origin = new Vector2(texture.Width / 2, texture.Height / Main.npcFrameCount[npc.type] / 2);
                SpriteEffects effects = npc.spriteDirection == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

                for (int i = 1; i < 6; i++)
                {
                    Color imageColor = Color.LimeGreen * (0.35f / i) * npc.Opacity;
                    spriteBatch.Draw(texture, npc.oldPos[i] + new Vector2(npc.width, npc.height) / 2f - Main.screenPosition, npc.frame, imageColor, npc.rotation, origin, npc.scale, effects, 0f);
                }
            }

            // 7. Draw local green glow aura if active in higher phases
            if (phase >= 2 && auraDrawAlpha > 0.01f)
            {
                Texture2D texture = TextureAssets.Npc[npc.type].Value;
                Vector2 origin = new Vector2(texture.Width / 2, texture.Height / Main.npcFrameCount[npc.type] / 2);
                SpriteEffects effects = npc.spriteDirection == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                
                for (int i = 0; i < 4; i++)
                {
                    Vector2 offset = (i * PiOver2).ToRotationVector2() * 6f;
                    spriteBatch.Draw(texture, npc.Center + offset - Main.screenPosition, npc.frame, Color.Lime * auraDrawAlpha, npc.rotation, origin, npc.scale, effects, 0f);
                }
            }

            // Renders standard texture on top
            return true;
        }

        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            // Draw Calamity Eye glow masks
            int phase = (int)npc.ai[0];
            if (phase >= 2)
            {
                Texture2D eyeTexture = ModContent.Request<Texture2D>("CalamityMod/NPCs/OldDuke/OldDukeGlow").Value;
                Vector2 origin = new Vector2(TextureAssets.Npc[npc.type].Value.Width / 2, TextureAssets.Npc[npc.type].Value.Height / Main.npcFrameCount[npc.type] / 2);
                SpriteEffects effects = npc.spriteDirection == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                
                Color eyeColor = Color.Yellow * 0.95f * npc.Opacity;
                if (isExhausted)
                {
                    eyeColor = Color.Red * 0.5f; // Eyes dim when fatigued
                }

                spriteBatch.Draw(eyeTexture, npc.Center - Main.screenPosition, npc.frame, eyeColor, npc.rotation, origin, npc.scale, effects, 0f);
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
        /// Drawing helper for rendering circular boundary lines.
        /// </summary>
        private void DrawMaelstromBoundary(SpriteBatch spriteBatch, Vector2 center, float alpha)
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

                // Alternating colors to resemble swirling toxic maelstrom clouds
                Color lineColor = (i % 2 == 0) ? Color.Lime * alpha * 0.8f : Color.DarkGreen * alpha * 0.4f;
                DrawTelegraphLine(spriteBatch, prevPoint, nextPoint, lineColor, 6f);
                prevPoint = nextPoint;
            }
        }

        /// <summary>
        /// Drawing helper for rendering radioactive core pulse warning indicators.
        /// </summary>
        private void DrawRadioactivePulse(SpriteBatch spriteBatch, Vector2 center, float radius, Color color)
        {
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            if (pixel == null) return;

            int segments = 72;
            Vector2 prevPoint = center + new Vector2(radius, 0f);

            for (int i = 1; i <= segments; i++)
            {
                float angle = (i / (float)segments) * TwoPi;
                Vector2 nextPoint = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;

                DrawTelegraphLine(spriteBatch, prevPoint, nextPoint, color, 8f);
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
            
            if (packetData.AttackState == (int)AttackState.MaelstromRageDash || packetData.AttackState == (int)AttackState.NuclearDetonation)
            {
                desperationCenter = packetData.TargetCenter;
            }
        }
        #endregion
    }
}
