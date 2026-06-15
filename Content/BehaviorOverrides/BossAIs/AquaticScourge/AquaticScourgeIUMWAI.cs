// =====================================================================================================================
// AQUATIC SCOURGE - CUSTOM BEHAVIOR OVERRIDE (IUMW MODE)
// =====================================================================================================================
// DESIGN PHILOSOPHY:
// The Aquatic Scourge is a mutated, colossal sea worm dwelling in the Sulphurous Sea. This file completely overrides the
// head segment's update loop in PreAI, returning false to suppress vanilla and Calamity movement and state loops.
// The body and tail segments automatically follow the head via Terraria's native worm-following physics, avoiding segment
// desync bugs while allowing us to implement a highly customized, 1500+ line state machine.
//
// LORE & SETTING CONTEXT:
// Once a peaceful desert worm akin to the Desert Scourge, this creature migrated to the Sulphurous Sea as the deserts
// dried up. The highly acidic, toxic wastewater mutated its biology, replacing its dried husks with venomous spikes
// and giving it the ability to breathe toxic gas and project acid vortexes. In IUMW Mode, this mutation is pushed to its
// limit: it commands the oceans themselves, creating massive horizontal sweep tornadoes and boiling the water into
// radioactive zones.
//
// FIGHT MECHANICS & FLOW:
// - Phase 1 (100% - 67% HP) - Sulphur Scourge:
//   * Introduction Spawn: Descends from a toxic storm, roaring and generating localized acidic explosions.
//   * Bubble Spin: Circles the player in a fast circular orbit, releasing waves of expanding toxic bubbles that float inwards.
//   * Acid Spit Lunge: High-speed predictive charges that spray acidic droplets in a downward fan shape.
//   * Radiation Pulse: Coils up, charging a radioactive warning zone. When charged, fires a radial burst of energy rays.
// - Phase 2 (67% - 25% HP) - Sulphur Storm:
//   * Sulphur Gas Breath: Lunges rapidly while breathing a wide cone of green toxic gas clouds that linger in the air.
//   * Perpendicular Spike Rain: Flies in a sinusoidal path above the player, firing spikes perpendicularly from its body segments.
//   * Sulphur Meteor Spit: Launches heavy sulfuric meteors that split into splashing rain droplets.
//   * Acid Sinkholes: Summons localized vortex portals at the bottom of the screen that pull the player downwards.
//   * Coiled Shell Reflection: Coils tightly in defense, gaining 99% DR, deflecting projectiles, and emitting spiral fire.
// - Phase 3 (25% - 0% HP) - Toxic Typhoon (Desperation):
//   * Typhoon Arena: Teleports, creating a massive 660f Acid Typhoon vortex. Crossing it inflicts severe tick damage.
//   * Vortex Storm Dash: Circles the boundary at extreme speed, launching bubble streams, while horizontal tornadoes sweep.
//   * Shield Break: Invulnerable during the 15-second survival window, after which the boss becomes vulnerable.
//   * Bespoke Death Animation: Explodes segment-by-segment from tail to head before scattering custom water splash particles.
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
using CalamityScourge = CalamityMod.NPCs.AquaticScourge.AquaticScourgeHead;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.AquaticScourge
{
    internal sealed class AquaticScourgeIUMWAI : IUMWBossAI
    {
        #region Constants & Configuration
        // NPC Identifiers
        public override int NPCType => ModContent.NPCType<CalamityScourge>();
        public override string BossName => "Aquatic Scourge";

        // Phase Thresholds & Settings
        public override float[] PhaseLifeRatios => new[] { 0.67f, 0.25f };
        public override int AttackCycleLength => 160;
        public override float MotionIntensity => 1.15f;
        public override Color DebugColor => new(0, 230, 160);

        // Sound Registers
        public static readonly SoundStyle RoarSound = new("Terraria/Sounds/NPC_Killed_14") { Volume = 1.25f, Pitch = -0.15f };
        public static readonly SoundStyle SplashSound = new("Terraria/Sounds/Item_19") { Volume = 0.95f, Pitch = -0.3f };
        public static readonly SoundStyle BubbleSound = new("Terraria/Sounds/Item_85") { Volume = 0.85f, Pitch = 0.15f };
        public static readonly SoundStyle LaserSound = new("Terraria/Sounds/Item_33") { Volume = 0.65f, Pitch = -0.2f };
        public static readonly SoundStyle TyphoonSound = new("Terraria/Sounds/Item_122") { Volume = 1.1f, Pitch = -0.25f };
        public static readonly SoundStyle DeflectSound = new("Terraria/Sounds/Item_150") { Volume = 0.8f, Pitch = 0.4f };

        // Math Constants
        private const float TwoPi = MathHelper.TwoPi;
        private const float Pi = MathHelper.Pi;
        private const float PiOver2 = MathHelper.PiOver2;
        private const float ArenaRadius = 660f;

        // Projectile Reference Keys
        private const string AcidBubbleProjName = "AcidBubble";
        private const string SulphurSpikeProjName = "SulphurSpike";
        private const string SulphuricCloudProjName = "SulphuricCloud";
        private const string SulphurTornadoProjName = "SulphurTornado";
        private const string AcidRainProjName = "FallingAcid";
        #endregion

        #region State Machine Enumeration
        public enum AttackState
        {
            IntroductionSpawn = 0,
            BubbleSpin = 1,
            AcidSpitLunge = 2,
            RadiationPulse = 3,
            GasBreath = 4,
            PerpendicularSpikeRain = 5,
            SulphurMeteorSpit = 6,
            AcidSinkholes = 7,
            CoiledShellReflect = 8,
            TyphoonTransition = 9,
            TyphoonSurvival = 10,
            ShieldBrokenDefeat = 11,
            DeathAnimation = 12,
            DespawnRetreat = 13
        }
        #endregion

        #region Local Fields
        // Radii and angles for drawing indicators
        private float pulseDrawRadius = 0f;
        private float pulseDrawAlpha = 0f;

        // Tornado offsets for desperation phase
        private readonly Vector2[] tornadoPositions = new Vector2[3];
        private readonly float[] tornadoDirections = new float[3];
        private int tornadoCount = 0;

        // Sinkhole position array
        private readonly Vector2[] sinkholePositions = new Vector2[3];
        private int sinkholeCount = 0;
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

            // Grant infinite flight to target players to avoid aquatic movement penalty
            foreach (Player player in Main.ActivePlayers)
            {
                if (player.dead || player.ghost || !npc.WithinRange(player.Center, 8000f))
                    continue;

                player.breath = player.breathMax;
                player.ignoreWater = true;
                player.wingTime = player.wingTimeMax;
                player.Calamity().infiniteFlight = true;
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

            // Intercept health for death animation trigger
            if (state != AttackState.DeathAnimation && state != AttackState.ShieldBrokenDefeat && npc.life <= 2)
            {
                npc.life = 2;
                TransitionToState(npc, AttackState.DeathAnimation);
                state = AttackState.DeathAnimation;
            }

            // Handle Phase Transitions
            CheckPhaseTransitions(npc, target, ref currentPhase, ref state, ref timer, ref stateTracker);

            // Set Damage Reduction Defaults
            if (state == AttackState.IntroductionSpawn || state == AttackState.TyphoonSurvival || state == AttackState.RadiationPulse || state == AttackState.DeathAnimation)
            {
                npc.Calamity().DR = 0.95f; // Shield mode
            }
            else if (state == AttackState.CoiledShellReflect)
            {
                npc.Calamity().DR = 0.99f; // Reflection armor shield
            }
            else
            {
                npc.Calamity().DR = 0.30f; // Normal mode
            }

            // Execute State Machine
            switch (state)
            {
                case AttackState.IntroductionSpawn:
                    DoAttack_IntroductionSpawn(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.BubbleSpin:
                    DoAttack_BubbleSpin(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.AcidSpitLunge:
                    DoAttack_AcidSpitLunge(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.RadiationPulse:
                    DoAttack_RadiationPulse(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.GasBreath:
                    DoAttack_GasBreath(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.PerpendicularSpikeRain:
                    DoAttack_PerpendicularSpikeRain(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.SulphurMeteorSpit:
                    DoAttack_SulphurMeteorSpit(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.AcidSinkholes:
                    DoAttack_AcidSinkholes(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.CoiledShellReflect:
                    DoAttack_CoiledShellReflect(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.TyphoonTransition:
                    DoAttack_TyphoonTransition(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.TyphoonSurvival:
                    DoAttack_TyphoonSurvival(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.ShieldBrokenDefeat:
                    DoAttack_ShieldBrokenDefeat(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.DeathAnimation:
                    DoAttack_DeathAnimation(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.DespawnRetreat:
                    ExecuteDespawnAI(npc);
                    break;
            }

            // Rotation tracking and speed updates
            timer++;
            npc.rotation = npc.velocity.ToRotation() + PiOver2;
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

            // Trigger Phase 2 at < 67% HP
            if (phase == 1 && lifeRatio < 0.67f)
            {
                phase = 2;
                BroadcastMessage("The Aquatic Scourge's spines glow green! The sea enrages.", DebugColor);
                TransitionToState(npc, AttackState.GasBreath);
                return;
            }

            // Trigger Phase 3 (Desperation Phase) at < 25% HP
            if (phase < 3 && lifeRatio < 0.25f)
            {
                phase = 3;
                BroadcastMessage("The ocean undergoes a violent toxic eruption!", DebugColor);
                TransitionToState(npc, AttackState.TyphoonTransition);
                return;
            }
        }

        private void TransitionToState(NPC npc, AttackState newState)
        {
            npc.ai[1] = (float)newState;
            npc.ai[2] = 0f;
            npc.ai[3] = 0f;
            npc.netUpdate = true;

            // Reset local drawing variables
            pulseDrawRadius = 0f;
            pulseDrawAlpha = 0f;
            tornadoCount = 0;
            sinkholeCount = 0;
        }
        #endregion

        #region Attack Implementations

        /// <summary>
        /// Introduction Spawn: Splashes down, drawing in green toxic sparks, then roars.
        /// </summary>
        private void DoAttack_IntroductionSpawn(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.damage = 0;
            npc.dontTakeDamage = true;
            npc.velocity *= 0.95f;

            if (timer == 1)
            {
                npc.Center = target.Center - new Vector2(0f, 600f);
                SoundEngine.PlaySound(SplashSound, npc.Center);
                npc.netUpdate = true;
            }

            // Falling motion
            if (timer < 60)
            {
                npc.velocity.Y = 8f;
                // Green dust trails
                for (int i = 0; i < 3; i++)
                {
                    Dust d = Dust.NewDustPerfect(npc.Center + Main.rand.NextVector2Circular(40f, 40f), DustID.GreenFairy, Vector2.Zero, 100, default, 1.4f);
                    d.noGravity = true;
                }
            }
            else if (timer == 60)
            {
                npc.velocity = Vector2.Zero;
                PlayRoarWithScreenShake(npc, 15f);
                SpawnExplosionDust(npc.Center, 60, 12f, DustID.GreenFairy);
                npc.netUpdate = true;
            }

            if (timer >= 120)
            {
                npc.dontTakeDamage = false;
                TransitionToState(npc, AttackState.BubbleSpin);
            }
        }

        /// <summary>
        /// Phase 1 Attack: Circles player at high speed, releasing expanding rings of bubbles.
        /// </summary>
        private void DoAttack_BubbleSpin(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            float orbitRadius = 350f;
            float angularSpeed = phase == 1 ? 0.045f : 0.055f;
            float angle = timer * angularSpeed;

            // Circular target position
            Vector2 orbitCenter = target.Center;
            Vector2 dest = orbitCenter + angle.ToRotationVector2() * orbitRadius;
            
            npc.velocity = (dest - npc.Center) * 0.15f;

            // Periodically fire expanding bubble rings inwards
            int fireRate = phase == 1 ? 25 : 18;
            if (timer % fireRate == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SoundEngine.PlaySound(BubbleSound, npc.Center);
                int damage = ScaleBossDamage(npc, 95);
                int projType = GetCalamityProjectileType(AcidBubbleProjName);

                // Spawn 4 bubbles flying inward
                for (int i = 0; i < 4; i++)
                {
                    float offsetAngle = angle + (i * TwoPi / 4f);
                    Vector2 spawnPos = npc.Center;
                    Vector2 dir = SafeNormalize(target.Center - spawnPos, Vector2.Zero);
                    
                    Projectile.NewProjectile(npc.GetSource_FromAI(), spawnPos, dir.RotatedBy(offsetAngle * 0.1f) * 4.5f, projType, damage, 1f, Main.myPlayer);
                }
            }

            if (timer >= 260)
            {
                TransitionToState(npc, AttackState.AcidSpitLunge);
            }
        }

        /// <summary>
        /// Phase 1 Attack: High-speed dashes spraying acidic droplets downward.
        /// </summary>
        private void DoAttack_AcidSpitLunge(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            const float ChargeTime = 45f;
            const float PostChargeTime = 25f;
            float cycleTime = ChargeTime + PostChargeTime;

            float relativeTimer = timer % cycleTime;
            int chargeCount = (int)stateTracker;

            if (chargeCount >= 3)
            {
                TransitionToState(npc, AttackState.RadiationPulse);
                return;
            }

            if (relativeTimer < ChargeTime)
            {
                // Align ahead of player predictive offset
                Vector2 targetFuture = target.Center + target.velocity * 10f;
                Vector2 hoverOffset = SafeNormalize(npc.Center - targetFuture, -Vector2.UnitY) * 400f;
                Vector2 dest = targetFuture + hoverOffset;
                
                npc.velocity = Vector2.Lerp(npc.velocity, (dest - npc.Center) * 0.08f, 0.12f);

                // Charging dust
                if (Main.rand.NextBool(5))
                {
                    Dust d = Dust.NewDustPerfect(npc.Center, DustID.CursedTorch, Main.rand.NextVector2Circular(2f, 2f), 100, default, 1.2f);
                    d.noGravity = true;
                }
            }
            else if (relativeTimer == ChargeTime)
            {
                // Execute charge
                Vector2 targetFuture = target.Center + target.velocity * 6f;
                Vector2 chargeDir = SafeNormalize(targetFuture - npc.Center, Vector2.UnitY);
                float speed = phase == 1 ? 17f : 20f;
                npc.velocity = chargeDir * speed;

                PlayRoarWithScreenShake(npc, 8f);
                npc.netUpdate = true;
            }
            else
            {
                // Decelerate and spray acid spit downward
                npc.velocity *= 0.94f;

                if (timer % 5 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int damage = ScaleBossDamage(npc, 90);
                    int projType = GetCalamityProjectileType(SulphurSpikeProjName);
                    
                    // Spray fan of 3 spikes downwards
                    for (int i = -1; i <= 1; i++)
                    {
                        Vector2 speed = new Vector2(i * 2f, 7.5f);
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, speed, projType, damage, 1f, Main.myPlayer);
                    }
                }

                if (relativeTimer == cycleTime - 1)
                {
                    stateTracker += 1f;
                    npc.netUpdate = true;
                }
            }
        }

        /// <summary>
        /// Phase 1 Attack: Coils up and channels a radioactive pulse indicator, releasing a radial burst.
        /// </summary>
        private void DoAttack_RadiationPulse(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            npc.velocity *= 0.9f;

            const float ChargeMax = 80f;
            const float MaxRadius = 380f;

            if (timer < ChargeMax)
            {
                // Scale indicator size and alpha
                pulseDrawRadius = MathHelper.Lerp(0f, MaxRadius, timer / ChargeMax);
                pulseDrawAlpha = timer / ChargeMax;

                // Dust charging
                if (Main.rand.NextBool(3))
                {
                    float angle = Main.rand.NextFloat(TwoPi);
                    Vector2 offset = angle.ToRotationVector2() * pulseDrawRadius;
                    Dust d = Dust.NewDustPerfect(npc.Center + offset, DustID.CursedTorch, Vector2.Zero, 100, default, 1.3f);
                    d.noGravity = true;
                }
            }
            else if (timer == ChargeMax)
            {
                // Erupt and release radial spikes
                SoundEngine.PlaySound(LaserSound, npc.Center);
                SpawnExplosionDust(npc.Center, 50, 12f, DustID.GreenFairy);

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int damage = ScaleBossDamage(npc, 105);
                    int projType = GetCalamityProjectileType(SulphurSpikeProjName);
                    int spikes = phase == 1 ? 16 : 20;

                    for (int i = 0; i < spikes; i++)
                    {
                        float angle = i * TwoPi / spikes;
                        Vector2 dir = angle.ToRotationVector2();
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, dir * 8f, projType, damage, 1f, Main.myPlayer);
                    }
                }

                pulseDrawRadius = 0f;
                pulseDrawAlpha = 0f;
                npc.netUpdate = true;
            }
            else if (timer >= ChargeMax + 40f)
            {
                // Transition state
                if (phase == 1)
                {
                    TransitionToState(npc, AttackState.BubbleSpin);
                }
                else
                {
                    TransitionToState(npc, AttackState.GasBreath);
                }
            }
        }

        /// <summary>
        /// Phase 2 Attack: Fast lunges breathing lingering green toxic gas clouds.
        /// </summary>
        private void DoAttack_GasBreath(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            const float PrepTime = 50f;
            const float ChargeDuration = 60f;
            float totalTime = PrepTime + ChargeDuration;

            float relativeTimer = timer % totalTime;
            int count = (int)stateTracker;

            if (count >= 2)
            {
                TransitionToState(npc, AttackState.PerpendicularSpikeRain);
                return;
            }

            if (relativeTimer < PrepTime)
            {
                // Position above and slightly to the side of the player
                Vector2 dest = target.Center + new Vector2(count == 0 ? -380f : 380f, -250f);
                npc.velocity = Vector2.Lerp(npc.velocity, (dest - npc.Center) * 0.09f, 0.12f);
            }
            else if (relativeTimer == PrepTime)
            {
                // Lunge horizontally across the player's path
                Vector2 dir = count == 0 ? Vector2.UnitX : -Vector2.UnitX;
                npc.velocity = dir * 16.5f;
                PlayRoarWithScreenShake(npc, 9f);
                npc.netUpdate = true;
            }
            else
            {
                // Release toxic gas clouds during the charge
                if (timer % 4 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int damage = ScaleBossDamage(npc, 100);
                    int projType = GetCalamityProjectileType(SulphuricCloudProjName);
                    // Spawn cloud slightly behind the head
                    Vector2 spawnPos = npc.Center - SafeNormalize(npc.velocity, Vector2.Zero) * 60f;
                    Projectile.NewProjectile(npc.GetSource_FromAI(), spawnPos, -npc.velocity * 0.1f, projType, damage, 1f, Main.myPlayer);
                }

                if (relativeTimer == totalTime - 1)
                {
                    stateTracker += 1f;
                    npc.netUpdate = true;
                }
            }
        }

        /// <summary>
        /// Phase 2 Attack: Flies in sinusoidal path, firing perpendicular spikes from segments.
        /// </summary>
        private void DoAttack_PerpendicularSpikeRain(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            // Sinusoidal movement path above player center
            Vector2 center = target.Center - new Vector2(0f, 320f);
            float speedX = phase == 2 ? 8.5f : 10.5f;
            
            // Move back and forth horizontally
            float posX = center.X + MathF.Sin(timer * 0.035f) * 550f;
            float posY = center.Y + MathF.Cos(timer * 0.07f) * 80f;

            Vector2 targetPos = new Vector2(posX, posY);
            npc.velocity = (targetPos - npc.Center) * 0.12f;

            // Spawning perpendicular spikes from segments
            // To simulate segments firing, we spawn spikes from positions offset behind the head
            if (timer % 15 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SoundEngine.PlaySound(BubbleSound, npc.Center);
                int damage = ScaleBossDamage(npc, 95);
                int projType = GetCalamityProjectileType(SulphurSpikeProjName);

                // Spawn 3 spikes along the scourges length
                for (int i = 1; i <= 3; i++)
                {
                    Vector2 segmentOffset = -SafeNormalize(npc.velocity, Vector2.UnitX) * (i * 120f);
                    Vector2 spawnPos = npc.Center + segmentOffset;

                    // Direction is perpendicular (90 degrees to motion)
                    Vector2 perpDir = SafeNormalize(npc.velocity, Vector2.UnitX).RotatedBy(PiOver2);
                    
                    // Downward-facing perpendicular lunge spikes
                    if (perpDir.Y < 0f) perpDir = -perpDir;

                    Projectile.NewProjectile(npc.GetSource_FromAI(), spawnPos, perpDir * 8f, projType, damage, 1f, Main.myPlayer);
                }
            }

            if (timer >= 280)
            {
                TransitionToState(npc, AttackState.SulphurMeteorSpit);
            }
        }

        /// <summary>
        /// Phase 2 Attack (New): Spits massive acid meteors that split into splashing rain droplets.
        /// </summary>
        private void DoAttack_SulphurMeteorSpit(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            // Hover directly above the player
            Vector2 hoverDest = target.Center + new Vector2(0f, -340f);
            npc.velocity = Vector2.Lerp(npc.velocity, (hoverDest - npc.Center) * 0.06f, 0.1f);

            // Periodically spit heavy acid meteors
            int interval = phase == 2 ? 45 : 35;
            if (timer % interval == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SoundEngine.PlaySound(SplashSound, npc.Center);
                int damage = ScaleBossDamage(npc, 110);
                int projType = GetCalamityProjectileType(AcidRainProjName);

                // Spits 2 meteors with diagonal offsets downwards
                for (int i = -1; i <= 1; i += 2)
                {
                    Vector2 speed = new Vector2(i * 3.5f, 6.5f);
                    int p = Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, speed, projType, damage, 1f, Main.myPlayer);
                    if (p != Main.maxProjectiles)
                    {
                        Main.projectile[p].scale = 1.6f; // Make it look like a large meteor
                        Main.projectile[p].netUpdate = true;
                    }
                }
            }

            if (timer >= 220)
            {
                TransitionToState(npc, AttackState.AcidSinkholes);
            }
        }

        /// <summary>
        /// Phase 2 Attack (New): Summons localized vortex portals at the bottom of the screen.
        /// They pull the player downwards while spouting sulfuric gas clouds.
        /// </summary>
        private void DoAttack_AcidSinkholes(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            // Hover in place above player center
            Vector2 dest = target.Center + new Vector2(0f, -280f);
            npc.velocity = Vector2.Lerp(npc.velocity, (dest - npc.Center) * 0.08f, 0.12f);

            if (timer == 1)
            {
                sinkholeCount = 3;
                float spacing = 280f;
                // Generate 3 sinkholes along the bottom floor height
                for (int i = 0; i < 3; i++)
                {
                    sinkholePositions[i] = target.Center + new Vector2((i - 1) * spacing, 350f);
                }
                SoundEngine.PlaySound(SplashSound, npc.Center);
                npc.netUpdate = true;
            }

            // Apply downward gravitational pull when player is above a sinkhole
            for (int i = 0; i < sinkholeCount; i++)
            {
                Vector2 sinkhole = sinkholePositions[i];
                float distH = Math.Abs(target.Center.X - sinkhole.X);
                float distV = target.Center.Y - sinkhole.Y; // Distance vertical

                // If player is within horizontal range and above the sinkhole
                if (distH < 220f && distV < 0f && distV > -500f)
                {
                    float pullFactor = MathHelper.Clamp(400f / (Math.Abs(distV) + 50f), 0.5f, 4.5f);
                    target.velocity.Y += pullFactor; // Pull down

                    // Spawning green rising bubbles from the sinkholes
                    if (timer % 8 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        int damage = ScaleBossDamage(npc, 95);
                        int projType = GetCalamityProjectileType(AcidBubbleProjName);
                        Vector2 speed = -Vector2.UnitY * Main.rand.NextFloat(4.5f, 6.5f);
                        Projectile.NewProjectile(npc.GetSource_FromAI(), sinkhole + Main.rand.NextVector2Circular(20f, 20f), speed, projType, damage, 1f, Main.myPlayer);
                    }
                }

                // Particle visual effects for sinkholes
                if (Main.rand.NextBool(4))
                {
                    Dust d = Dust.NewDustPerfect(sinkhole + Main.rand.NextVector2Circular(40f, 10f), DustID.CursedTorch, -Vector2.UnitY * 2f, 100, default, 1.3f);
                    d.noGravity = true;
                }
            }

            if (timer >= 240)
            {
                TransitionToState(npc, AttackState.CoiledShellReflect);
            }
        }

        /// <summary>
        /// Phase 2 Attack (New): Coils tightly in a defensive ring, reflecting projectiles and firing spiral sparks.
        /// </summary>
        private void DoAttack_CoiledShellReflect(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            // Zero speed, coil in center
            npc.velocity *= 0.85f;

            if (timer == 1)
            {
                PlayRoarWithScreenShake(npc, 10f);
                npc.netUpdate = true;
            }

            // Coil particles visual
            float spinAngle = timer * 0.12f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 offset = (spinAngle + i * PiOver2).ToRotationVector2() * 80f;
                Dust d = Dust.NewDustPerfect(npc.Center + offset, DustID.GreenFairy, Vector2.Zero, 100, default, 1.4f);
                d.noGravity = true;
            }

            // Deflect player projectiles in range
            foreach (Projectile p in Main.ActiveProjectiles)
            {
                if (p.active && !p.hostile && p.friendly && Vector2.Distance(p.Center, npc.Center) < 140f)
                {
                    // Bounce projectile backwards and make it hostile
                    p.velocity = -p.velocity * 0.9f;
                    p.friendly = false;
                    p.hostile = true;
                    p.damage = ScaleBossDamage(npc, 75);
                    p.netUpdate = true;

                    SoundEngine.PlaySound(DeflectSound, p.Center);
                    
                    // Deflection dust
                    for (int i = 0; i < 8; i++)
                    {
                        Dust d = Dust.NewDustPerfect(p.Center, DustID.CursedTorch, Main.rand.NextVector2Circular(3f, 3f), 100, default, 1.2f);
                        d.noGravity = true;
                    }
                }
            }

            // Spiral sparks pattern
            int interval = phase == 2 ? 8 : 6;
            if (timer % interval == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                float fireAngle = timer * 0.08f;
                int damage = ScaleBossDamage(npc, 95);
                int projType = GetCalamityProjectileType(SulphurSpikeProjName);

                for (int i = 0; i < 3; i++)
                {
                    float angle = fireAngle + (i * TwoPi / 3f);
                    Vector2 dir = angle.ToRotationVector2();
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, dir * 7f, projType, damage, 1f, Main.myPlayer);
                }
            }

            if (timer >= 200)
            {
                TransitionToState(npc, AttackState.BubbleSpin);
            }
        }

        /// <summary>
        /// Phase 3 Desperation Transition: Teleports to center, charging typhoon.
        /// </summary>
        private void DoAttack_TyphoonTransition(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.velocity *= 0.9f;
            npc.dontTakeDamage = true;

            if (timer == 1)
            {
                TeleportToPosition(npc, target.Center - new Vector2(0f, 150f));
                SoundEngine.PlaySound(TyphoonSound, npc.Center);
                npc.netUpdate = true;
            }

            // Green dust spiral windup
            if (timer < 100)
            {
                float angle = timer * 0.15f;
                float radius = ArenaRadius * (1f - (timer / 100f));
                for (int i = 0; i < 6; i++)
                {
                    Vector2 offset = (angle + (i * TwoPi / 6f)).ToRotationVector2() * radius;
                    Dust d = Dust.NewDustPerfect(npc.Center + offset, DustID.CursedTorch, Vector2.Zero, 100, default, 1.4f);
                    d.noGravity = true;
                }
            }

            if (timer >= 120)
            {
                TransitionToState(npc, AttackState.TyphoonSurvival);
            }
        }

        /// <summary>
        /// Phase 3 Desperation Survival: Locks player in 660f toxic typhoon ring,
        /// circles boundaries and sweeps horizontal tornadoes.
        /// </summary>
        private void DoAttack_TyphoonSurvival(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.dontTakeDamage = true;

            // Restrict player position to Arena Boundary
            float playerDist = Vector2.Distance(target.Center, npc.Center);
            if (playerDist > ArenaRadius)
            {
                Vector2 pull = SafeNormalize(npc.Center - target.Center, Vector2.Zero) * 6.5f;
                target.velocity += pull;

                if (Main.rand.NextBool(3))
                {
                    target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(NPCType), ScaleBossDamage(npc, 130), 0);
                }

                // Acid dust sparks around player
                for (int i = 0; i < 3; i++)
                {
                    Dust d = Dust.NewDustPerfect(target.Center + Main.rand.NextVector2Circular(20f, 20f), DustID.CursedTorch, Vector2.Zero, 100, default, 1.3f);
                    d.noGravity = true;
                }
            }

            // Scourge circles the boundary at high speed
            float orbitRadius = ArenaRadius + 80f;
            float angularSpeed = 0.065f;
            float angle = timer * angularSpeed;
            Vector2 dest = npc.Center + angle.ToRotationVector2() * orbitRadius;
            npc.velocity = (dest - npc.Center) * 0.18f;

            // Spawn horizontal sweeping tornadoes
            if (timer == 1)
            {
                tornadoCount = 2;
                // Tornado 0 sweeps Left to Right
                tornadoPositions[0] = npc.Center + new Vector2(-ArenaRadius + 50f, 0f);
                tornadoDirections[0] = 1f;

                // Tornado 1 sweeps Right to Left
                tornadoPositions[1] = npc.Center + new Vector2(ArenaRadius - 50f, 0f);
                tornadoDirections[1] = -1f;

                SoundEngine.PlaySound(TyphoonSound, npc.Center);
            }

            // Update Tornado positions and spawn actual Calamity Tornado projectiles
            for (int i = 0; i < tornadoCount; i++)
            {
                tornadoPositions[i].X += tornadoDirections[i] * 5.5f;

                // Bounce at boundaries
                if (Math.Abs(tornadoPositions[i].X - npc.Center.X) > ArenaRadius - 80f)
                {
                    tornadoDirections[i] = -tornadoDirections[i];
                }

                // Periodically spawn tornado projectile to damage player
                if (timer % 20 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int damage = ScaleBossDamage(npc, 115);
                    int projType = GetCalamityProjectileType(SulphurTornadoProjName);
                    // Spawn stationary tornado projectile that tracks player heights
                    Projectile.NewProjectile(npc.GetSource_FromAI(), tornadoPositions[i], Vector2.Zero, projType, damage, 1f, Main.myPlayer);
                }

                // Visual dust for tornado path
                if (Main.rand.NextBool(2))
                {
                    Vector2 dustPos = tornadoPositions[i] + new Vector2(0f, Main.rand.NextFloat(-200f, 200f));
                    Dust d = Dust.NewDustPerfect(dustPos, DustID.CursedTorch, Vector2.Zero, 100, default, 1.5f);
                    d.noGravity = true;
                }
            }

            // Continuous stream of acid bubbles from head
            if (timer % 8 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                int damage = ScaleBossDamage(npc, 95);
                int projType = GetCalamityProjectileType(AcidBubbleProjName);
                Vector2 dir = SafeNormalize(target.Center - npc.Center, Vector2.Zero);
                Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, dir * 5.5f, projType, damage, 1f, Main.myPlayer);
            }

            // Screen shake
            if (timer % 30 == 0)
            {
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 3f;
            }

            // Survival check for 15 seconds (900 ticks)
            if (timer >= 900)
            {
                TransitionToState(npc, AttackState.ShieldBrokenDefeat);
            }
        }

        /// <summary>
        /// Shield Broken Defeat State: DR falls to 0%, sits inactive, smoking.
        /// </summary>
        private void DoAttack_ShieldBrokenDefeat(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.velocity *= 0.95f;
            npc.damage = 0;
            npc.dontTakeDamage = false;

            if (timer == 1)
            {
                PlayRoarWithScreenShake(npc, 14f);
                SpawnExplosionDust(npc.Center, 60, 12f, DustID.GreenFairy);
                npc.netUpdate = true;
            }

            // Constant slow release of steam/smoke particles
            if (Main.rand.NextBool(3))
            {
                Dust d = Dust.NewDustPerfect(npc.Center + Main.rand.NextVector2Circular(35f, 35f), DustID.Smoke, -Vector2.UnitY * Main.rand.NextFloat(1.5f, 3f), 80, Color.LimeGreen, 1.5f);
                d.noGravity = true;
            }
        }

        /// <summary>
        /// Bespoke Death Animation: Explodes segment-by-segment from tail to head, shaking the screen.
        /// </summary>
        private void DoAttack_DeathAnimation(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.velocity *= 0.9f;
            npc.damage = 0;
            npc.dontTakeDamage = true;

            int bodyType = ModContent.NPCType<CalamityMod.NPCs.AquaticScourge.AquaticScourgeBody>();
            int bodyTypeAlt = ModContent.NPCType<CalamityMod.NPCs.AquaticScourge.AquaticScourgeBodyAlt>();
            int tailType = ModContent.NPCType<CalamityMod.NPCs.AquaticScourge.AquaticScourgeTail>();

            // Coils tight in center
            if (timer == 1)
            {
                PlayRoarWithScreenShake(npc, 18f);
                npc.netUpdate = true;
            }

            // Sequentially detonate segments starting from the tail
            if (timer >= 30 && timer <= 180 && timer % 4 == 0)
            {
                int step = (int)((timer - 30) / 4);
                
                // Scan for segments
                int currentSegment = 0;
                foreach (NPC n in Main.npc)
                {
                    if (n.active && n.realLife == npc.whoAmI && (n.type == bodyType || n.type == bodyTypeAlt || n.type == tailType))
                    {
                        if (currentSegment == step)
                        {
                            // Detonate this segment
                            SpawnSegmentExplosion(n);
                            n.active = false;
                            n.netUpdate = true;
                            break;
                        }
                        currentSegment++;
                    }
                }
            }

            // Final head explosion
            if (timer >= 190)
            {
                PlayRoarWithScreenShake(npc, 25f);
                SpawnExplosionDust(npc.Center, 80, 16f, DustID.GreenFairy);
                
                // Spawn splash water droplets
                if (Main.netMode != NetmodeID.Server)
                {
                    for (int i = 0; i < 40; i++)
                    {
                        Dust d = Dust.NewDustPerfect(npc.Center, DustID.Water, Main.rand.NextVector2Circular(10f, 10f), 80, Color.LimeGreen, 1.6f);
                        d.noGravity = false;
                    }
                }

                // Scatter gores
                if (Main.netMode != NetmodeID.Server)
                {
                    for (int i = 1; i <= 3; i++)
                    {
                        Gore.NewGore(npc.GetSource_Death(), npc.Center, Main.rand.NextVector2Circular(5f, 5f), Main.rand.Next(61, 64), 1f);
                    }
                }

                npc.life = 0;
                npc.HitEffect();
                npc.active = false;
                npc.netUpdate = true;
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
        /// Override PreDraw to draw custom indicators, telegraph lines,
        /// and desperation typhoon boundaries.
        /// </summary>
        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            AttackState state = (AttackState)(int)npc.ai[1];
            float timer = npc.ai[2];

            // 1. Draw Dash Indicator during Acid Spit Lunge
            if (state == AttackState.AcidSpitLunge)
            {
                const float ChargeTime = 45f;
                float relativeTimer = timer % 70f;

                if (relativeTimer < ChargeTime)
                {
                    Player target = Main.player[npc.target];
                    Vector2 targetFuture = target.Center + target.velocity * 6f;
                    DrawTelegraphLine(spriteBatch, npc.Center, targetFuture, DebugColor * (relativeTimer / ChargeTime) * 0.8f, 5f);
                }
            }

            // 2. Draw Radiation Pulse charging field
            if (state == AttackState.RadiationPulse && pulseDrawAlpha > 0.01f)
            {
                DrawShieldBubble(spriteBatch, npc.Center, pulseDrawRadius, Color.LimeGreen * pulseDrawAlpha * 0.45f);
            }

            // 3. Draw Sinkhole circles during Acid Sinkholes state
            if (state == AttackState.AcidSinkholes)
            {
                for (int i = 0; i < sinkholeCount; i++)
                {
                    DrawShieldBubble(spriteBatch, sinkholePositions[i], 120f, Color.LimeGreen * 0.35f);
                }
            }

            // 4. Draw Event Horizon Boundary Circle (Desperation Typhoon)
            if (state == AttackState.TyphoonSurvival || state == AttackState.TyphoonTransition)
            {
                float alpha = state == AttackState.TyphoonTransition ? (timer / 120f) : 1f;
                DrawTyphoonBoundary(spriteBatch, npc.Center, alpha);
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

        private void DrawTyphoonBoundary(SpriteBatch spriteBatch, Vector2 center, float alpha)
        {
            Texture2D circleTex = TextureAssets.MagicPixel.Value;
            if (circleTex == null) return;

            int segments = 120;
            Color ringColor = Color.Lime * 0.6f * alpha;

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
            spriteBatch.Draw(circleTex, drawPos, null, new Color(20, 100, 40) * 0.22f * alpha, 0f, circleTex.Size() * 0.5f, scaleAmt, SpriteEffects.None, 0f);
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

        /// <summary>
        /// Spawns localized explosions at a segment position.
        /// </summary>
        private void SpawnSegmentExplosion(NPC segment)
        {
            if (Main.netMode == NetmodeID.Server) return;

            SoundEngine.PlaySound(SplashSound, segment.Center);
            SpawnExplosionDust(segment.Center, 20, 6f, DustID.GreenFairy);
            
            // Release green bubble particles
            for (int i = 0; i < 4; i++)
            {
                Dust d = Dust.NewDustPerfect(segment.Center, DustID.CursedTorch, Main.rand.NextVector2Circular(4f, 4f), 80, default, 1.3f);
                d.noGravity = true;
            }
        }

        /// <summary>
        /// Spawns a spray of toxic acid dust along a velocity trajectory.
        /// </summary>
        private void SpawnAcidDustSpray(Vector2 pos, Vector2 velocity, int count)
        {
            if (Main.netMode == NetmodeID.Server) return;

            for (int i = 0; i < count; i++)
            {
                Vector2 speed = velocity.RotatedByRandom(0.4f) * Main.rand.NextFloat(0.5f, 1.5f);
                Dust d = Dust.NewDustPerfect(pos, DustID.CursedTorch, speed, 100, default, Main.rand.NextFloat(1.1f, 1.6f));
                d.noGravity = true;
            }
        }

        /// <summary>
        /// Plays a boss roar sound and registers screenshake directly on the local player.
        /// </summary>
        private void PlayRoarWithScreenShake(NPC npc, float shakePower)
        {
            SoundEngine.PlaySound(RoarSound, npc.Center);
            if (Main.netMode != NetmodeID.Server)
            {
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = shakePower;
            }
        }

        /// <summary>
        /// Scale boss base damage values relative to Expert/Master difficulties.
        /// </summary>
        private int ScaleBossDamage(NPC npc, int baseDamage)
        {
            float scale = 1.0f;
            if (Main.expertMode) scale = 1.6f;
            if (Main.masterMode) scale = 2.4f;
            return (int)(baseDamage * scale);
        }

        /// <summary>
        /// Dynamically checks Calamity Mod namespaces for specific projectile indices.
        /// </summary>
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
            SpawnExplosionDust(npc.Center, 30, 8f, DustID.GreenFairy);
            npc.Center = destination;
            npc.velocity = Vector2.Zero;
            SpawnExplosionDust(npc.Center, 30, 8f, DustID.GreenFairy);
            SoundEngine.PlaySound(SplashSound, npc.Center);
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
            packet.Write((byte)6); // Type 6 packet for Aquatic Scourge
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
        /// Quartic easing in and out. Extremely aggressive start and end transitions.
        /// Used for high-tier phase transitions and speed lunges.
        /// </summary>
        /// <param name="t">Linear time progress between 0 and 1.</param>
        /// <returns>Eased time ratio.</returns>
        private static float EaseInOutQuart(float t)
        {
            return t < 0.5f ? 8f * t * t * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 4f) * 0.5f;
        }

        /// <summary>
        /// Exponential easing out. Extremely rapid start deceleration.
        /// Used to damp speed suddenly upon target arrival.
        /// </summary>
        /// <param name="t">Linear time progress between 0 and 1.</param>
        /// <returns>Eased time ratio.</returns>
        private static float EaseOutExpo(float t)
        {
            return t == 1f ? 1f : 1f - (float)Math.Pow(2f, -10f * t);
        }

        /// <summary>
        /// Bounce easing out. Creates a natural bouncing decay motion.
        /// Ideal for debris falling or sinkhole eruptions settling.
        /// </summary>
        /// <param name="t">Linear time progress between 0 and 1.</param>
        /// <returns>Eased time ratio.</returns>
        private static float EaseOutBounce(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;

            if (t < 1f / d1)
            {
                return n1 * t * t;
            }
            else if (t < 2f / d1)
            {
                t -= 1.5f / d1;
                return n1 * t * t + 0.75f;
            }
            else if (t < 2.5f / d1)
            {
                t -= 2.25f / d1;
                return n1 * t * t + 0.9375f;
            }
            else
            {
                t -= 2.625f / d1;
                return n1 * t * t + 0.984375f;
            }
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
        /// Elastic easing out. Creates a spring-like oscillating damping motion.
        /// Ideal for high-impact screen shake animations.
        /// </summary>
        /// <param name="t">Linear time progress between 0 and 1.</param>
        /// <returns>Eased time ratio.</returns>
        private static float EaseOutElastic(float t)
        {
            const float c4 = TwoPi / 3f;
            return t == 0f ? 0f : t == 1f ? 1f : (float)Math.Pow(2f, -10f * t) * MathF.Sin((t * 10f - 0.75f) * c4) + 1f;
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

        /// <summary>
        /// Bounce easing in. Accelerates from zero velocity with minor bouncing increments.
        /// </summary>
        /// <param name="t">Linear time progress between 0 and 1.</param>
        /// <returns>Eased time ratio.</returns>
        private static float EaseInBounce(float t)
        {
            return 1f - EaseOutBounce(1f - t);
        }

        /// <summary>
        /// Bounce easing in and out. Produces elastic bouncing transitions at both bounds.
        /// </summary>
        /// <param name="t">Linear time progress between 0 and 1.</param>
        /// <returns>Eased time ratio.</returns>
        private static float EaseInOutBounce(float t)
        {
            return t < 0.5f
                ? (1f - EaseOutBounce(1f - 2f * t)) * 0.5f
                : (1f + EaseOutBounce(2f * t - 1f)) * 0.5f;
        }

        /// <summary>
        /// Spawns a cluster of toxic splash bubble particles.
        /// Used during splash spawns and segment detonations.
        /// </summary>
        /// <param name="pos">Position to spawn bubbles.</param>
        /// <param name="count">Density of bubbles.</param>
        private void SpawnToxicSplashBubbles(Vector2 pos, int count)
        {
            if (Main.netMode == NetmodeID.Server) return;

            for (int i = 0; i < count; i++)
            {
                Dust d = Dust.NewDustPerfect(pos, DustID.CursedTorch, Main.rand.NextVector2Circular(5f, 5f), 100, default, Main.rand.NextFloat(1.2f, 1.8f));
                d.noGravity = true;
                d.velocity.Y -= Main.rand.NextFloat(1f, 3f);
            }
        }

        /// <summary>
        /// Broadcasts an attack notification alert to players with custom Mod color.
        /// </summary>
        /// <param name="message">The message to display.</param>
        private void AlertPlayerOfAttack(string message)
        {
            BroadcastMessage(message, DebugColor);
        }

        /// <summary>
        /// Calculates the position offset of a worm segment relative to the head.
        /// Used for calculating trailing lengths and segment alignment checks.
        /// </summary>
        /// <param name="npc">The head NPC instance.</param>
        /// <param name="segmentIndex">Index of segment to query.</param>
        /// <returns>A vector pointing from the head to the segment.</returns>
        private Vector2 GetSegmentPositionOffset(NPC npc, int segmentIndex)
        {
            float spacing = 84f; // Segment spacing distance
            Vector2 headDirection = -SafeNormalize(npc.velocity, Vector2.UnitX);
            return headDirection * (segmentIndex * spacing);
        }
        #endregion
    }
}
