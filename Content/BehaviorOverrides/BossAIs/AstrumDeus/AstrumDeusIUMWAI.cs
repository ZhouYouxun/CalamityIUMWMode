// =====================================================================================================================
// ASTRUM DEUS - CUSTOM BEHAVIOR OVERRIDE (IUMW MODE)
// =====================================================================================================================
// DESIGN PHILOSOPHY:
// Astrum Deus is the cosmic worm of the Astral Infection. This override replaces the default Calamity split AI
// with a single massive worm fight that scales across 4 custom phases, climaxing in an armor-shedding ceremony
// and a desperation Astral Nova Singularity.
//
// FIGHT PHASES & STATES:
// - Phase 1 (100% - 74% HP) - Astral Awakening:
//   * Spawn Animation: Materializes at the cosmic beacon, descending from the stars in a spiral path.
//   * Warp Charge: Fades out, teleports in anticipated direction, charges at player with radial comet fans.
//   * Astral Meteor Shower: Vertically rises into the heavens, slams down, raining comets from the sky.
// - Phase 2 (74% - 48% HP) - Celestial Alignment:
//   * Vortex Lemniscate: Flies in a parametric figure-8 Bernoulli Lemniscate, creating twin rotating vortices.
//   * Rubble From Below: Dive-bombs beneath the screen, rushes upward, launching gravity rubble at the apex.
//   * Plasma & Crystals: Hovers near target firing plasma, body segments shoot homing crystal shards.
// - Phase 3 (48% - 20% HP) - Constellation Outburst:
//   * Infected Star Weave: Orbit circles around a growing star, hurling it, segments shoot crossing laser grids.
//   * Dark God's Outburst: Anchors a central black hole pulling player, surrounded by dark stars firing lasers.
//   * Constellation Explosions: Spawns diagonal/sinusoidal star lines detonating into large plasma bursts.
// - Phase 4 (20% - 0% HP) - Exposed Singularity (Desperation):
//   * Armor Shed Ceremony: Flies high, sheds shell with a massive cosmic particle blast, exposed glowing state.
//   * Astral Nova Singularity: Locks player inside a 660f boundary circle. Boss anchors at center, channeling 
//     expanding crystal rings, crossing laser grids, falling comets, and high-speed warp charges.
//
// MATH & VECTOR PHYSICS:
// - Segment Trailing: Custom slithering physics for Body and Tail segments by reading ahead segments and wrapping angles.
// - Bernoulli Lemniscate: Parametric calculation: x = r*cos(t)/(1+sin^2(t)), y = r*sin(t)*cos(t)/(1+sin^2(t)).
// - Teleport Easing: Smooth quadratic and cubic easing formulas for boss fade-outs and fade-ins.
// =====================================================================================================================

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using CalamityIUMWMode.Core.Systems;
using CalamityMod;
using CalamityMod.Events;
using CalamityMod.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using CalamityAstrumDeus = CalamityMod.NPCs.AstrumDeus.AstrumDeusHead;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.AstrumDeus
{
    internal sealed class AstrumDeusIUMWAI : IUMWBossAI
    {
        #region Constants & Configuration
        public override int NPCType => ModContent.NPCType<CalamityAstrumDeus>();
        public override string BossName => "Astrum Deus";

        // Phase Thresholds
        public override float[] PhaseLifeRatios => new[] { 0.74f, 0.48f, 0.20f };
        public override int AttackCycleLength => 150;
        public override float MotionIntensity => 1.15f;
        public override Color DebugColor => new(255, 118, 226);

        // Sound Registers (Dynamic/Static Calamity Mod sound lookups)
        public static readonly SoundStyle SpawnSound = new("CalamityMod/Sounds/Custom/AstrumDeus/AstrumDeusSpawn");
        public static readonly SoundStyle LaserSound = new("CalamityMod/Sounds/Custom/AstrumDeus/AstrumDeusLaser") { Volume = 0.35f };
        public static readonly SoundStyle GodRaySound = new("CalamityMod/Sounds/Custom/AstrumDeus/AstrumDeusGodRay") { Volume = 0.4f };
        public static readonly SoundStyle MineSound = new("CalamityMod/Sounds/Custom/AstrumDeus/AstrumDeusMine") { Volume = 0.4f };
        public static readonly SoundStyle SplitSound = new("CalamityMod/Sounds/Custom/AstrumDeus/AstrumDeusSplit");
        public static readonly SoundStyle HitSound = new("CalamityMod/Sounds/NPCHit/AstrumDeusHit", 2) { Volume = 0.7f };
        public static readonly SoundStyle DeathSound = new("CalamityMod/Sounds/NPCKilled/AstrumDeusDeath");

        // Fallback Vanilla Sound Triggers
        public static readonly SoundStyle ThunderSound = SoundID.Thunder;
        public static readonly SoundStyle BlastSound = SoundID.Item62;
        public static readonly SoundStyle FireSound = SoundID.Item20;

        // Math Constants
        private const float TwoPi = MathHelper.TwoPi;
        private const float Pi = MathHelper.Pi;
        private const float PiOver2 = MathHelper.PiOver2;

        // Projectile String Constants for Dynamic Loader Lookups
        private const string ProjComet = "AstralBlueComet";
        private const string ProjFlame = "AstralFlame";
        private const string ProjVortex = "AstralVortex";
        private const string ProjRubble = "AstralRubble";
        private const string ProjGlob = "InfectionGlob";
        private const string ProjShot = "AstralShot2";
        private const string ProjConstellation = "AstralConstellation";
        private const string ProjBlackHole = "AstralBlackHole";
        private const string ProjTelegraph = "AstralTelegraphLine";
        private const string ProjCrystal = "AstralCrystal";
        private const string ProjStar = "MassiveInfectedStar";

        // Desperation boundary radius
        private const float ArenaRadius = 660f;
        #endregion

        #region State Machine Enumeration
        public enum AttackState
        {
            SpawnAnimation = 0,
            WarpCharge = 1,
            AstralMeteorShower = 2,
            VortexLemniscate = 3,
            RubbleFromBelow = 4,
            PlasmaAndCrystals = 5,
            InfectedStarWeave = 6,
            DarkGodsOutburst = 7,
            AstralGlobRush = 8,
            ConstellationExplosions = 9,
            ArmorShedCeremony = 10,
            AstralNovaSingularity = 11,
            WarpBarrage = 12,
            DespawnRetreat = 13
        }
        #endregion

        #region Instance Fields
        // Drawing variables
        private float desperationVignetteAlpha = 0f;
        private float desperationVignetteRadius = 0f;
        private float lemniscateLineAlpha = 0f;
        
        // Figure-8 Lemniscate Foci coordinates
        private Vector2 lemniscateCenter = Vector2.Zero;

        // Star constellation nodes
        private readonly List<Vector2> starNodeCoordinates = new();
        private readonly List<float> starNodeScale = new();

        // Singularity Arena coordinates
        private Vector2 singularityCenter = Vector2.Zero;

        // Ectoplasmic/Astral laser telegraphs
        private readonly List<Vector2> laserTelegraphStarts = new();
        private readonly List<Vector2> laserTelegraphEnds = new();
        private int laserTelegraphTimer = 0;
        #endregion

        #region Core AI Override Hooks
        /// <summary>
        /// Redirects AI update tick to specific worm segment logic (Head, Body, or Tail).
        /// </summary>
        public override bool PreAI(NPC npc, IUMWGlobalNPC data)
        {
            // Verify worm segment types and route accordingly
            int bodyType = ModContent.NPCType<CalamityMod.NPCs.AstrumDeus.AstrumDeusBody>();
            int tailType = ModContent.NPCType<CalamityMod.NPCs.AstrumDeus.AstrumDeusTail>();

            if (npc.type == bodyType)
            {
                UpdateBodySegment(npc);
                return false;
            }
            if (npc.type == tailType)
            {
                UpdateTailSegment(npc);
                return false;
            }

            // Head Segment logic
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

            // Sync states
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

            // Reset damage parameters if fading/teleporting
            npc.defDamage = 180;
            npc.damage = npc.alpha > 40 ? 0 : npc.defDamage;

            // Trigger Phase 4 Desperation when health drops below 20%
            float lifeRatio = npc.life / (float)npc.lifeMax;
            if (currentPhase < 4 && lifeRatio <= PhaseLifeRatios[2])
            {
                npc.ai[0] = 4f;
                currentPhase = 4;
                TransitionToState(npc, AttackState.ArmorShedCeremony);
                state = AttackState.ArmorShedCeremony;
                singularityCenter = target.Center;
                despawnRemainingSegments(npc);
                SoundEngine.PlaySound(SplitSound, npc.Center);
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 25f;
            }

            // Run standard phase transitions
            if (currentPhase < 4)
            {
                CheckPhaseTransitions(npc, target, ref currentPhase, ref state, ref timer, ref stateTracker);
            }

            // Update local timers/variables
            UpdateLocalFields(npc, state, timer);

            // Execute State Machine
            switch (state)
            {
                case AttackState.SpawnAnimation:
                    DoAttack_SpawnAnimation(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.WarpCharge:
                    DoAttack_WarpCharge(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.AstralMeteorShower:
                    DoAttack_AstralMeteorShower(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.VortexLemniscate:
                    DoAttack_VortexLemniscate(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.RubbleFromBelow:
                    DoAttack_RubbleFromBelow(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.PlasmaAndCrystals:
                    DoAttack_PlasmaAndCrystals(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.InfectedStarWeave:
                    DoAttack_InfectedStarWeave(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.DarkGodsOutburst:
                    DoAttack_DarkGodsOutburst(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.AstralGlobRush:
                    DoAttack_AstralGlobRush(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.ConstellationExplosions:
                    DoAttack_ConstellationExplosions(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.ArmorShedCeremony:
                    DoAttack_ArmorShedCeremony(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.AstralNovaSingularity:
                    DoAttack_AstralNovaSingularity(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.WarpBarrage:
                    DoAttack_WarpBarrage(npc, target, ref timer, ref stateTracker);
                    break;
                case AttackState.DespawnRetreat:
                    ExecuteDespawnAI(npc);
                    break;
            }

            // Core physics adjustments
            timer++;
            npc.rotation = npc.velocity.ToRotation() + PiOver2;
            npc.knockBackResist = 0f;

            // Report debug values
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

        #region Segment Trailing Physics
        /// <summary>
        /// Updates trailing coordinates and alignment physics for Astrum Deus Body segments.
        /// </summary>
        private void UpdateBodySegment(NPC npc)
        {
            if (!Main.npc.IndexInRange((int)npc.ai[0]) || !Main.npc[(int)npc.ai[0]].active)
            {
                npc.life = 0;
                npc.HitEffect(0, 10.0);
                npc.active = false;
                npc.netUpdate = true;
                return;
            }

            NPC aheadSegment = Main.npc[(int)npc.ai[0]];
            NPC headSegment = Main.npc[(int)npc.ai[1]];
            npc.target = aheadSegment.target;
            npc.alpha = headSegment.alpha;

            npc.defense = aheadSegment.defense;
            npc.dontTakeDamage = aheadSegment.dontTakeDamage;
            npc.damage = npc.alpha > 40 || headSegment.damage <= 0 ? 0 : npc.defDamage;

            npc.Calamity().DR = 0.35f;

            // Positioning and rotation trailing
            Vector2 directionToNextSegment = aheadSegment.Center - npc.Center;
            if (aheadSegment.rotation != npc.rotation)
            {
                directionToNextSegment = directionToNextSegment.RotatedBy(MathHelper.WrapAngle(aheadSegment.rotation - npc.rotation) * 0.12f);
            }

            bool isExposed = headSegment.active && headSegment.life / (float)headSegment.lifeMax < 0.20f;
            if (isExposed)
            {
                npc.HitSound = SoundID.NPCHit1;
                npc.Calamity().DR = 0.45f;
            }

            npc.rotation = directionToNextSegment.ToRotation() + PiOver2;
            npc.Center = aheadSegment.Center - directionToNextSegment.SafeNormalize(Vector2.Zero) * npc.width * npc.scale;

            // Emit cosmic dust trails when exposed
            if (isExposed && Main.rand.NextFloat() < npc.Opacity * 0.18f)
            {
                Dust d = Dust.NewDustPerfect(npc.Center, DustID.PinkCrystalShard, Main.rand.NextVector2Circular(3.5f, 3.5f));
                d.noGravity = true;
                d.scale = Main.rand.NextFloat(0.85f, 1.35f);
            }
        }

        /// <summary>
        /// Updates trailing coordinates and alignment physics for Astrum Deus Tail segments.
        /// </summary>
        private void UpdateTailSegment(NPC npc)
        {
            if (!Main.npc.IndexInRange((int)npc.ai[0]) || !Main.npc[(int)npc.ai[0]].active)
            {
                npc.life = 0;
                npc.HitEffect(0, 10.0);
                npc.active = false;
                npc.netUpdate = true;
                return;
            }

            NPC aheadSegment = Main.npc[(int)npc.ai[0]];
            npc.target = aheadSegment.target;
            npc.alpha = aheadSegment.alpha;

            npc.defense = aheadSegment.defense;
            npc.dontTakeDamage = aheadSegment.dontTakeDamage;
            npc.damage = npc.alpha > 40 || aheadSegment.damage <= 0 ? 0 : npc.defDamage;

            npc.Calamity().DR = 0.65f;

            Vector2 directionToNextSegment = aheadSegment.Center - npc.Center;
            if (aheadSegment.rotation != npc.rotation)
            {
                directionToNextSegment = directionToNextSegment.RotatedBy(MathHelper.WrapAngle(aheadSegment.rotation - npc.rotation) * 0.075f);
            }

            NPC headSegment = Main.npc[(int)npc.realLife];
            bool isExposed = headSegment.active && headSegment.life / (float)headSegment.lifeMax < 0.20f;
            if (isExposed)
            {
                npc.HitSound = SoundID.NPCHit1;
            }

            npc.rotation = directionToNextSegment.ToRotation() + PiOver2;
            npc.Center = aheadSegment.Center - directionToNextSegment.SafeNormalize(Vector2.Zero) * npc.width * npc.scale;
        }
        #endregion

        #region Phase Transitions & Local Updates
        /// <summary>
        /// Updates local visual values (alphas, sizes) based on current state and time.
        /// </summary>
        private void UpdateLocalFields(NPC npc, AttackState state, float timer)
        {
            float lifeRatio = npc.life / (float)npc.lifeMax;
            bool isExposed = lifeRatio < 0.20f;

            // Update Lemniscate path line visibility
            if (state == AttackState.VortexLemniscate)
            {
                lemniscateLineAlpha = MathHelper.Lerp(lemniscateLineAlpha, 0.5f, 0.05f);
            }
            else
            {
                lemniscateLineAlpha = MathHelper.Lerp(lemniscateLineAlpha, 0f, 0.1f);
            }

            // Update Desperation Vignette/Ring boundaries
            if (isExposed || state == AttackState.ArmorShedCeremony || state == AttackState.AstralNovaSingularity || state == AttackState.WarpBarrage)
            {
                desperationVignetteAlpha = MathHelper.Lerp(desperationVignetteAlpha, 0.65f, 0.04f);
                desperationVignetteRadius = MathHelper.Lerp(desperationVignetteRadius, ArenaRadius, 0.04f);
            }
            else
            {
                desperationVignetteAlpha = MathHelper.Lerp(desperationVignetteAlpha, 0f, 0.08f);
                desperationVignetteRadius = MathHelper.Lerp(desperationVignetteRadius, 0f, 0.08f);
            }

            // Gradually decrease laser grid telegraph indicators
            if (laserTelegraphTimer > 0)
            {
                laserTelegraphTimer--;
                if (laserTelegraphTimer <= 0)
                {
                    laserTelegraphStarts.Clear();
                    laserTelegraphEnds.Clear();
                }
            }
        }

        /// <summary>
        /// Selects the next appropriate state depending on current phase and cycle logic.
        /// </summary>
        private AttackState SelectNextState(int phase, ref float cycleIndex)
        {
            List<AttackState> pattern;

            if (phase == 1)
            {
                pattern = new List<AttackState>
                {
                    AttackState.WarpCharge,
                    AttackState.AstralMeteorShower,
                    AttackState.RubbleFromBelow,
                    AttackState.WarpCharge
                };
            }
            else if (phase == 2)
            {
                pattern = new List<AttackState>
                {
                    AttackState.VortexLemniscate,
                    AttackState.PlasmaAndCrystals,
                    AttackState.AstralGlobRush,
                    AttackState.RubbleFromBelow
                };
            }
            else if (phase == 3)
            {
                pattern = new List<AttackState>
                {
                    AttackState.InfectedStarWeave,
                    AttackState.DarkGodsOutburst,
                    AttackState.ConstellationExplosions,
                    AttackState.WarpCharge
                };
            }
            else
            {
                // Desperation loops
                pattern = new List<AttackState>
                {
                    AttackState.AstralNovaSingularity,
                    AttackState.WarpBarrage
                };
            }

            int index = (int)(cycleIndex % pattern.Count);
            cycleIndex++;
            return pattern[index];
        }

        /// <summary>
        /// Validates boss health and transitions to next attack pattern cycles.
        /// </summary>
        private void CheckPhaseTransitions(NPC npc, Player target, ref int phase, ref AttackState state, ref float timer, ref float cycleIndex)
        {
            float lifeRatio = npc.life / (float)npc.lifeMax;
            int targetPhase = 1;

            foreach (float threshold in PhaseLifeRatios)
            {
                if (lifeRatio <= threshold)
                {
                    targetPhase++;
                }
            }

            // Handle transition shifts
            if (phase != targetPhase)
            {
                phase = targetPhase;
                npc.ai[0] = phase;
                timer = 0f;
                cycleIndex = 0f;
                CleanupStrayEntities();

                if (phase == 4)
                {
                    TransitionToState(npc, AttackState.ArmorShedCeremony);
                    state = AttackState.ArmorShedCeremony;
                }
                else
                {
                    TransitionToState(npc, AttackState.WarpCharge);
                    state = AttackState.WarpCharge;
                }

                npc.netUpdate = true;
            }
        }

        private void TransitionToState(NPC npc, AttackState newState)
        {
            npc.ai[1] = (float)newState;
            npc.ai[2] = 0f; // Reset timer
            npc.netUpdate = true;
        }

        /// <summary>
        /// Removes stray portal, comets, or other projectles to clean up screen.
        /// </summary>
        private void CleanupStrayEntities()
        {
            int vortexType = GetCalamityProjectileType(ProjVortex);
            int starType = GetCalamityProjectileType(ProjStar);
            int blackholeType = GetCalamityProjectileType(ProjBlackHole);

            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && (p.type == vortexType || p.type == starType || p.type == blackholeType))
                {
                    p.Kill();
                }
            }
        }

        private void despawnRemainingSegments(NPC head)
        {
            int bodyType = ModContent.NPCType<CalamityMod.NPCs.AstrumDeus.AstrumDeusBody>();
            int tailType = ModContent.NPCType<CalamityMod.NPCs.AstrumDeus.AstrumDeusTail>();

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC n = Main.npc[i];
                if (n.active && n.realLife == head.whoAmI && (n.type == bodyType || n.type == tailType))
                {
                    // Body segments are retained, but we trigger a net update
                    n.netUpdate = true;
                }
            }
        }
        #endregion

        #region Attack Implementations - Phase 1
        /// <summary>
        /// Spawn intro: materializes and descends from the sky.
        /// </summary>
        private void DoAttack_SpawnAnimation(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.damage = 0;
            npc.dontTakeDamage = true;
            npc.velocity *= 0.94f;

            if (timer == 1)
            {
                SoundEngine.PlaySound(SpawnSound, npc.Center);
                npc.Center = target.Center + new Vector2(0f, -800f);
                npc.velocity = new Vector2(0f, 15f);
                npc.netUpdate = true;
            }

            // Descend with spiral loops
            if (timer > 30 && timer < 120)
            {
                float angle = (timer - 30) * 0.05f;
                npc.velocity = new Vector2((float)Math.Cos(angle) * 12f, 16f);
            }

            if (timer >= 120)
            {
                npc.dontTakeDamage = false;
                TransitionToState(npc, AttackState.WarpCharge);
            }
        }

        /// <summary>
        /// Warp Charge: Fades out, teleports behind player, dashes through target.
        /// </summary>
        private void DoAttack_WarpCharge(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            const float FadeOutTime = 40f;
            const float DelayTime = 25f;
            const float DashTime = 45f;

            if (timer < FadeOutTime)
            {
                npc.damage = 0;
                npc.velocity *= 0.92f;
                // Fade out
                npc.alpha = (int)MathHelper.Lerp(npc.alpha, 255, 0.08f);
            }
            else if (timer == FadeOutTime)
            {
                // Play teleport sound
                SoundEngine.PlaySound(SplitSound, npc.Center);
                
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    // Target predicted coordinate
                    Vector2 predictedPos = target.Center + target.velocity * 22f;
                    Vector2 offset = -target.velocity.SafeNormalize(Vector2.UnitY).RotatedBy(Main.rand.NextFloat(-0.3f, 0.3f)) * 850f;
                    if (offset.LengthSquared() < 100f)
                    {
                        offset = new Vector2(0f, -850f);
                    }

                    npc.Center = predictedPos + offset;
                    npc.velocity = SafeNormalize(predictedPos - npc.Center, Vector2.UnitY) * 38f;
                    npc.alpha = 255;
                    npc.netUpdate = true;

                    // Spawn telegraph indicator
                    int telType = GetCalamityProjectileType(ProjTelegraph);
                    if (telType != ProjectileID.DeathLaser)
                    {
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, SafeNormalize(npc.velocity, Vector2.UnitY), telType, 0, 0f, Main.myPlayer);
                    }
                }
            }
            else if (timer > FadeOutTime && timer < FadeOutTime + DelayTime)
            {
                npc.damage = 0;
                npc.velocity *= 0.95f;
                npc.alpha = (int)MathHelper.Lerp(npc.alpha, 0, 0.15f);
            }
            else if (timer == FadeOutTime + DelayTime)
            {
                SoundEngine.PlaySound(LaserSound, npc.Center);
                npc.damage = npc.defDamage;
                npc.alpha = 0;
                
                // Set dash speed
                float dashSpeed = 44f + (phase * 3f);
                npc.velocity = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * dashSpeed;
                npc.netUpdate = true;

                // Fire comets
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int cometType = GetCalamityProjectileType(ProjComet);
                    int count = 6 + phase * 2;
                    for (int i = 0; i < count; i++)
                    {
                        float angle = (i / (float)count) * TwoPi;
                        Vector2 cometVel = angle.ToRotationVector2() * 8.5f;
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, cometVel, cometType, ScaleDamage(npc, 135), 0f, Main.myPlayer);
                    }
                }
            }
            else if (timer >= FadeOutTime + DelayTime + DashTime)
            {
                npc.velocity *= 0.88f;
                AttackState next = SelectNextState(phase, ref stateTracker);
                TransitionToState(npc, next);
            }
        }

        /// <summary>
        /// Meteor Shower: Flies high into heavens, charges down, raining comets.
        /// </summary>
        private void DoAttack_AstralMeteorShower(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            const float RiseTime = 80f;
            const float RainTime = 160f;

            if (timer < RiseTime)
            {
                // Rise vertically
                npc.velocity.X = MathHelper.Lerp(npc.velocity.X, 0f, 0.1f);
                npc.velocity.Y = MathHelper.Lerp(npc.velocity.Y, -28f, 0.08f);

                if (Math.Abs(npc.Center.X - target.Center.X) > 600f)
                {
                    npc.Center = new Vector2(MathHelper.Lerp(npc.Center.X, target.Center.X, 0.05f), npc.Center.Y);
                }
            }
            else if (timer == RiseTime)
            {
                SoundEngine.PlaySound(ThunderSound, target.Center);
                npc.velocity = new Vector2(0f, 30f);
                npc.netUpdate = true;
            }
            else if (timer > RiseTime && timer < RiseTime + RainTime)
            {
                // Slam down, curving slightly towards target
                npc.velocity.X = MathHelper.Lerp(npc.velocity.X, Math.Sign(target.Center.X - npc.Center.X) * 14f, 0.04f);
                npc.velocity.Y = MathHelper.Lerp(npc.velocity.Y, 28f, 0.05f);

                // Rain comets from sky
                if (timer % 8 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int cometType = GetCalamityProjectileType(ProjComet);
                    float offset = Main.rand.NextFloat(-800f, 800f);
                    Vector2 spawnPos = target.Center + new Vector2(offset, -750f);
                    Vector2 cometVel = new Vector2(Main.rand.NextFloat(-2f, 2f), 12f);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), spawnPos, cometVel, cometType, ScaleDamage(npc, 130), 0f, Main.myPlayer);
                }
            }
            else if (timer >= RiseTime + RainTime)
            {
                npc.velocity *= 0.9f;
                AttackState next = SelectNextState(phase, ref stateTracker);
                TransitionToState(npc, next);
            }
        }
        #endregion

        #region Attack Implementations - Phase 2
        /// <summary>
        /// Vortex Lemniscate: Figure-8 parametric curve flight path with vortexes.
        /// </summary>
        private void DoAttack_VortexLemniscate(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            const float SetupTime = 40f;
            const float LemniscateTime = 220f;

            if (timer < SetupTime)
            {
                npc.damage = 0;
                // Hover closer to target to align the Lemniscate
                Vector2 alignPoint = target.Center + new Vector2(0f, -250f);
                npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(alignPoint - npc.Center, Vector2.UnitY) * 22f, 0.08f);
                lemniscateCenter = target.Center;
            }
            else if (timer >= SetupTime && timer < SetupTime + LemniscateTime)
            {
                npc.damage = npc.defDamage;
                
                // Bernoulli Lemniscate parametric calculation
                float t = (timer - SetupTime) * 0.045f;
                float cost = (float)Math.Cos(t);
                float sint = (float)Math.Sin(t);
                float denom = 1f + sint * sint;

                float r = 620f;
                Vector2 targetOffset = new Vector2(r * cost / denom, r * cost * sint / denom);
                Vector2 dest = lemniscateCenter + targetOffset;

                npc.Center = Vector2.Lerp(npc.Center, dest, 0.12f);
                npc.velocity = dest - npc.Center;

                // Spawn vortices at lemniscate foci points
                if (timer == SetupTime + 10f && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int vortexType = GetCalamityProjectileType(ProjVortex);
                    Vector2 focus1 = lemniscateCenter - new Vector2(r * 0.7f, 0f);
                    Vector2 focus2 = lemniscateCenter + new Vector2(r * 0.7f, 0f);

                    Projectile.NewProjectile(npc.GetSource_FromAI(), focus1, Vector2.Zero, vortexType, ScaleDamage(npc, 160), 0f, Main.myPlayer);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), focus2, Vector2.Zero, vortexType, ScaleDamage(npc, 160), 0f, Main.myPlayer);
                }

                // Fire plasma fireballs occasionally
                if (timer % 40 == 0)
                {
                    SoundEngine.PlaySound(FireSound, npc.Center);
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        int flameType = GetCalamityProjectileType(ProjFlame);
                        Vector2 flameVel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 11f;
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, flameVel, flameType, ScaleDamage(npc, 140), 0f, Main.myPlayer);
                    }
                }
            }
            else if (timer >= SetupTime + LemniscateTime)
            {
                npc.velocity *= 0.88f;
                CleanupStrayEntities();
                AttackState next = SelectNextState(phase, ref stateTracker);
                TransitionToState(npc, next);
            }
        }

        /// <summary>
        /// Rubble From Below: Dive-bombs, then rushes up launching rubble.
        /// </summary>
        private void DoAttack_RubbleFromBelow(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            const float DiveTime = 60f;
            const float RiseTime = 70f;

            if (timer < DiveTime)
            {
                // Move down
                npc.velocity.X = MathHelper.Lerp(npc.velocity.X, 0f, 0.1f);
                npc.velocity.Y = MathHelper.Lerp(npc.velocity.Y, 26f, 0.08f);

                if (npc.Center.Y < target.Center.Y + 650f && timer >= DiveTime - 5f)
                {
                    timer = DiveTime - 5f; // Wait until far enough down
                }
            }
            else if (timer == DiveTime)
            {
                SoundEngine.PlaySound(BlastSound, target.Center);
                npc.velocity = new Vector2(0f, -32f);
                npc.netUpdate = true;
            }
            else if (timer > DiveTime && timer < DiveTime + RiseTime)
            {
                // Rush upward
                npc.velocity.Y = MathHelper.Lerp(npc.velocity.Y, -34f, 0.06f);
                
                // Align horizontally closer to player
                if (Math.Abs(npc.Center.X - target.Center.X) > 150f)
                {
                    npc.velocity.X = MathHelper.Lerp(npc.velocity.X, Math.Sign(target.Center.X - npc.Center.X) * 12f, 0.05f);
                }

                // Shoot rubble from segments
                if (timer % 12 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int rubbleType = GetCalamityProjectileType(ProjRubble);
                    int count = 4 + phase;
                    for (int i = 0; i < count; i++)
                    {
                        float angle = -PiOver2 + MathHelper.Lerp(-0.7f, 0.7f, i / (float)(count - 1));
                        Vector2 rubbleVel = angle.ToRotationVector2() * Main.rand.NextFloat(8f, 15f);
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, rubbleVel, rubbleType, ScaleDamage(npc, 135), 0f, Main.myPlayer);
                    }
                }
            }
            else if (timer >= DiveTime + RiseTime)
            {
                npc.velocity *= 0.9f;
                AttackState next = SelectNextState(phase, ref stateTracker);
                TransitionToState(npc, next);
            }
        }

        /// <summary>
        /// Plasma & Crystals: Curved flight, firing fireballs, while segments fire crystals.
        /// </summary>
        private void DoAttack_PlasmaAndCrystals(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            const float AttackTime = 200f;

            if (timer < AttackTime)
            {
                // Hover / orbit target
                float speed = 21f + phase * 2f;
                Vector2 targetDest = target.Center + (timer * 0.02f).ToRotationVector2() * 520f;
                npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(targetDest - npc.Center, Vector2.UnitY) * speed, 0.06f);

                // Shoot plasma fireballs from head
                if (timer % 30 == 0)
                {
                    SoundEngine.PlaySound(FireSound, npc.Center);
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        int flameType = GetCalamityProjectileType(ProjFlame);
                        Vector2 flameVel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 13f;
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, flameVel, flameType, ScaleDamage(npc, 140), 0f, Main.myPlayer);
                    }
                }

                // Shoot crystals from random body segments
                if (timer % 15 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int crystalType = GetCalamityProjectileType(ProjCrystal);
                    int bodyType = ModContent.NPCType<CalamityMod.NPCs.AstrumDeus.AstrumDeusBody>();
                    List<NPC> segmentList = new();
                    for (int i = 0; i < Main.maxNPCs; i++)
                    {
                        NPC n = Main.npc[i];
                        if (n.active && n.type == bodyType && n.realLife == npc.whoAmI && n.WithinRange(target.Center, 850f))
                        {
                            segmentList.Add(n);
                        }
                    }

                    if (segmentList.Count > 0)
                    {
                        NPC sourceSegment = Main.rand.Next(segmentList);
                        Vector2 crystalVel = SafeNormalize(target.Center - sourceSegment.Center, Vector2.UnitY) * 9f;
                        Projectile.NewProjectile(npc.GetSource_FromAI(), sourceSegment.Center, crystalVel, crystalType, ScaleDamage(npc, 130), 0f, Main.myPlayer);
                    }
                }
            }
            else
            {
                npc.velocity *= 0.9f;
                AttackState next = SelectNextState(phase, ref stateTracker);
                TransitionToState(npc, next);
            }
        }
        #endregion

        #region Attack Implementations - Phase 3
        /// <summary>
        /// Infected Star Weave: Circles a central spot, spawning a growing star.
        /// </summary>
        private void DoAttack_InfectedStarWeave(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            const float WeaveTime = 160f;
            const float ChargeDelay = 50f;

            if (timer == 1)
            {
                // Establish central spot
                lemniscateCenter = target.Center + Main.rand.NextVector2Circular(200f, 200f);
                npc.netUpdate = true;
            }

            if (timer < WeaveTime)
            {
                npc.damage = 0;
                // Circle around the center
                float angle = timer * 0.05f;
                Vector2 dest = lemniscateCenter + angle.ToRotationVector2() * 450f;
                npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(dest - npc.Center, Vector2.UnitY) * 25f, 0.08f);

                // Spawn and grow infected star at center
                if (timer == 10 && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int starType = GetCalamityProjectileType(ProjStar);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), lemniscateCenter, Vector2.Zero, starType, ScaleDamage(npc, 180), 0f, Main.myPlayer);
                }

                // Fire crossing lasers from segments
                if (timer % 20 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int shotType = GetCalamityProjectileType(ProjShot);
                    int bodyType = ModContent.NPCType<CalamityMod.NPCs.AstrumDeus.AstrumDeusBody>();
                    for (int i = 0; i < Main.maxNPCs; i++)
                    {
                        NPC n = Main.npc[i];
                        if (n.active && n.type == bodyType && n.realLife == npc.whoAmI && Main.rand.NextBool(12))
                        {
                            Vector2 shotVel = SafeNormalize(target.Center - n.Center, Vector2.UnitY) * 10f;
                            Projectile.NewProjectile(npc.GetSource_FromAI(), n.Center, shotVel, shotType, ScaleDamage(npc, 135), 0f, Main.myPlayer);
                        }
                    }
                }
            }
            else if (timer >= WeaveTime && timer < WeaveTime + ChargeDelay)
            {
                npc.damage = npc.defDamage;
                if (timer == WeaveTime)
                {
                    SoundEngine.PlaySound(LaserSound, npc.Center);
                    // Lunge directly at player
                    npc.velocity = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 36f;
                    npc.netUpdate = true;

                    // Hurl the infected star
                    int starType = GetCalamityProjectileType(ProjStar);
                    for (int i = 0; i < Main.maxProjectiles; i++)
                    {
                        Projectile p = Main.projectile[i];
                        if (p.active && p.type == starType)
                        {
                            p.velocity = SafeNormalize(target.Center - p.Center, Vector2.UnitY) * 18f;
                            p.netUpdate = true;
                        }
                    }
                }
            }
            else if (timer >= WeaveTime + ChargeDelay)
            {
                npc.velocity *= 0.88f;
                CleanupStrayEntities();
                AttackState next = SelectNextState(phase, ref stateTracker);
                TransitionToState(npc, next);
            }
        }

        /// <summary>
        /// Dark God's Outburst: Black hole pulling player, surrounded by dark stars firing lasers.
        /// </summary>
        private void DoAttack_DarkGodsOutburst(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            const float OutburstTime = 240f;

            if (timer == 1)
            {
                lemniscateCenter = target.Center;
                SoundEngine.PlaySound(GodRaySound, npc.Center);
                npc.netUpdate = true;

                // Spawn black hole at center
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int bhType = GetCalamityProjectileType(ProjBlackHole);
                    Projectile.NewProjectile(npc.GetSource_FromAI(), lemniscateCenter, Vector2.Zero, bhType, ScaleDamage(npc, 200), 0f, Main.myPlayer);
                }
            }

            if (timer < OutburstTime)
            {
                npc.damage = 0;
                // Move in slow orbit around black hole
                float angle = timer * 0.02f;
                Vector2 dest = lemniscateCenter + angle.ToRotationVector2() * 620f;
                npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(dest - npc.Center, Vector2.UnitY) * 18f, 0.08f);

                // Gravitational pull on player towards black hole
                float dist = Vector2.Distance(target.Center, lemniscateCenter);
                if (dist > 50f && dist < 1200f)
                {
                    Vector2 pull = SafeNormalize(lemniscateCenter - target.Center, Vector2.Zero) * (6f - (dist / 220f));
                    if (pull.Length() > 0.1f)
                    {
                        target.velocity += pull * 0.35f;
                    }
                }

                // Star constellation laser sweeps
                if (timer % 15 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int constType = GetCalamityProjectileType(ProjConstellation);
                    if (constType != ProjectileID.DeathLaser)
                    {
                        float starOffsetAngle = Main.rand.NextFloat(TwoPi);
                        Vector2 starSpawn = lemniscateCenter + starOffsetAngle.ToRotationVector2() * 450f;
                        Vector2 starVel = SafeNormalize(target.Center - starSpawn, Vector2.UnitY) * 9f;
                        Projectile.NewProjectile(npc.GetSource_FromAI(), starSpawn, starVel, constType, ScaleDamage(npc, 135), 0f, Main.myPlayer);
                    }
                }
            }
            else
            {
                npc.velocity *= 0.9f;
                CleanupStrayEntities();
                AttackState next = SelectNextState(phase, ref stateTracker);
                TransitionToState(npc, next);
            }
        }

        /// <summary>
        /// Glob Rush: Lunges directly, releasing spreads of infection globs.
        /// </summary>
        private void DoAttack_AstralGlobRush(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            const float DashCycleTime = 55f;
            const int DashCount = 3;

            float localTimer = timer % DashCycleTime;
            int dashIndex = (int)(timer / DashCycleTime);

            if (dashIndex >= DashCount)
            {
                npc.velocity *= 0.9f;
                AttackState next = SelectNextState(phase, ref stateTracker);
                TransitionToState(npc, next);
                return;
            }

            if (localTimer < 20)
            {
                npc.damage = 0;
                // Predictive alignment
                Vector2 targetDest = target.Center + target.velocity * 18f;
                npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(targetDest - npc.Center, Vector2.UnitY) * 16f, 0.12f);
            }
            else if (localTimer == 20)
            {
                SoundEngine.PlaySound(LaserSound, npc.Center);
                npc.damage = npc.defDamage;
                // Sudden dash
                npc.velocity = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 35f;
                npc.netUpdate = true;

                // Spit globs
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int globType = GetCalamityProjectileType(ProjGlob);
                    for (int i = 0; i < 7; i++)
                    {
                        Vector2 globVel = npc.velocity.RotatedBy(MathHelper.Lerp(-0.4f, 0.4f, i / 6f)) * 0.45f;
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, globVel, globType, ScaleDamage(npc, 130), 0f, Main.myPlayer);
                    }
                }
            }
            else
            {
                npc.velocity *= 0.96f;
            }
        }

        /// <summary>
        /// Constellation Explosions: Diagonal grids of stars that detonate into bursts.
        /// </summary>
        private void DoAttack_ConstellationExplosions(NPC npc, Player target, ref float timer, ref float stateTracker, int phase)
        {
            const float StarSpawnTime = 60f;
            const float DetonateDelay = 60f;

            if (timer == 1)
            {
                starNodeCoordinates.Clear();
                starNodeScale.Clear();
                
                // Establish linear path node coordinates
                Vector2 start = target.Center + new Vector2(-600f, -400f);
                Vector2 end = target.Center + new Vector2(600f, 400f);
                for (int i = 0; i < 8; i++)
                {
                    Vector2 coordinate = Vector2.Lerp(start, end, i / 7f);
                    starNodeCoordinates.Add(coordinate);
                    starNodeScale.Add(0.1f);
                }
                npc.netUpdate = true;
            }

            if (timer < StarSpawnTime)
            {
                npc.damage = 0;
                // Move slowly around target
                npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(target.Center + new Vector2(0f, -300f) - npc.Center, Vector2.UnitY) * 14f, 0.08f);

                // Grow star scale values
                for (int i = 0; i < starNodeScale.Count; i++)
                {
                    starNodeScale[i] = MathHelper.Lerp(starNodeScale[i], 1.2f, 0.05f);
                }
            }
            else if (timer == StarSpawnTime)
            {
                SoundEngine.PlaySound(BlastSound, target.Center);
                // Explode star nodes into radial comets
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int cometType = GetCalamityProjectileType(ProjComet);
                    foreach (Vector2 coordinate in starNodeCoordinates)
                    {
                        for (int i = 0; i < 6; i++)
                        {
                            Vector2 cometVel = (i * (TwoPi / 6f)).ToRotationVector2() * 7.5f;
                            Projectile.NewProjectile(npc.GetSource_FromAI(), coordinate, cometVel, cometType, ScaleDamage(npc, 140), 0f, Main.myPlayer);
                        }
                    }
                }
            }
            else if (timer >= StarSpawnTime + DetonateDelay)
            {
                starNodeCoordinates.Clear();
                starNodeScale.Clear();
                AttackState next = SelectNextState(phase, ref stateTracker);
                TransitionToState(npc, next);
            }
        }
        #endregion

        #region Attack Implementations - Phase 4 (Desperation)
        /// <summary>
        /// Armor Shed Ceremony: Flies high, sheds outer shell, glows exposed.
        /// </summary>
        private void DoAttack_ArmorShedCeremony(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.damage = 0;
            npc.dontTakeDamage = true;
            npc.velocity.X = MathHelper.Lerp(npc.velocity.X, 0f, 0.08f);
            npc.velocity.Y = MathHelper.Lerp(npc.velocity.Y, -26f, 0.05f);

            if (timer == 1)
            {
                SoundEngine.PlaySound(SpawnSound, npc.Center);
                npc.netUpdate = true;
            }

            // Burst particles at peak
            if (timer == 90)
            {
                SoundEngine.PlaySound(BlastSound, npc.Center);
                Main.LocalPlayer.Calamity().GeneralScreenShakePower = 30f;
                
                // Spawn massive ring of shards
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int cometType = GetCalamityProjectileType(ProjComet);
                    for (int i = 0; i < 24; i++)
                    {
                        Vector2 particleVel = (i * (TwoPi / 24f)).ToRotationVector2() * 12f;
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, particleVel, cometType, ScaleDamage(npc, 150), 0f, Main.myPlayer);
                    }
                }
            }

            if (timer >= 120)
            {
                npc.dontTakeDamage = false;
                TransitionToState(npc, AttackState.AstralNovaSingularity);
            }
        }

        /// <summary>
        /// Singularity: locked inside 660f boundary circle. Boss channels expanding rings/lasers.
        /// </summary>
        private void DoAttack_AstralNovaSingularity(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            npc.damage = 0;
            
            // Constrain boss at center of arena
            npc.velocity = Vector2.Lerp(npc.velocity, SafeNormalize(singularityCenter - npc.Center, Vector2.Zero) * 8f, 0.1f);
            if (npc.Distance(singularityCenter) < 20f)
            {
                npc.Center = singularityCenter;
                npc.velocity = Vector2.Zero;
            }

            // Force player inside the 660f boundary circle
            ForcePlayerInsideArena(target);

            // Channel laser grids
            if (timer % 50 == 0)
            {
                SoundEngine.PlaySound(LaserSound, target.Center);
                triggerDesperationLaserGrid(target);
            }
            if (timer % 50 == 45)
            {
                spawnDesperationLasers();
            }

            // Spawn expanding crystal rings from center
            if (timer % 60 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                int crystalType = GetCalamityProjectileType(ProjCrystal);
                int count = 12;
                for (int i = 0; i < count; i++)
                {
                    Vector2 crystalVel = (i * (TwoPi / count)).ToRotationVector2() * 6.5f;
                    Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, crystalVel, crystalType, ScaleDamage(npc, 140), 0f, Main.myPlayer);
                }
            }

            // Rain comets occasionally
            if (timer % 14 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                int cometType = GetCalamityProjectileType(ProjComet);
                float angle = Main.rand.NextFloat(TwoPi);
                Vector2 spawnPos = singularityCenter + angle.ToRotationVector2() * ArenaRadius;
                Vector2 cometVel = SafeNormalize(target.Center - spawnPos, Vector2.UnitY) * 9.5f;
                Projectile.NewProjectile(npc.GetSource_FromAI(), spawnPos, cometVel, cometType, ScaleDamage(npc, 130), 0f, Main.myPlayer);
            }

            if (timer >= 320f)
            {
                TransitionToState(npc, AttackState.WarpBarrage);
            }
        }

        /// <summary>
        /// Warp Barrage: consecutive warp-dashes crossing the singularity boundary.
        /// </summary>
        private void DoAttack_WarpBarrage(NPC npc, Player target, ref float timer, ref float stateTracker)
        {
            const float SetupTime = 30f;
            const float ChargeTime = 35f;
            const int DashTotal = 3;

            float localTimer = timer % (SetupTime + ChargeTime);
            int currentDash = (int)(timer / (SetupTime + ChargeTime));

            if (currentDash >= DashTotal)
            {
                npc.velocity *= 0.9f;
                TransitionToState(npc, AttackState.AstralNovaSingularity);
                return;
            }

            // Force player inside arena
            ForcePlayerInsideArena(target);

            if (localTimer < SetupTime)
            {
                npc.damage = 0;
                npc.velocity *= 0.9f;
                // Fade out
                npc.alpha = (int)MathHelper.Lerp(npc.alpha, 255, 0.15f);

                if (localTimer == SetupTime - 1f)
                {
                    SoundEngine.PlaySound(SplitSound, npc.Center);
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        // Warp to side of arena targeting predicted player position
                        float offsetAngle = Main.rand.NextFloat(TwoPi);
                        npc.Center = singularityCenter + offsetAngle.ToRotationVector2() * (ArenaRadius - 80f);
                        npc.velocity = SafeNormalize(target.Center + target.velocity * 15f - npc.Center, Vector2.UnitY) * 42f;
                        npc.alpha = 255;
                        npc.netUpdate = true;

                        // Spawn laser guide line
                        int telType = GetCalamityProjectileType(ProjTelegraph);
                        if (telType != ProjectileID.DeathLaser)
                        {
                            Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, SafeNormalize(npc.velocity, Vector2.UnitY), telType, 0, 0f, Main.myPlayer);
                        }
                    }
                }
            }
            else
            {
                npc.damage = npc.defDamage;
                npc.alpha = 0;
            }
        }

        /// <summary>
        /// Pulls player back inside and inflicts damage if they step out of the 660f arena boundary.
        /// </summary>
        private void ForcePlayerInsideArena(Player player)
        {
            float dist = Vector2.Distance(player.Center, singularityCenter);
            if (dist > ArenaRadius)
            {
                Vector2 pullDirection = SafeNormalize(singularityCenter - player.Center, Vector2.UnitY);
                player.velocity += pullDirection * 1.6f;

                if (Main.GameUpdateCount % 8 == 0)
                {
                    player.Hurt(Terraria.DataStructures.PlayerDeathReason.ByCustomReason(Terraria.Localization.NetworkText.FromLiteral(player.name + " was vaporized by the Astral Nova Singularity!")), 35, 0);
                    // Spawn warning particles
                    for (int i = 0; i < 15; i++)
                    {
                        Dust d = Dust.NewDustPerfect(player.Center, DustID.PinkCrystalShard, Main.rand.NextVector2Circular(6f, 6f));
                        d.noGravity = true;
                    }
                }
            }
        }

        /// <summary>
        /// Triggers cross laser telegraph grids.
        /// </summary>
        private void triggerDesperationLaserGrid(Player target)
        {
            laserTelegraphStarts.Clear();
            laserTelegraphEnds.Clear();

            // Set grid positions around target
            laserTelegraphStarts.Add(target.Center + new Vector2(-800f, 0f));
            laserTelegraphEnds.Add(target.Center + new Vector2(800f, 0f));

            laserTelegraphStarts.Add(target.Center + new Vector2(0f, -600f));
            laserTelegraphEnds.Add(target.Center + new Vector2(0f, 600f));

            laserTelegraphTimer = 45; // Guide guide line duration
        }

        private void spawnDesperationLasers()
        {
            if (laserTelegraphStarts.Count >= 2 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SoundEngine.PlaySound(LaserSound, laserTelegraphStarts[0] + new Vector2(800f, 0f));
                int shotType = GetCalamityProjectileType(ProjShot);
                Projectile.NewProjectile(null, laserTelegraphStarts[0], new Vector2(16f, 0f), shotType, 130, 0f, Main.myPlayer);
                Projectile.NewProjectile(null, laserTelegraphStarts[1], new Vector2(0f, 16f), shotType, 130, 0f, Main.myPlayer);
            }
        }

        private void ExecuteDespawnAI(NPC npc)
        {
            npc.velocity.Y -= 0.5f;
            if (npc.velocity.Y < -30f)
                npc.velocity.Y = -30f;
            npc.velocity.X *= 0.95f;
            npc.rotation = npc.velocity.ToRotation() + PiOver2;
            
            npc.timeLeft = Math.Min(npc.timeLeft - 1, 60);
            if (npc.timeLeft <= 0)
            {
                npc.active = false;
                npc.netUpdate = true;
            }
        }

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
        private int GetCalamityProjectileType(string name, int fallback = ProjectileID.DeathLaser)
        {
            if (ModContent.TryFind("CalamityMod", name, out ModProjectile proj))
            {
                return proj.Type;
            }
            return fallback;
        }

        private int ScaleDamage(NPC npc, int baselineDamage)
        {
            if (Main.expertMode)
            {
                return (int)(baselineDamage * 0.75f);
            }
            return baselineDamage;
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
        #endregion

        #region Custom FindFrame Drawing Hooks
        public override void FindFrame(NPC npc, int frameHeight)
        {
            // Set standard frame sheet switching
            npc.frameCounter += 1.0;
            if (npc.frameCounter >= 6.0)
            {
                npc.frame.Y = (npc.frame.Y + frameHeight) % (frameHeight * 4);
                npc.frameCounter = 0.0;
            }
        }

        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            int bodyType = ModContent.NPCType<CalamityMod.NPCs.AstrumDeus.AstrumDeusBody>();
            int tailType = ModContent.NPCType<CalamityMod.NPCs.AstrumDeus.AstrumDeusTail>();

            if (npc.type == bodyType)
            {
                return DrawBodySegment(npc, spriteBatch, screenPos, drawColor);
            }
            if (npc.type == tailType)
            {
                return DrawTailSegment(npc, spriteBatch, screenPos, drawColor);
            }

            // Draw Head segment
            return DrawHeadSegment(npc, spriteBatch, screenPos, drawColor);
        }

        private bool DrawHeadSegment(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            Texture2D texture = TextureAssets.Npc[npc.type].Value;
            float lifeRatio = npc.life / (float)npc.lifeMax;
            bool isExposed = lifeRatio < 0.20f;

            Vector2 drawPosition = npc.Center - screenPos;
            Vector2 origin = texture.Size() * 0.5f;

            if (isExposed)
            {
                // exposed glowing afterimage effects
                Color glowColor = new Color(146, 238, 255) * 0.85f;
                for (int i = 1; i <= 4; i++)
                {
                    Vector2 offsetPos = npc.oldPos[i] - screenPos + npc.Size * 0.5f;
                    spriteBatch.Draw(texture, offsetPos, null, glowColor * (1f - i / 5f) * 0.35f, npc.oldRot[i] + PiOver2, origin, npc.scale, SpriteEffects.None, 0f);
                }
                spriteBatch.Draw(texture, drawPosition, null, Color.White, npc.rotation, origin, npc.scale, SpriteEffects.None, 0f);
            }
            else
            {
                drawColor = Color.Lerp(drawColor, Color.White, 0.6f);
                spriteBatch.Draw(texture, drawPosition, null, npc.GetAlpha(drawColor), npc.rotation, origin, npc.scale, SpriteEffects.None, 0f);
            }
            return false;
        }

        private bool DrawBodySegment(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (!Main.npc.IndexInRange((int)npc.realLife))
                return true;
            NPC headSegment = Main.npc[(int)npc.realLife];
            bool isExposed = headSegment.active && headSegment.life / (float)headSegment.lifeMax < 0.20f;

            Texture2D texture = TextureAssets.Npc[npc.type].Value;
            Vector2 drawPosition = npc.Center - screenPos;
            Vector2 origin = texture.Size() * 0.5f;

            if (isExposed)
            {
                Color glowColor = new Color(146, 238, 255) * 0.85f;
                for (int i = 1; i <= 3; i++)
                {
                    Vector2 offsetPos = npc.oldPos[i] - screenPos + npc.Size * 0.5f;
                    spriteBatch.Draw(texture, offsetPos, null, glowColor * (1f - i / 4f) * 0.35f, npc.oldRot[i] + PiOver2, origin, npc.scale, SpriteEffects.None, 0f);
                }
                spriteBatch.Draw(texture, drawPosition, null, Color.White, npc.rotation, origin, npc.scale, SpriteEffects.None, 0f);
            }
            else
            {
                drawColor = Color.Lerp(drawColor, Color.White, 0.6f);
                spriteBatch.Draw(texture, drawPosition, null, npc.GetAlpha(drawColor), npc.rotation, origin, npc.scale, SpriteEffects.None, 0f);
            }
            return false;
        }

        private bool DrawTailSegment(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (!Main.npc.IndexInRange((int)npc.realLife))
                return true;
            NPC headSegment = Main.npc[(int)npc.realLife];
            bool isExposed = headSegment.active && headSegment.life / (float)headSegment.lifeMax < 0.20f;

            Texture2D texture = TextureAssets.Npc[npc.type].Value;
            Vector2 drawPosition = npc.Center - screenPos;
            Vector2 origin = texture.Size() * 0.5f;

            if (isExposed)
            {
                Color glowColor = new Color(146, 238, 255) * 0.85f;
                for (int i = 1; i <= 3; i++)
                {
                    Vector2 offsetPos = npc.oldPos[i] - screenPos + npc.Size * 0.5f;
                    spriteBatch.Draw(texture, offsetPos, null, glowColor * (1f - i / 4f) * 0.35f, npc.oldRot[i] + PiOver2, origin, npc.scale, SpriteEffects.None, 0f);
                }
                spriteBatch.Draw(texture, drawPosition, null, Color.White, npc.rotation, origin, npc.scale, SpriteEffects.None, 0f);
            }
            else
            {
                drawColor = Color.Lerp(drawColor, Color.White, 0.6f);
                spriteBatch.Draw(texture, drawPosition, null, npc.GetAlpha(drawColor), npc.rotation, origin, npc.scale, SpriteEffects.None, 0f);
            }
            return false;
        }

        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            // Verify worm segment type. Draw telegraph boundaries only for head segment
            int bodyType = ModContent.NPCType<CalamityMod.NPCs.AstrumDeus.AstrumDeusBody>();
            int tailType = ModContent.NPCType<CalamityMod.NPCs.AstrumDeus.AstrumDeusTail>();
            if (npc.type == bodyType || npc.type == tailType)
            {
                return;
            }

            // Find current target
            if (npc.target < 0 || npc.target >= Main.maxPlayers)
                return;
            Player target = Main.player[npc.target];

            // Draw Bernoulli Lemniscate figure-8 path preview line in Phase 2
            if (lemniscateLineAlpha > 0.01f)
            {
                int pointCount = 60;
                float r = 620f;
                Vector2 prevPoint = Vector2.Zero;
                for (int i = 0; i <= pointCount; i++)
                {
                    float t = (i / (float)pointCount) * TwoPi;
                    float cost = (float)Math.Cos(t);
                    float sint = (float)Math.Sin(t);
                    float denom = 1f + sint * sint;
                    Vector2 targetOffset = new Vector2(r * cost / denom, r * cost * sint / denom);
                    Vector2 currentPoint = target.Center + targetOffset;

                    if (i > 0)
                    {
                        DrawLine(spriteBatch, prevPoint, currentPoint, new Color(255, 118, 226) * lemniscateLineAlpha, 4f);
                    }
                    prevPoint = currentPoint;
                }
            }

            // Draw star constellation node guides
            if (starNodeCoordinates.Count > 0)
            {
                for (int i = 0; i < starNodeCoordinates.Count; i++)
                {
                    Vector2 drawPos = starNodeCoordinates[i] - screenPos;
                    Texture2D starTex = TextureAssets.Projectile[ProjectileID.FallingStar].Value;
                    float scale = starNodeScale[i];
                    Color nodeColor = new Color(255, 118, 226) * 0.9f;
                    spriteBatch.Draw(starTex, drawPos, null, nodeColor, 0f, starTex.Size() * 0.5f, scale * 2.5f, SpriteEffects.None, 0f);

                    // Connect lines between nodes
                    if (i > 0)
                    {
                        DrawLine(spriteBatch, starNodeCoordinates[i - 1], starNodeCoordinates[i], new Color(255, 118, 226) * 0.45f, 2f);
                    }
                }
            }

            // Draw desperation singularity boundary circle
            if (desperationVignetteAlpha > 0.01f)
            {
                int segments = 120;
                Vector2 prevPos = Vector2.Zero;
                Color circleColor = new Color(146, 238, 255) * desperationVignetteAlpha;
                for (int i = 0; i <= segments; i++)
                {
                    float angle = (i / (float)segments) * TwoPi;
                    Vector2 currentPos = singularityCenter + angle.ToRotationVector2() * desperationVignetteRadius;

                    if (i > 0)
                    {
                        DrawLine(spriteBatch, prevPos, currentPos, circleColor, 6f);
                        // Add glow ring outline
                        DrawLine(spriteBatch, prevPos, currentPos, Color.White * desperationVignetteAlpha * 0.5f, 2f);
                    }
                    prevPos = currentPos;
                }

                // Draw dust swirls around boundary occasionally
                if (Main.rand.NextBool(4))
                {
                    float angle = Main.rand.NextFloat(TwoPi);
                    Vector2 swirlPos = singularityCenter + angle.ToRotationVector2() * desperationVignetteRadius;
                    Dust d = Dust.NewDustPerfect(swirlPos, DustID.PinkCrystalShard, angle.ToRotationVector2().RotatedBy(PiOver2) * Main.rand.NextFloat(2f, 5f));
                    d.noGravity = true;
                    d.scale = Main.rand.NextFloat(0.7f, 1.2f);
                }
            }

            // Draw laser guide grid telegraph lines
            if (laserTelegraphTimer > 0)
            {
                Color gridColor = new Color(255, 30, 80) * (laserTelegraphTimer / 45f) * 0.7f;
                for (int i = 0; i < laserTelegraphStarts.Count; i++)
                {
                    DrawLine(spriteBatch, laserTelegraphStarts[i], laserTelegraphEnds[i], gridColor, 3f);
                }
            }
        }
        #endregion
    }
}
