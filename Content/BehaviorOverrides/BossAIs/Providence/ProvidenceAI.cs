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

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Providence
{
    internal sealed class ProvidenceAI : IUMWBossAI
    {
        #region Constants & Configurations
        public override int NPCType => ModContent.Find<ModNPC>("CalamityMod/Providence").Type;
        public override string BossName => "Providence";
        public override Color DebugColor => new(255, 220, 60);

        public override int MaxPhaseCount => 3;
        public override float[] PhaseLifeRatios => new[] { 0.70f, 0.35f };
        public override int AttackCycleLength => 120;
        public override float MotionIntensity => 0.9f;
        #endregion

        #region Attack States
        public enum AttackState
        {
            // P1 Attacks (3x Repeat)
            HolyCollider = 0,
            BurningRevelation = 1,
            TelluricGlare = 2,
            BlissfulBombardier = 3,
            PurgeGuzzler = 4,
            DazzlingStabber = 5,
            MoltenAmputator = 6,
            PristineFury = 7,

            // P2 Attacks
            AetherfluxCannon = 8,
            DarkSpark = 9,
            MirrorOfKalandra = 10,
            ShatteredDawn = 11,
            Maelstrom = 12,
            Transition = 13
        }
        #endregion

        #region Fields
        private int ticksRunning = 0;
        private int currentRepetition = 0;

        // Tri-Source Crystals HP
        private float yellowCrystalHP = 800f;
        private float orangeCrystalHP = 800f;
        private float purpleCrystalHP = 800f;

        private int stunTimer = 0;
        private int respawnCrystalsTimer = 0;

        // Bounding Cage and refraction laser
        private int refractionTimer = 0;
        #endregion

        #region Core AI Hooks
        public override bool PreAI(NPC npc, IUMWGlobalNPC data)
        {
            ticksRunning++;

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
                state = AttackState.HolyCollider;
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

            // Crystal Cage Bounding Box (1500px in P1, 1100px in P2/P3)
            float borderSize = currentPhase == 1 ? 1500f : 1100f;
            Vector2 dist = target.Center - npc.Center;
            if (Math.Abs(dist.X) > borderSize / 2f || Math.Abs(dist.Y) > borderSize / 2f)
            {
                target.AddBuff(BuffID.Daybreak, 180);
                target.AddBuff(BuffID.Obscured, 120); // Profaned Weakness: 0 defense
                target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(npc.whoAmI), 25, 0);
            }

            // Refraction Laser (Every 8 seconds / 480 frames)
            UpdateRefractionLaser(npc, target, borderSize);

            // Orbiting crystals respawn check
            UpdateCrystalsRespawn();

            // Mirror of Kalandra projectile reflection check (only in P2/P3)
            if (currentPhase > 1)
            {
                UpdateMirrorReflection(npc, target);
            }

            // Stun and movement
            if (stunTimer > 0)
            {
                stunTimer--;
                npc.velocity *= 0.85f;
            }
            else
            {
                float speed = 9f + (1f - lifeRatio) * 5f;
                Vector2 desiredPos = target.Center + new Vector2((float)Math.Sin(ticksRunning * 0.04f) * 200f, -220f);
                Vector2 desiredVel = (desiredPos - npc.Center) * 0.04f;
                if (desiredVel.Length() > speed) desiredVel = SafeNormalize(desiredVel, Vector2.Zero) * speed;
                npc.velocity = Vector2.Lerp(npc.velocity, desiredVel, 0.12f);
            }
            npc.rotation = npc.velocity.X * 0.03f;

            // Execute state machine
            if (stunTimer == 0)
            {
                switch (state)
                {
                    case AttackState.HolyCollider:
                        ExecuteHolyCollider(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.BurningRevelation:
                        ExecuteBurningRevelation(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.TelluricGlare:
                        ExecuteTelluricGlare(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.BlissfulBombardier:
                        ExecuteBlissfulBombardier(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.PurgeGuzzler:
                        ExecutePurgeGuzzler(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.DazzlingStabber:
                        ExecuteDazzlingStabber(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.MoltenAmputator:
                        ExecuteMoltenAmputator(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.PristineFury:
                        ExecutePristineFury(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.AetherfluxCannon:
                        ExecuteAetherfluxCannon(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.DarkSpark:
                        ExecuteDarkSpark(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.MirrorOfKalandra:
                        ExecuteMirrorOfKalandra(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.ShatteredDawn:
                        ExecuteShatteredDawn(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.Maelstrom:
                        ExecuteMaelstrom(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.Transition:
                        ExecuteTransition(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                }
            }

            return false;
        }

        public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers)
        {
            ProcessCrystalHits(npc, player.Center, ref modifiers, item.damage);
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            ProcessCrystalHits(npc, projectile.Center, ref modifiers, projectile.damage);
        }
        #endregion

        #region Refraction & Crystals Logic
        private void UpdateRefractionLaser(NPC npc, Player target, float borderSize)
        {
            refractionTimer++;
            if (refractionTimer >= 480)
            {
                refractionTimer = 0;
            }

            if (refractionTimer >= 420 && refractionTimer < 480)
            {
                // Refraction lines warning & blast
                int mode = refractionTimer - 420;
                if (mode == 0)
                {
                    SoundEngine.PlaySound(SoundID.Item60, npc.Center);
                }

                // 4 corners
                Vector2 topLeft = npc.Center + new Vector2(-borderSize / 2f, -borderSize / 2f);
                Vector2 topRight = npc.Center + new Vector2(borderSize / 2f, -borderSize / 2f);
                Vector2 bottomLeft = npc.Center + new Vector2(-borderSize / 2f, borderSize / 2f);
                Vector2 bottomRight = npc.Center + new Vector2(borderSize / 2f, borderSize / 2f);

                // Check collision of player against X lines
                if (mode >= 45) // laser active for last 0.25s
                {
                    // Check intersection
                    if (Collision.CheckAABBvLineCollision(target.position, target.Size, topLeft, bottomRight) ||
                        Collision.CheckAABBvLineCollision(target.position, target.Size, topRight, bottomLeft))
                    {
                        target.AddBuff(BuffID.Daybreak, 120);
                        target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(npc.whoAmI), 30, 0);
                    }
                }
            }
        }

        private void UpdateCrystalsRespawn()
        {
            if (yellowCrystalHP <= 0f && orangeCrystalHP <= 0f && purpleCrystalHP <= 0f && stunTimer == 0)
            {
                respawnCrystalsTimer++;
                if (respawnCrystalsTimer >= 1500) // 25s respawn
                {
                    yellowCrystalHP = 800f;
                    orangeCrystalHP = 800f;
                    purpleCrystalHP = 800f;
                    respawnCrystalsTimer = 0;
                }
            }
        }

        private void UpdateMirrorReflection(NPC npc, Player target)
        {
            // Center mirror coordinates
            Vector2 mirrorCenter = npc.Center + new Vector2(0f, 150f);

            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile proj = Main.projectile[i];
                if (proj.active && !proj.hostile && proj.owner == target.whoAmI)
                {
                    if (Vector2.Distance(proj.Center, mirrorCenter) < 100f)
                    {
                        proj.Kill();
                        // Reflect into a hostile spear targeting the player
                        int dmg = npc.damage / 3;
                        Vector2 vel = SafeNormalize(target.Center - mirrorCenter, Vector2.UnitY) * 12f;
                        SpawnHostile(npc, mirrorCenter, vel, "Projectiles/Boss/KalandraSpear", dmg);
                        SoundEngine.PlaySound(SoundID.Item28, mirrorCenter);
                    }
                }
            }
        }

        private void ProcessCrystalHits(NPC npc, Vector2 hitPos, ref NPC.HitModifiers modifiers, int damage)
        {
            // Calculate coordinates of the 3 orbiting crystals
            Vector2 yellowPos = npc.Center + (ticksRunning * 0.03f).ToRotationVector2() * 120f;
            Vector2 orangePos = npc.Center + (ticksRunning * 0.03f + MathHelper.TwoPi / 3f).ToRotationVector2() * 120f;
            Vector2 purplePos = npc.Center + (ticksRunning * 0.03f + 2f * MathHelper.TwoPi / 3f).ToRotationVector2() * 120f;

            float dYellow = Vector2.Distance(hitPos, yellowPos);
            float dOrange = Vector2.Distance(hitPos, orangePos);
            float dPurple = Vector2.Distance(hitPos, purplePos);

            // Determine active crystals count for DR
            int activeCount = 0;
            if (yellowCrystalHP > 0f) activeCount++;
            if (orangeCrystalHP > 0f) activeCount++;
            if (purpleCrystalHP > 0f) activeCount++;

            if (activeCount > 0)
            {
                modifiers.FinalDamage *= (1f - 0.30f * activeCount); // Up to 90% DR
            }

            // Redirect damage to the closest active crystal if within range
            if (dYellow < 80f && yellowCrystalHP > 0f)
            {
                yellowCrystalHP -= damage;
                if (yellowCrystalHP <= 0f)
                {
                    SoundEngine.PlaySound(SoundID.NPCDeath4, yellowPos);
                    CheckAllCrystalsBroken(npc);
                }
            }
            else if (dOrange < 80f && orangeCrystalHP > 0f)
            {
                orangeCrystalHP -= damage;
                if (orangeCrystalHP <= 0f)
                {
                    SoundEngine.PlaySound(SoundID.NPCDeath4, orangePos);
                    CheckAllCrystalsBroken(npc);
                }
            }
            else if (dPurple < 80f && purpleCrystalHP > 0f)
            {
                purpleCrystalHP -= damage;
                if (purpleCrystalHP <= 0f)
                {
                    SoundEngine.PlaySound(SoundID.NPCDeath4, purplePos);
                    CheckAllCrystalsBroken(npc);
                }
            }
        }

        private void CheckAllCrystalsBroken(NPC npc)
        {
            if (yellowCrystalHP <= 0f && orangeCrystalHP <= 0f && purpleCrystalHP <= 0f)
            {
                stunTimer = 480; // 8s stun
                npc.velocity = Vector2.Zero;
                SoundEngine.PlaySound(SoundID.NPCHit53, npc.Center);
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
                        AttackState.HolyCollider => AttackState.BurningRevelation,
                        AttackState.BurningRevelation => AttackState.TelluricGlare,
                        AttackState.TelluricGlare => AttackState.BlissfulBombardier,
                        AttackState.BlissfulBombardier => AttackState.PurgeGuzzler,
                        AttackState.PurgeGuzzler => AttackState.DazzlingStabber,
                        AttackState.DazzlingStabber => AttackState.MoltenAmputator,
                        AttackState.MoltenAmputator => AttackState.PristineFury,
                        _ => AttackState.HolyCollider
                    };

                    // Check disabled weapons due to crystal breaks
                    if (next == AttackState.TelluricGlare && yellowCrystalHP <= 0f) next = AttackState.BlissfulBombardier;
                    if (next == AttackState.PristineFury && yellowCrystalHP <= 0f) next = AttackState.HolyCollider;
                    if (next == AttackState.MoltenAmputator && orangeCrystalHP <= 0f) next = AttackState.HolyCollider;
                    if (next == AttackState.BlissfulBombardier && orangeCrystalHP <= 0f) next = AttackState.PurgeGuzzler;
                    if (next == AttackState.PurgeGuzzler && purpleCrystalHP <= 0f) next = AttackState.HolyCollider;
                    if (next == AttackState.DazzlingStabber && purpleCrystalHP <= 0f) next = AttackState.MoltenAmputator;

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
                    AttackState.AetherfluxCannon => AttackState.DarkSpark,
                    AttackState.DarkSpark => AttackState.MirrorOfKalandra,
                    AttackState.MirrorOfKalandra => AttackState.ShatteredDawn,
                    AttackState.ShatteredDawn => AttackState.Maelstrom,
                    _ => AttackState.AetherfluxCannon
                };
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
            }
            npc.netUpdate = true;
        }
        #endregion

        #region P1 Attack States
        private void ExecuteHolyCollider(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                Vector2 dir = SafeNormalize(target.Center - npc.Center, Vector2.UnitY);
                // Swing leaves warning path and detonates columns of holy fire
                int idx = SpawnHostile(npc, npc.Center, dir * 14f, "Projectiles/Boss/HolyColliderSword", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // trails fire sparks
                    Main.projectile[idx].timeLeft = 120;
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.HolyCollider);
            }
        }

        private void ExecuteBurningRevelation(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // Outer ring
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * MathHelper.TwoPi / 12f;
                    SpawnHostile(npc, target.Center + angle.ToRotationVector2() * 320f, angle.ToRotationVector2() * 4f, "Projectiles/Boss/BurningRevelationFire", dmg);
                }
                // Inner ring contracting
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * MathHelper.TwoPi / 8f;
                    int idx = SpawnHostile(npc, target.Center + angle.ToRotationVector2() * 240f, -angle.ToRotationVector2() * 3f, "Projectiles/Boss/BurningRevelationFire", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].timeLeft = 150;
                    }
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.BurningRevelation);
            }
        }

        private void ExecuteTelluricGlare(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // 4 parallel laser paths
                for (int i = 0; i < 4; i++)
                {
                    Vector2 pos = target.Center + new Vector2(-500f, i * 100f - 150f);
                    SpawnHostile(npc, pos, new Vector2(16f, 0f), "Projectiles/Boss/TelluricGlareArrow", dmg);
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.TelluricGlare);
            }
        }

        private void ExecuteBlissfulBombardier(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 8f;
                int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/BlissfulBombardierRocket", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // splits on target approach
                    Main.projectile[idx].timeLeft = 180;
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.BlissfulBombardier);
            }
        }

        private void ExecutePurgeGuzzler(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 3; i++)
                {
                    float angle = i * MathHelper.TwoPi / 3f;
                    Vector2 pos = target.Center + angle.ToRotationVector2() * 240f;
                    int idx = SpawnHostile(npc, pos, Vector2.Zero, "Projectiles/Boss/PurgeGuzzlerCore", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 1f; // lasers cross center
                        Main.projectile[idx].timeLeft = 120;
                    }
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.PurgeGuzzler);
            }
        }

        private void ExecuteDazzlingStabber(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                Vector2 pos = target.Center + new Vector2(0f, -420f);
                int idx = SpawnHostile(npc, pos, new Vector2(0f, 15f), "Projectiles/Boss/DazzlingStabberSpear", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // creates golden barrier fall on collision
                    Main.projectile[idx].timeLeft = 140;
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.DazzlingStabber);
            }
        }

        private void ExecuteMoltenAmputator(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 11f;
                int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/MoltenAmputatorSickle", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // drops gravity sparks trailing lava pools
                    Main.projectile[idx].timeLeft = 200;
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.MoltenAmputator);
            }
        }

        private void ExecutePristineFury(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer >= 50 && timer <= 140 && timer % 5 == 0)
            {
                int dmg = npc.damage / 3;
                float angle = MathHelper.Lerp(-0.7f, 0.7f, (timer - 50f) / 90f);
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY).RotatedBy(angle) * 9f;
                SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/PristineFuryFlame", dmg);
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.PristineFury);
            }
        }
        #endregion

        #region P2 Attack States
        private void ExecuteAetherfluxCannon(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50 || timer == 80 || timer == 110)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 12f;
                int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/AetherfluxLaser", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // tracks player slightly on arc
                    Main.projectile[idx].timeLeft = 140;
                }
            }

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 6; i++)
                {
                    Vector2 vel = (i * MathHelper.TwoPi / 6f).ToRotationVector2() * 8f;
                    int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/AngelicShotgunShot", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 1f; // bouncing check on cage boundary
                        Main.projectile[idx].timeLeft = 150;
                    }
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.AetherfluxCannon);
            }
        }

        private void ExecuteDarkSpark(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                int idx = SpawnHostile(npc, target.Center + new Vector2(Main.rand.NextFloat(-200f, 200f), -200f), Vector2.Zero, "Projectiles/Boss/ProvulenceDarkSpark", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].timeLeft = 160;
                }
            }

            if (timer >= 60 && timer <= 120 && timer % 5 == 0)
            {
                int dmg = npc.damage / 3;
                Vector2 spawn = target.Center + new Vector2(Main.rand.NextFloat(-350f, 350f), -450f);
                SpawnHostile(npc, spawn, new Vector2(0f, 14f), "Projectiles/Boss/GalactusBladeSword", dmg);
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.DarkSpark);
            }
        }

        private void ExecuteMirrorOfKalandra(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // Double spiral Mourningstar firelines
                for (int i = 0; i < 2; i++)
                {
                    int idx = SpawnHostile(npc, npc.Center, Vector2.Zero, "Projectiles/Boss/MourningstarLine", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = i; // opposite phases
                        Main.projectile[idx].timeLeft = 160;
                    }
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.MirrorOfKalandra);
            }
        }

        private void ExecuteShatteredDawn(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                int idx = SpawnHostile(npc, npc.Center, SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 6f, "Projectiles/Boss/ShatteredDawnDisc", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // splits into 24-way blast
                    Main.projectile[idx].timeLeft = 150;
                }
            }

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                int idx = SpawnHostile(npc, target.Center, Vector2.Zero, "Projectiles/Boss/SeekingScorcherRing", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].timeLeft = 140;
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.ShatteredDawn);
            }
        }

        private void ExecuteMaelstrom(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // Maelstrom suction center
                int idx = SpawnHostile(npc, npc.Center, Vector2.Zero, "Projectiles/Boss/MaelstromVortex", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].timeLeft = 160;
                }

                // Spawn Prince helper golem (Calamity mini-guardian)
                int princeType = ModContent.Find<ModNPC>("CalamityMod/ProvidenceGuardianOffensive").Type;
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int p = NPC.NewNPC(npc.GetSource_FromAI(), (int)npc.Center.X, (int)npc.Center.Y + 120, princeType);
                    if (p >= 0 && p < Main.maxNPCs)
                    {
                        Main.npc[p].ai[0] = npc.whoAmI;
                        Main.npc[p].netUpdate = true;
                    }
                }
            }

            if (timer >= 180)
            {
                RotateAttack(npc, phase, AttackState.Maelstrom);
            }
        }

        private void ExecuteTransition(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            npc.velocity *= 0.8f;

            if (timer == 45)
            {
                SoundEngine.PlaySound(SoundID.NPCDeath10, npc.Center);
            }

            if (timer >= 90)
            {
                AttackState next = AttackState.AetherfluxCannon;
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
                npc.netUpdate = true;
            }
        }
        #endregion

        #region Drawing
        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            // Draw the 3 orbiting crystals
            Texture2D glowTex = TextureAssets.Dust.Value; // simple helper texture (circle)
            Rectangle sourceRect = new Rectangle(0, 0, 8, 8);

            Vector2 yellowPos = npc.Center + (ticksRunning * 0.03f).ToRotationVector2() * 120f;
            Vector2 orangePos = npc.Center + (ticksRunning * 0.03f + MathHelper.TwoPi / 3f).ToRotationVector2() * 120f;
            Vector2 purplePos = npc.Center + (ticksRunning * 0.03f + 2f * MathHelper.TwoPi / 3f).ToRotationVector2() * 120f;

            if (yellowCrystalHP > 0f)
            {
                spriteBatch.Draw(glowTex, yellowPos - screenPos, sourceRect, Color.Yellow * 0.8f, 0f, new Vector2(4f, 4f), 4f, SpriteEffects.None, 0f);
            }
            if (orangeCrystalHP > 0f)
            {
                spriteBatch.Draw(glowTex, orangePos - screenPos, sourceRect, Color.Orange * 0.8f, 0f, new Vector2(4f, 4f), 4f, SpriteEffects.None, 0f);
            }
            if (purpleCrystalHP > 0f)
            {
                spriteBatch.Draw(glowTex, purplePos - screenPos, sourceRect, Color.Purple * 0.8f, 0f, new Vector2(4f, 4f), 4f, SpriteEffects.None, 0f);
            }
        }
        #endregion
    }
}
