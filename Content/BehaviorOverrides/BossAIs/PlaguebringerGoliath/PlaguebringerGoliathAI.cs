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

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.PlaguebringerGoliath
{
    internal sealed class PlaguebringerGoliathAI : IUMWBossAI
    {
        #region Constants & Configurations
        public override int NPCType => ModContent.Find<ModNPC>("CalamityMod/PlaguebringerGoliath").Type;
        public override string BossName => "Plaguebringer Goliath";
        public override Color DebugColor => new(88, 210, 60);

        public override int MaxPhaseCount => 6;
        public override float[] PhaseLifeRatios => new[] { 0.90f, 0.70f, 0.50f, 0.30f, 0.12f };
        public override int AttackCycleLength => 120;
        public override float MotionIntensity => 0.9f;

        private static readonly SoundStyle ChargeSound = new("CalamityMod/Sounds/Custom/GoliathCharge");
        private static readonly SoundStyle ShieldRegenSound = new("CalamityMod/Sounds/Custom/CryogenShieldRegenerate");
        private static readonly SoundStyle NukeExplosionSound = SoundID.Item62;
        #endregion

        #region Attack States
        public enum AttackState
        {
            Virulence = 0,
            Malevolence = 1,
            PlagueStaff = 2,
            FuelCellBundle = 3,
            InfectedRemote = 4,
            TheSyringe = 5,
            TheHive = 6,
            PestilentDefiler = 7,
            Malachite = 8,
            BlightSpewer = 9,
            Pandemic = 10,
            PlagueTaintedSMG = 11,
            OverloadTransition = 12
        }
        #endregion

        #region Fields
        private int ticksRunning = 0;
        private int currentRepetition = 0;
        private readonly Vector2[] oldPositions = new Vector2[14];
        private int oldPositionsIndex;

        // Shield parameters
        private bool shieldActive = true;
        private int shieldStunTimer = 0;
        private int shieldRegenTimer = 0;

        // Arena steam variables
        private int steamWarnTimer = 0;
        private int activeSteamAxis = -1; // -1: none, 0: horizontal, 1: vertical, 2: both
        private float steamWarnOpacity = 0f;

        // Visual fields
        private float armorDither = 0f;
        #endregion

        #region Core AI Hooks
        public override bool PreAI(NPC npc, IUMWGlobalNPC data)
        {
            ticksRunning++;
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

            // Initialize Phase
            if (currentPhase == 0)
            {
                currentPhase = 1;
                npc.ai[0] = 1f;
                state = AttackState.Virulence;
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
                state = AttackState.OverloadTransition;
                npc.ai[1] = (float)state;
                timer = 0;
                stateTracker = 0;
                npc.netUpdate = true;
            }

            // Greenhouse Boundary Arena (1400px in P1-P3, 1000px in P4-P6)
            float borderSize = currentPhase <= 3 ? 1400f : 1000f;
            Vector2 dist = target.Center - npc.Center;
            if (dist.Length() > borderSize / 2f)
            {
                target.AddBuff(BuffID.Poisoned, 180);
                target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(npc.whoAmI), 15, 0);
                // push player back
                target.velocity += SafeNormalize(npc.Center - target.Center, Vector2.Zero) * 2f;
            }

            // Update steam vents every 6 seconds (360 frames)
            UpdateGreenhouseSteam(npc, target, borderSize);

            // Update Nano-Drone Grid Shield
            UpdateDroneShield(npc, currentPhase);

            // Visual oscillations and breathing
            npc.rotation = npc.velocity.X * 0.03f;
            npc.scale = 1f + (float)Math.Sin(ticksRunning * 0.05f) * 0.02f;

            // Execute state machine
            switch (state)
            {
                case AttackState.Virulence:
                    ExecuteVirulence(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.Malevolence:
                    ExecuteMalevolence(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.PlagueStaff:
                    ExecutePlagueStaff(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.FuelCellBundle:
                    ExecuteFuelCell(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.InfectedRemote:
                    ExecuteInfectedRemote(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.TheSyringe:
                    ExecuteTheSyringe(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.TheHive:
                    ExecuteTheHive(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.PestilentDefiler:
                    ExecutePestilentDefiler(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.Malachite:
                    ExecuteMalachite(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.BlightSpewer:
                    ExecuteBlightSpewer(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.Pandemic:
                    ExecutePandemic(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.PlagueTaintedSMG:
                    ExecutePlagueTaintedSMG(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.OverloadTransition:
                    ExecuteOverloadTransition(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
            }

            return false;
        }

        public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers)
        {
            if (shieldActive)
            {
                modifiers.FinalDamage *= 0.10f; // 90% DR
            }
            if (npc.ai[1] == (float)AttackState.OverloadTransition)
            {
                modifiers.FinalDamage *= 0f; // immune during transition
            }
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            if (shieldActive)
            {
                modifiers.FinalDamage *= 0.10f; // 90% DR
            }
            if (npc.ai[1] == (float)AttackState.OverloadTransition)
            {
                modifiers.FinalDamage *= 0f; // immune during transition
            }
        }
        #endregion

        #region Vents & Shield Helper Logic
        private void UpdateGreenhouseSteam(NPC npc, Player target, float borderSize)
        {
            steamWarnTimer++;
            if (steamWarnTimer >= 360)
            {
                steamWarnTimer = 0;
                activeSteamAxis = Main.rand.Next(3); // 0: horizontal, 1: vertical, 2: cross
                steamWarnOpacity = 1f;
            }

            if (steamWarnOpacity > 0f)
            {
                steamWarnOpacity -= 0.015f; // warning lasts 1.2s (72 frames)
                if (steamWarnOpacity <= 0f)
                {
                    int dmg = npc.damage / 3;
                    SoundEngine.PlaySound(SoundID.Item34, target.Center);

                    // Spray Steam columns/lines
                    if (activeSteamAxis == 0 || activeSteamAxis == 2)
                    {
                        // Horizontal stream through target Y
                        for (float x = -borderSize / 2f; x < borderSize / 2f; x += 80f)
                        {
                            Vector2 pos = npc.Center + new Vector2(x, target.Center.Y - npc.Center.Y);
                            SpawnHostile(npc, pos, new Vector2(0f, -0.1f), "Projectiles/Boss/PlagueCloud", dmg);
                        }
                    }
                    if (activeSteamAxis == 1 || activeSteamAxis == 2)
                    {
                        // Vertical stream through target X
                        for (float y = -borderSize / 2f; y < borderSize / 2f; y += 80f)
                        {
                            Vector2 pos = npc.Center + new Vector2(target.Center.X - npc.Center.X, y);
                            SpawnHostile(npc, pos, new Vector2(0.1f, 0f), "Projectiles/Boss/PlagueCloud", dmg);
                        }
                    }
                    activeSteamAxis = -1;
                }
            }
        }

        private void UpdateDroneShield(NPC npc, int currentPhase)
        {
            if (currentPhase > 3)
            {
                // In Phase 4-6, Drone shield is completely disabled
                shieldActive = false;
                return;
            }

            if (shieldActive)
            {
                // Check if any drone exists
                bool droneAlive = false;
                int droneType = ModContent.Find<ModNPC>("CalamityMod/PlagueChargerLarge").Type;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i].active && Main.npc[i].type == droneType && Main.npc[i].ai[0] == npc.whoAmI)
                    {
                        droneAlive = true;
                        break;
                    }
                }

                if (!droneAlive)
                {
                    // Stun state initiated
                    shieldActive = false;
                    shieldStunTimer = 480; // 8 seconds stun
                    npc.velocity = new Vector2(0, 1.5f);
                    SoundEngine.PlaySound(SoundID.NPCHit53, npc.Center);
                }
            }
            else
            {
                if (shieldStunTimer > 0)
                {
                    shieldStunTimer--;
                    npc.velocity *= 0.95f;
                    npc.defense = 0;
                    if (shieldStunTimer == 0)
                    {
                        shieldRegenTimer = 1500; // 25s weak period before regeneration
                    }
                }
                else if (shieldRegenTimer > 0)
                {
                    shieldRegenTimer--;
                    if (shieldRegenTimer == 0)
                    {
                        shieldActive = true;
                        SoundEngine.PlaySound(ShieldRegenSound, npc.Center);
                        // Respawn 6 chargers
                        if (Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            int type = ModContent.Find<ModNPC>("CalamityMod/PlagueChargerLarge").Type;
                            for (int i = 0; i < 6; i++)
                            {
                                int minion = NPC.NewNPC(npc.GetSource_FromAI(), (int)npc.Center.X, (int)npc.Center.Y, type);
                                if (minion >= 0 && minion < Main.maxNPCs)
                                {
                                    Main.npc[minion].ai[0] = npc.whoAmI;
                                    Main.npc[minion].netUpdate = true;
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region Attack Rotations
        private void RotateAttack(NPC npc, int currentPhase, AttackState current)
        {
            currentRepetition++;
            if (currentPhase <= 3)
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
                        AttackState.Virulence => AttackState.Malevolence,
                        AttackState.Malevolence => AttackState.PlagueStaff,
                        AttackState.PlagueStaff => AttackState.FuelCellBundle,
                        AttackState.FuelCellBundle => AttackState.InfectedRemote,
                        AttackState.InfectedRemote => AttackState.TheSyringe,
                        _ => AttackState.Virulence
                    };
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
                    AttackState.TheHive => AttackState.PestilentDefiler,
                    AttackState.PestilentDefiler => AttackState.Malachite,
                    AttackState.Malachite => AttackState.BlightSpewer,
                    AttackState.BlightSpewer => AttackState.Pandemic,
                    AttackState.Pandemic => AttackState.PlagueTaintedSMG,
                    _ => AttackState.TheHive
                };
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
            }
            npc.netUpdate = true;
        }
        #endregion

        #region State Machine Implementations
        private void ExecuteVirulence(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -280f), timer < 40 ? 12f : 3f, 20f);

            // Every 80 frames, slice Virulence sword
            if (timer == 60 || timer == 140)
            {
                int dmg = npc.damage / 3;
                Vector2 dir = SafeNormalize(target.Center - npc.Center, Vector2.UnitY);
                int idx = SpawnHostile(npc, npc.Center, dir * 4f, "Projectiles/Boss/VirulentWave", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].timeLeft = 300;
                    Main.projectile[idx].ai[0] = 1f; // splits after 120px
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.Virulence);
            }
        }

        private void ExecuteMalevolence(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(-350f, -240f), 10f, 15f);

            if (timer == 40)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 8; i++)
                {
                    Vector2 spawnPos = target.Center + new Vector2(Main.rand.NextFloat(-300f, 300f), -480f);
                    int idx = SpawnHostile(npc, spawnPos, new Vector2(0f, 1f), "Projectiles/Boss/PlagueArrow", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = i * 10 + 20; // align delay
                        Main.projectile[idx].timeLeft = 320;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.Malevolence);
            }
        }

        private void ExecutePlagueStaff(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(250f, -250f), 8f, 18f);

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                Vector2[] offsets = { new(-240f, 240f), new(240f, 240f), new(0f, -320f) };
                foreach (Vector2 off in offsets)
                {
                    Vector2 spawn = target.Center + off;
                    int idx = SpawnHostile(npc, spawn, Vector2.Zero, "Projectiles/Boss/PlagueFang", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 45; // trigger delay
                        Main.projectile[idx].timeLeft = 180;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.PlagueStaff);
            }
        }

        private void ExecuteFuelCell(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -340f), 14f, 12f);

            if (timer == 50 || timer == 100 || timer == 150)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = new(Main.rand.NextFloat(-6f, 6f), 8f);
                SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/FuelCell", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.FuelCellBundle);
            }
        }

        private void ExecuteInfectedRemote(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(-400f, -260f), 9f, 22f);

            if (timer == 40)
            {
                int dmg = npc.damage / 3;
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int virili = NPC.NewNPC(npc.GetSource_FromAI(), (int)target.Center.X - 500, (int)target.Center.Y - 320, ModContent.Find<ModNPC>("CalamityMod/PlaguePrincess").Type);
                    if (virili >= 0 && virili < Main.maxNPCs)
                    {
                        Main.npc[virili].velocity = new Vector2(12f, 0f);
                        Main.npc[virili].ai[0] = npc.whoAmI;
                        Main.npc[virili].netUpdate = true;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.InfectedRemote);
            }
        }

        private void ExecuteTheSyringe(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(300f, -200f), 11f, 15f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 22f;
                int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/TheSyringe", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // embed & shatter
                    Main.projectile[idx].timeLeft = 240;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.TheSyringe);
            }
        }

        private void ExecuteTheHive(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -280f), 13f, 16f);

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 3f;
                int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/HiveNuke", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].timeLeft = 360;
                    Main.projectile[idx].ai[0] = 1f; // radial split trigger
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.TheHive);
            }
        }

        private void ExecutePestilentDefiler(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(-280f, -240f), 11f, 20f);

            if (timer == 40 || timer == 90 || timer == 140)
            {
                int dmg = npc.damage / 3;
                Vector2 dir = SafeNormalize(target.Center - npc.Center, Vector2.UnitY);
                for (int i = 0; i < 8; i++)
                {
                    Vector2 vel = dir.RotatedBy(MathHelper.Lerp(-0.25f, 0.25f, i / 7f)) * 14f;
                    int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/PestilentBullet", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 1f; // sin path trigger
                        Main.projectile[idx].timeLeft = 180;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.PestilentDefiler);
            }
        }

        private void ExecuteMalachite(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(300f, -220f), 9f, 22f);

            if (timer >= 60 && timer <= 180 && (timer - 60) % 10 == 0)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 20f;
                SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/MalachiteDagger", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.Malachite);
            }
        }

        private void ExecuteBlightSpewer(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -320f), 8f, 24f);

            if (timer >= 50 && timer <= 170 && timer % 4 == 0)
            {
                int dmg = npc.damage / 3;
                float angle = MathHelper.Lerp(-MathHelper.PiOver2, MathHelper.PiOver2, (timer - 50f) / 120f);
                Vector2 vel = angle.ToRotationVector2().RotatedBy(MathHelper.PiOver2) * 12f;
                SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/BlightFlame", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.BlightSpewer);
            }
        }

        private void ExecutePandemic(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -280f), 10f, 20f);

            if (timer == 40)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 2; i++)
                {
                    float angle = i * MathHelper.Pi;
                    int idx = SpawnHostile(npc, target.Center + angle.ToRotationVector2() * 160f, Vector2.Zero, "Projectiles/Boss/PandemicYoyo", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 160f; // orbit radius
                        Main.projectile[idx].ai[1] = angle;
                        Main.projectile[idx].timeLeft = 180;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.Pandemic);
            }
        }

        private void ExecutePlagueTaintedSMG(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -240f), 8f, 22f);

            if (timer == 40)
            {
                // Spawn 4 corner drones
                int type = ModContent.Find<ModNPC>("CalamityMod/PlagueTaintedDrone").Type;
                Vector2[] corners = { new(-500f, -500f), new(500f, -500f), new(-500f, 500f), new(500f, 500f) };
                foreach (Vector2 c in corners)
                {
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        int minion = NPC.NewNPC(npc.GetSource_FromAI(), (int)target.Center.X + (int)c.X, (int)target.Center.Y + (int)c.Y, type);
                        if (minion >= 0 && minion < Main.maxNPCs)
                        {
                            Main.npc[minion].ai[0] = npc.whoAmI;
                            Main.npc[minion].netUpdate = true;
                        }
                    }
                }
            }

            if (timer >= 80 && timer <= 180 && timer % 6 == 0)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 14f;
                SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/PestilentBullet", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.PlagueTaintedSMG);
            }
        }

        private void ExecuteOverloadTransition(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            npc.velocity *= 0.9f;

            if (timer == 1)
            {
                SoundEngine.PlaySound(SoundID.NPCDeath10, npc.Center);
                target.Calamity().GeneralScreenShakePower = 8f;
                // kill remaining drones
                int droneType = ModContent.Find<ModNPC>("CalamityMod/PlagueChargerLarge").Type;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i].active && Main.npc[i].type == droneType && Main.npc[i].ai[0] == npc.whoAmI)
                    {
                        Main.npc[i].active = false;
                    }
                }
            }

            if (timer >= 90)
            {
                // Re-initialize to P2 loop
                AttackState next = AttackState.TheHive;
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
            Texture2D tex = TextureAssets.Npc[npc.type].Value;
            Rectangle frame = npc.frame;
            Vector2 origin = frame.Size() / 2f;

            for (int i = 0; i < oldPositions.Length; i++)
            {
                int idx = (oldPositionsIndex - i - 1 + oldPositions.Length) % oldPositions.Length;
                if (oldPositions[idx] == Vector2.Zero) continue;
                float alpha = (1f - i / (float)oldPositions.Length) * 0.55f;
                Color trailColor = new Color(88, 210, 60, 0) * alpha;
                spriteBatch.Draw(tex, oldPositions[idx] - screenPos, frame, trailColor, npc.rotation, origin, npc.scale, SpriteEffects.None, 0f);
            }

            return true;
        }

        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            Texture2D tex = TextureAssets.Npc[npc.type].Value;
            Rectangle frame = npc.frame;
            Vector2 origin = frame.Size() / 2f;

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);

            Color glowColor = new Color(88, 210, 60, 0) * 0.35f;
            spriteBatch.Draw(tex, npc.Center - screenPos, frame, glowColor, npc.rotation, origin, npc.scale * 1.08f, SpriteEffects.None, 0f);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
        }
        #endregion
    }
}
