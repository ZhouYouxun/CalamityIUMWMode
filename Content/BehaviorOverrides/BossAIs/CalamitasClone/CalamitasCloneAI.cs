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

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.CalamitasClone
{
    internal sealed class CalamitasCloneAI : IUMWBossAI
    {
        #region Constants & Configuration
        public override int NPCType => ModContent.NPCType<CalamityMod.NPCs.CalClone.CalamitasClone>();
        public override string BossName => "Calamitas Clone";
        public override Color DebugColor => new(220, 60, 60);

        public override int MaxPhaseCount => 4;
        public override float[] PhaseLifeRatios => new[] { 0.70f, 0.35f, 0.10f };
        public override int AttackCycleLength => 120;
        public override float MotionIntensity => 1.0f;
        #endregion

        #region Attack States
        public enum AttackState
        {
            Oblivion = 0,
            Animosity = 1,
            LashesOfChaos = 2,
            EntropysVigil = 3,
            CrushsawCrasher = 4,
            HavocsBreath = 5,
            DesperationOverload = 6,
            BrotherTransition = 7
        }
        #endregion

        #region Fields
        private int ticksRunning = 0;
        private int currentRepetition = 0;
        private readonly Vector2[] oldPositions = new Vector2[14];
        private int oldPositionsIndex;
        private Vector2 arenaCenter = Vector2.Zero;
        private bool centerSet = false;

        // Shield status
        private bool shieldActive = true;
        private int shieldRegenTimer = 0;
        private int shieldStunTimer = 0;
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

            if (!centerSet)
            {
                arenaCenter = npc.Center;
                centerSet = true;
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
                state = AttackState.Oblivion;
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
                if (currentPhase == 3)
                {
                    state = AttackState.BrotherTransition;
                }
                else if (currentPhase == 4)
                {
                    state = AttackState.DesperationOverload;
                }
                else
                {
                    state = AttackState.Oblivion;
                }
                npc.ai[1] = (float)state;
                timer = 0;
                stateTracker = 0;
                npc.netUpdate = true;
            }

            // Dynamic Arena Box Size
            float borderSize = 1400f;
            if (currentPhase == 2) borderSize = 1100f;
            else if (currentPhase == 3) borderSize = 900f;
            else if (currentPhase == 4) borderSize = 650f;

            // Player boundaries
            Vector2 dist = target.Center - arenaCenter;
            if (Math.Abs(dist.X) > borderSize / 2f || Math.Abs(dist.Y) > borderSize / 2f)
            {
                target.AddBuff(BuffID.OnFire, 180);
                target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(npc.whoAmI), 12, 0);
                // push player back
                if (Math.Abs(dist.X) > borderSize / 2f)
                {
                    target.velocity.X = -Math.Sign(dist.X) * 5f;
                }
                if (Math.Abs(dist.Y) > borderSize / 2f)
                {
                    target.velocity.Y = -Math.Sign(dist.Y) * 5f;
                }
            }

            // Projectile Reflections
            UpdateProjectiles(borderSize);

            // Shield / Orbiter Management in P1/P2
            UpdateSoulSeekers(npc, currentPhase);

            // Visual oscillations and breathing
            npc.rotation = npc.velocity.X * 0.04f;
            npc.scale = 1f + (float)Math.Sin(ticksRunning * 0.06f) * 0.03f;

            // State Machine
            switch (state)
            {
                case AttackState.Oblivion:
                    ExecuteOblivion(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.Animosity:
                    ExecuteAnimosity(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.LashesOfChaos:
                    ExecuteLashes(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.EntropysVigil:
                    ExecuteVigil(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.CrushsawCrasher:
                    ExecuteCrushsaw(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.HavocsBreath:
                    ExecuteHavoc(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.DesperationOverload:
                    ExecuteDesperation(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
                case AttackState.BrotherTransition:
                    ExecuteBrotherTransition(npc, target, ref timer, ref stateTracker, currentPhase);
                    break;
            }

            return false;
        }

        public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers)
        {
            if (shieldActive && npc.ai[0] <= 2)
            {
                modifiers.FinalDamage *= 0.05f; // 95% DR
            }
            if (npc.ai[1] == (float)AttackState.BrotherTransition)
            {
                modifiers.FinalDamage *= 0f; // immune
            }
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            if (shieldActive && npc.ai[0] <= 2)
            {
                modifiers.FinalDamage *= 0.05f; // 95% DR
            }
            if (npc.ai[1] == (float)AttackState.BrotherTransition)
            {
                modifiers.FinalDamage *= 0f; // immune
            }
        }
        #endregion

        #region Bouncing & Orbiter Systems
        private void UpdateProjectiles(float borderSize)
        {
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile proj = Main.projectile[i];
                if (proj.active && proj.hostile)
                {
                    Vector2 dist = proj.Center - arenaCenter;
                    bool bounced = false;

                    if (Math.Abs(dist.X) > borderSize / 2f)
                    {
                        proj.velocity.X = -proj.velocity.X;
                        bounced = true;
                    }
                    if (Math.Abs(dist.Y) > borderSize / 2f)
                    {
                        proj.velocity.Y = -proj.velocity.Y;
                        bounced = true;
                    }

                    if (bounced)
                    {
                        proj.localAI[0]++; // bounce count
                        if (proj.localAI[0] >= 2)
                        {
                            proj.Kill();
                        }
                        else
                        {
                            // Spawn warning dust on borders
                            for (int k = 0; k < 8; k++)
                            {
                                Dust.NewDust(proj.position, proj.width, proj.height, DustID.Torch, 0f, 0f, 100, default, 1.5f);
                            }
                        }
                    }
                }
            }
        }

        private void UpdateSoulSeekers(NPC npc, int currentPhase)
        {
            if (currentPhase >= 3)
            {
                shieldActive = false;
                return;
            }

            int orbiterType = ModContent.NPCType<CalamityMod.NPCs.CalClone.SoulSeeker>();

            if (shieldActive)
            {
                bool alive = false;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    NPC m = Main.npc[i];
                    if (m.active && m.type == orbiterType && m.ai[0] == npc.whoAmI)
                    {
                        alive = true;
                        // Shot amplification: check if nearby boss fireballs overlap
                        for (int p = 0; p < Main.maxProjectiles; p++)
                        {
                            Projectile proj = Main.projectile[p];
                            if (proj.active && proj.hostile && (proj.ModProjectile?.Name == "BrimstoneBarrage" || proj.ModProjectile?.Name == "BrimstoneHellblast" || proj.ModProjectile?.Name == "BrimstoneGigablast"))
                            {
                                if (Vector2.Distance(proj.Center, m.Center) < 40f)
                                {
                                    proj.Kill();
                                    // Amplified 3-way laser towards player
                                    if (Main.netMode != NetmodeID.MultiplayerClient)
                                    {
                                        Vector2 dir = SafeNormalize(Main.player[npc.target].Center - m.Center, Vector2.UnitY);
                                        for (int s = -1; s <= 1; s++)
                                        {
                                            SpawnHostile(npc, m.Center, dir.RotatedBy(s * 0.2f) * 12f, "Projectiles/Boss/BrimstoneLaser", npc.damage / 3);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (!alive)
                {
                    shieldActive = false;
                    shieldStunTimer = 360; // 6s stun
                    npc.velocity = Vector2.Zero;
                }
            }
            else
            {
                if (shieldStunTimer > 0)
                {
                    shieldStunTimer--;
                    npc.defense = 0;
                    if (shieldStunTimer == 0)
                    {
                        shieldRegenTimer = 720; // 12s regeneration window
                    }
                }
                else if (shieldRegenTimer > 0)
                {
                    shieldRegenTimer--;
                    if (shieldRegenTimer == 0)
                    {
                        shieldActive = true;
                        if (Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            for (int i = 0; i < 6; i++)
                            {
                                int minion = NPC.NewNPC(npc.GetSource_FromAI(), (int)npc.Center.X, (int)npc.Center.Y, orbiterType);
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
            if (currentPhase <= 2)
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
                        AttackState.Oblivion => AttackState.Animosity,
                        AttackState.Animosity => AttackState.LashesOfChaos,
                        AttackState.LashesOfChaos => AttackState.EntropysVigil,
                        _ => AttackState.Oblivion
                    };
                    npc.ai[1] = (float)next;
                    npc.ai[2] = 0;
                    npc.ai[3] = 0;
                }
            }
            else if (currentPhase == 3)
            {
                currentRepetition = 0;
                AttackState next = current switch
                {
                    AttackState.CrushsawCrasher => AttackState.HavocsBreath,
                    _ => AttackState.CrushsawCrasher
                };
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
            }
            npc.netUpdate = true;
        }
        #endregion

        #region Attack State Machine
        private void ExecuteOblivion(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -280f), timer < 40 ? 12f : 3f, 20f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                int idx = SpawnHostile(npc, target.Center + new Vector2(-200f, 0f), Vector2.Zero, "Projectiles/Boss/OblivionYoyo", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].timeLeft = 140;
                    Main.projectile[idx].ai[0] = 1f; // orbit sweep
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.Oblivion);
            }
        }

        private void ExecuteAnimosity(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(-280f, -200f), 10f, 15f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 36f;
                int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/AnimosityBullet", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // fog wall trigger
                    Main.projectile[idx].timeLeft = 120;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.Animosity);
            }
        }

        private void ExecuteLashes(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(280f, -240f), 11f, 16f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 3; i++)
                {
                    Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY).RotatedBy((i - 1) * 0.15f) * 8f;
                    int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/BrimstoneHellfireball", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 1f; // pull vortex trigger
                        Main.projectile[idx].timeLeft = 160;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.LashesOfChaos);
            }
        }

        private void ExecuteVigil(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(0f, -240f), 9f, 22f);

            if (timer == 40)
            {
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int c1 = NPC.NewNPC(npc.GetSource_FromAI(), (int)arenaCenter.X - 400, (int)arenaCenter.Y - 400, ModContent.Find<ModNPC>("CalamityMod/Catastromini").Type);
                    int c2 = NPC.NewNPC(npc.GetSource_FromAI(), (int)arenaCenter.X + 400, (int)arenaCenter.Y - 400, ModContent.Find<ModNPC>("CalamityMod/Cataclymini").Type);
                    if (c1 >= 0 && c1 < Main.maxNPCs)
                    {
                        Main.npc[c1].velocity = new Vector2(10f, 10f);
                        Main.npc[c1].ai[0] = npc.whoAmI;
                        Main.npc[c1].netUpdate = true;
                    }
                    if (c2 >= 0 && c2 < Main.maxNPCs)
                    {
                        Main.npc[c2].velocity = new Vector2(-10f, 10f);
                        Main.npc[c2].ai[0] = npc.whoAmI;
                        Main.npc[c2].netUpdate = true;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.EntropysVigil);
            }
        }

        private void ExecuteCrushsaw(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(300f, -220f), 10f, 18f);

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                int idx = SpawnHostile(npc, npc.Center, SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 14f, "Projectiles/Boss/CrushsawCrasher", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // flat border wall roll
                    Main.projectile[idx].timeLeft = 300;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.CrushsawCrasher);
            }
        }

        private void ExecuteHavoc(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            HoverToward(npc, target.Center + new Vector2(-300f, -250f), 11f, 15f);

            if (timer >= 50 && timer <= 170 && timer % 5 == 0)
            {
                int dmg = npc.damage / 3;
                float angle = MathHelper.Lerp(-0.6f, 0.6f, (timer - 50f) / 120f);
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY).RotatedBy(angle) * 12f;
                int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/BrimstoneFire", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // ignites borders
                    Main.projectile[idx].timeLeft = 120;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.HavocsBreath);
            }
        }

        private void ExecuteDesperation(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            // Teleport to center and hover statically
            npc.Center = Vector2.Lerp(npc.Center, arenaCenter, 0.1f);
            npc.velocity = Vector2.Zero;

            // Rotating 4-way cross laser
            int dmg = npc.damage / 3;
            float rotation = timer * 0.012f;
            if (timer >= 40 && timer % 12 == 0)
            {
                for (int s = 0; s < 4; s++)
                {
                    float a = rotation + s * MathHelper.PiOver2;
                    SpawnHostile(npc, npc.Center, a.ToRotationVector2() * 15f, "Projectiles/Boss/BrimstoneLaser", dmg);
                }
            }

            // Falling star explosions
            if (timer >= 40 && timer % 20 == 0)
            {
                Vector2 fallPos = arenaCenter + new Vector2(Main.rand.NextFloat(-300f, 300f), -300f);
                SpawnHostile(npc, fallPos, new Vector2(0f, 6f), "Projectiles/Boss/HellfireExplosion", dmg);
            }

            if (timer >= 600)
            {
                timer = 40; // loop desperation until dead
            }
        }

        private void ExecuteBrotherTransition(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            npc.velocity *= 0.9f;
            npc.dontTakeDamage = true;
            npc.alpha = (int)MathHelper.Lerp(0f, 255f, timer / 90f);

            if (timer == 45)
            {
                SoundEngine.PlaySound(SoundID.NPCDeath10, npc.Center);
                target.Calamity().GeneralScreenShakePower = 8f;
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int c1 = NPC.NewNPC(npc.GetSource_FromAI(), (int)arenaCenter.X - 250, (int)arenaCenter.Y, ModContent.Find<ModNPC>("CalamityMod/Cataclysm").Type);
                    int c2 = NPC.NewNPC(npc.GetSource_FromAI(), (int)arenaCenter.X + 250, (int)arenaCenter.Y, ModContent.Find<ModNPC>("CalamityMod/Catastrophe").Type);
                    if (c1 >= 0 && c1 < Main.maxNPCs)
                    {
                        Main.npc[c1].ai[0] = npc.whoAmI;
                        Main.npc[c1].netUpdate = true;
                    }
                    if (c2 >= 0 && c2 < Main.maxNPCs)
                    {
                        Main.npc[c2].ai[0] = npc.whoAmI;
                        Main.npc[c2].netUpdate = true;
                    }
                }
            }

            // Wait for both brothers to be defeated
            bool brothersAlive = false;
            int cataclysm = ModContent.Find<ModNPC>("CalamityMod/Cataclysm").Type;
            int catastrophe = ModContent.Find<ModNPC>("CalamityMod/Catastrophe").Type;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                if (Main.npc[i].active && (Main.npc[i].type == cataclysm || Main.npc[i].type == catastrophe))
                {
                    brothersAlive = true;
                    break;
                }
            }

            if (!brothersAlive && timer >= 90)
            {
                npc.alpha = 0;
                npc.dontTakeDamage = false;
                AttackState next = AttackState.CrushsawCrasher;
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
                Color trailColor = new Color(220, 60, 60, 0) * alpha;
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

            Color glowColor = new Color(220, 60, 60, 0) * 0.35f;
            spriteBatch.Draw(tex, npc.Center - screenPos, frame, glowColor, npc.rotation, origin, npc.scale * 1.08f, SpriteEffects.None, 0f);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
        }
        #endregion
    }
}
