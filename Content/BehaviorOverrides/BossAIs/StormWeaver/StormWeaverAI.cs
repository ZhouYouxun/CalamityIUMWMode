using System;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.WeaponAttacks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.StormWeaver
{
    internal sealed class StormWeaverAI : IUMWBossAI
    {
        #region Constants & Configurations
        public override int NPCType => ModContent.Find<ModNPC>("CalamityMod/StormWeaverHead").Type;
        public override string BossName => "Storm Weaver";
        public override Color DebugColor => new(255, 120, 200);

        public override int MaxPhaseCount => 3;
        public override float[] PhaseLifeRatios => new[] { 0.80f, 0.40f };
        public override int AttackCycleLength => 120;
        public override float MotionIntensity => 1.25f;
        #endregion

        #region Attack States
        public enum AttackState
        {
            // P1 Attacks (3x Repeat)
            SkytideDragoon = 0,
            Storm = 1,
            Volterion = 2,

            // P2/P3 Attacks (No 3x Repeat)
            AquasScepter = 3,
            StellarTorus = 4,
            TwistingThunder = 5,
            ShadowboltStaff = 6,
            FourSeasons = 7,
            Transition = 8
        }
        #endregion

        #region Fields
        private int ticksRunning = 0;
        private int currentRepetition = 0;

        // Tesla link
        private int teslaLinkTimer = 0;
        #endregion

        #region Core AI Hooks
        public override bool PreAI(NPC npc, IUMWGlobalNPC data)
        {
            ticksRunning++;

            int headType = ModContent.Find<ModNPC>("CalamityMod/StormWeaverHead").Type;
            int bodyType = ModContent.Find<ModNPC>("CalamityMod/StormWeaverBody").Type;
            int tailType = ModContent.Find<ModNPC>("CalamityMod/StormWeaverTail").Type;

            // Return true for body/tail segments to let vanilla link them
            if (npc.type != headType)
                return true;

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

            // Re-normalize phase/state
            if (currentPhase == 0)
            {
                currentPhase = 1;
                npc.ai[0] = 1f;
                state = AttackState.SkytideDragoon;
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
                state = AttackState.Transition;
                npc.ai[1] = (float)state;
                timer = 0;
                stateTracker = 0;
                npc.netUpdate = true;
            }

            // Storm Rain Wing Time Drain
            UpdateWingDrain(target);

            // Polar Tesla Link
            UpdateTeslaLink(npc, target, tailType);

            // Head movement profile
            float baseSpeed = currentPhase == 1 ? 14f : 22f;
            float speed = baseSpeed + (1f - lifeRatio) * 6f;
            float turnSpeed = 0.045f + (1f - lifeRatio) * 0.03f;
            Vector2 desiredVel = SafeNormalize(target.Center - npc.Center, Vector2.Zero) * speed;
            npc.velocity = Vector2.Lerp(npc.velocity, desiredVel, turnSpeed);
            npc.rotation = npc.velocity.ToRotation() + MathHelper.PiOver2;

            // Execute state machine
            switch (state)
            {
                case AttackState.SkytideDragoon:
                    ExecuteSkytideDragoon(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.Storm:
                    ExecuteStorm(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.Volterion:
                    ExecuteVolterion(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.AquasScepter:
                    ExecuteAquasScepter(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.StellarTorus:
                    ExecuteStellarTorus(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.TwistingThunder:
                    ExecuteTwistingThunder(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.ShadowboltStaff:
                    ExecuteShadowboltStaff(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.FourSeasons:
                    ExecuteFourSeasons(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.Transition:
                    ExecuteTransition(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
            }

            return false;
        }

        public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers)
        {
            int tailType = ModContent.Find<ModNPC>("CalamityMod/StormWeaverTail").Type;

            // In P1, only tail takes damage (head and body have 99.9% DR)
            if (npc.ai[0] == 1f && npc.type != tailType)
            {
                modifiers.FinalDamage *= 0.001f;
            }
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            int tailType = ModContent.Find<ModNPC>("CalamityMod/StormWeaverTail").Type;

            if (npc.ai[0] == 1f && npc.type != tailType)
            {
                modifiers.FinalDamage *= 0.001f;
            }
        }
        #endregion

        #region Helpers
        private void UpdateWingDrain(Player player)
        {
            if (player.velocity.Y != 0f) // airborne
            {
                // check platform safety
                bool safe = false;
                int tileX = (int)(player.Center.X / 16f);
                int tileY = (int)(player.Center.Y / 16f);
                for (int y = tileY; y > tileY - 12; y--)
                {
                    if (WorldGen.InWorld(tileX, y) && Main.tile[tileX, y].HasTile && (Main.tileSolid[Main.tile[tileX, y].TileType] || Main.tileSolidTop[Main.tile[tileX, y].TileType]))
                    {
                        safe = true;
                        break;
                    }
                }

                if (!safe && player.wingTime > 0f)
                {
                    player.wingTime -= player.wingTimeMax * 0.0066f; // 40%/s drain
                    Dust.NewDust(player.position, player.width, player.height, DustID.Electric, 0f, 0f, 100, default, 1f);
                }
            }
        }

        private void UpdateTeslaLink(NPC npc, Player target, int tailType)
        {
            // Scan for tail segment
            NPC tail = null;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                if (Main.npc[i].active && Main.npc[i].type == tailType)
                {
                    tail = Main.npc[i];
                    break;
                }
            }

            if (tail != null)
            {
                Vector2 headPos = npc.Center;
                Vector2 tailPos = tail.Center;

                // Check collision of player against head-tail line segment
                Vector2 ab = tailPos - headPos;
                Vector2 ac = target.Center - headPos;
                float abLen = ab.Length();
                if (abLen > 0f)
                {
                    float proj = Vector2.Dot(ac, ab) / abLen;
                    proj = Math.Clamp(proj, 0f, abLen);
                    Vector2 closest = headPos + SafeNormalize(ab, Vector2.Zero) * proj;
                    if (Vector2.Distance(target.Center, closest) < 20f)
                    {
                        target.AddBuff(BuffID.Electrified, 120); // Tesla shock: slow & damage
                        target.velocity *= 0.5f;
                    }
                }
            }
        }
        #endregion

        #region Attack Rotations
        private void RotateAttack(NPC npc, int currentPhase, AttackState current)
        {
            currentRepetition++;
            if (currentPhase == 1)
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
                        AttackState.SkytideDragoon => AttackState.Storm,
                        AttackState.Storm => AttackState.Volterion,
                        _ => AttackState.SkytideDragoon
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
                    AttackState.AquasScepter => AttackState.StellarTorus,
                    AttackState.StellarTorus => AttackState.TwistingThunder,
                    AttackState.TwistingThunder => AttackState.ShadowboltStaff,
                    AttackState.ShadowboltStaff => AttackState.FourSeasons,
                    _ => AttackState.AquasScepter
                };
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
            }
            npc.netUpdate = true;
        }
        #endregion

        #region P1 Attacks
        private void ExecuteSkytideDragoon(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                Vector2 dir = SafeNormalize(target.Center - npc.Center, Vector2.UnitY);
                // Zig-zag charge, leaves crystal rails
                int idx = SpawnHostile(npc, npc.Center, dir * 18f, "Projectiles/Boss/SkytideLaser", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // explodes on wall hit
                    Main.projectile[idx].timeLeft = 140;
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.SkytideDragoon);
            }
        }

        private void ExecuteStorm(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // 4 lightning nodes sequentially flashing
                for (int i = 0; i < 4; i++)
                {
                    Vector2 pos = target.Center + new Vector2(i * 160f - 240f, Main.rand.NextFloat(-150f, 150f));
                    int idx = SpawnHostile(npc, pos, Vector2.Zero, "Projectiles/Boss/WeaverStormNode", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = i * 15 + 10; // flash trigger delay
                        Main.projectile[idx].timeLeft = 160;
                    }
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.Storm);
            }
        }

        private void ExecuteVolterion(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 3f;
                int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/VolterionSphere", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // pull force on target + 12-way split
                    Main.projectile[idx].timeLeft = 220;
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.Volterion);
            }
        }
        #endregion

        #region P2/P3 Attacks
        private void ExecuteAquasScepter(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer >= 50 && timer <= 140 && timer % 4 == 0)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY).RotatedBy(Main.rand.NextFloat(-0.12f, 0.12f)) * 14f;
                SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/AquasScepterSteam", dmg);
            }

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 3; i++)
                {
                    Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY).RotatedBy((i - 1) * 0.25f) * 11f;
                    int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/CorinthPrimeNuke", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 1f; // splits on boundary hit
                        Main.projectile[idx].timeLeft = 180;
                    }
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.AquasScepter);
            }
        }

        private void ExecuteStellarTorus(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                int idx = SpawnHostile(npc, target.Center, Vector2.Zero, "Projectiles/Boss/StellarTorusRing", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].timeLeft = 150;
                }
            }

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                // conduction line to nearest platform
                int idx = SpawnHostile(npc, npc.Center, SafeNormalize(target.Center - npc.Center, Vector2.Zero) * 14f, "Projectiles/Boss/WeaverConductionBolt", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].timeLeft = 150;
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.StellarTorus);
            }
        }

        private void ExecuteTwistingThunder(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // double helix electric line
                SpawnHostile(npc, npc.Center, SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 11f, "Projectiles/Boss/TwistingThunderHelix", dmg);

                // 12 tracking wolf rockets
                for (int i = 0; i < 12; i++)
                {
                    Vector2 vel = (i * MathHelper.TwoPi / 12f).ToRotationVector2() * 6f;
                    SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/PackRocket", dmg);
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.TwistingThunder);
            }
        }

        private void ExecuteShadowboltStaff(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // dark cloud ceiling
                for (int i = 0; i < 4; i++)
                {
                    Vector2 cloudPos = target.Center + new Vector2(i * 180f - 270f, -420f);
                    SpawnHostile(npc, cloudPos, Vector2.Zero, "Projectiles/Boss/WeaverDarkCloud", dmg);
                }
            }

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                // giant water wall phantom sweep
                int idx = SpawnHostile(npc, target.Center + new Vector2(-450f, 0f), new Vector2(12f, 0f), "Projectiles/Boss/SeadragonWaterWall", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].timeLeft = 120;
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.ShadowboltStaff);
            }
        }

        private void ExecuteFourSeasons(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // 4 seasons colored stars
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * MathHelper.PiOver2;
                    int idx = SpawnHostile(npc, target.Center + angle.ToRotationVector2() * 240f, Vector2.Zero, "Projectiles/Boss/WeaverSeasonStar", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = i; // color selector
                        Main.projectile[idx].timeLeft = 150;
                    }
                }
            }

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                // reality rupture space rift
                int idx = SpawnHostile(npc, target.Center, Vector2.Zero, "Projectiles/Boss/WeaverSpaceRift", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].timeLeft = 120;
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.FourSeasons);
            }
        }

        private void ExecuteTransition(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            npc.velocity *= 0.85f;

            if (timer == 45)
            {
                SoundEngine.PlaySound(SoundID.NPCDeath10, npc.Center);
            }

            if (timer >= 90)
            {
                AttackState next = AttackState.AquasScepter;
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
                npc.netUpdate = true;
            }
        }
        #endregion
    }
}
