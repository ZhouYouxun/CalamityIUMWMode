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

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.AstrumDeus
{
    internal sealed class AstrumDeusAI : IUMWBossAI
    {
        #region Constants & Configurations
        public override int NPCType => ModContent.Find<ModNPC>("CalamityMod/AstrumDeusHead").Type;
        public override string BossName => "Astrum Deus";
        public override Color DebugColor => new(160, 130, 255);

        public override int MaxPhaseCount => 2;
        public override float[] PhaseLifeRatios => new[] { 0.50f };
        public override int AttackCycleLength => 120;
        public override float MotionIntensity => 1.2f;
        #endregion

        #region Attack States
        public enum AttackState
        {
            TheMicrowave = 0,
            StarSputter = 1,
            StarShower = 2,
            StarspawnHelix = 3,
            RegulusRiot = 4,
            AstralPike = 5,
            AstralStaff = 6,
            TrueBiomeBlade = 7,
            SplitTransition = 8
        }
        #endregion

        #region Fields
        private int ticksRunning = 0;
        private int currentRepetition = 0;
        private readonly Vector2[] oldPositions = new Vector2[14];
        private int oldPositionsIndex;

        // Head stun / Segment break tracking
        private int headStunTimer = 0;
        private int brokenNodesTracker = 0;

        // Constellation laser sweep timer
        private int constellationTimer = 0;
        #endregion

        #region Core AI Hooks
        public override bool PreAI(NPC npc, IUMWGlobalNPC data)
        {
            ticksRunning++;

            int headType = ModContent.Find<ModNPC>("CalamityMod/AstrumDeusHead").Type;
            int bodyType = ModContent.Find<ModNPC>("CalamityMod/AstrumDeusBody").Type;
            int tailType = ModContent.Find<ModNPC>("CalamityMod/AstrumDeusTail").Type;

            // Return true for body/tail segments to let vanilla link code run
            if (npc.type != headType)
            {
                // Initialize node HP on first frame
                if (npc.localAI[0] == 0f && npc.localAI[1] == 0f)
                {
                    npc.localAI[0] = 250f; // node HP
                }
                return true;
            }

            oldPositions[oldPositionsIndex] = npc.Center;
            oldPositionsIndex = (oldPositionsIndex + 1) % oldPositions.Length;

            if (!TryGetTarget(npc, out Player target))
            {
                npc.velocity.Y += 0.5f;
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
                state = AttackState.TheMicrowave;
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
                state = AttackState.SplitTransition;
                npc.ai[1] = (float)state;
                timer = 0;
                stateTracker = 0;
                npc.netUpdate = true;
            }

            // Constellation Link Overload (every 6 seconds / 360 frames)
            UpdateConstellationLink(npc, target, tailType);

            // Broken nodes check to trigger head stun
            UpdateNodeStun(npc, bodyType);

            // Worm Head walking movement profile
            float speed = 12f + (1f - lifeRatio) * 6f;
            float turnSpeed = 0.04f + (1f - lifeRatio) * 0.03f;
            if (headStunTimer > 0)
            {
                npc.velocity *= 0.9f;
            }
            else
            {
                Vector2 desiredVel = SafeNormalize(target.Center - npc.Center, Vector2.Zero) * speed;
                npc.velocity = Vector2.Lerp(npc.velocity, desiredVel, turnSpeed);
            }
            npc.rotation = npc.velocity.ToRotation() + MathHelper.PiOver2;

            // Execute state machine
            if (headStunTimer == 0)
            {
                switch (state)
                {
                    case AttackState.TheMicrowave:
                        ExecuteMicrowave(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.StarSputter:
                        ExecuteStarSputter(npc, target, ref timer, ref stateTracker, currentPhase, bodyType);
                        break;
                    case AttackState.StarShower:
                        ExecuteStarShower(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.StarspawnHelix:
                        ExecuteStarspawnHelix(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.RegulusRiot:
                        ExecuteRegulusRiot(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.AstralPike:
                        ExecuteAstralPike(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.AstralStaff:
                        ExecuteAstralStaff(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.TrueBiomeBlade:
                        ExecuteTrueBiomeBlade(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                    case AttackState.SplitTransition:
                        ExecuteTransition(npc, target, ref timer, ref stateTracker, currentPhase);
                        break;
                }
            }

            return false;
        }

        public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers)
        {
            int headType = ModContent.Find<ModNPC>("CalamityMod/AstrumDeusHead").Type;
            int bodyType = ModContent.Find<ModNPC>("CalamityMod/AstrumDeusBody").Type;

            if (npc.type == bodyType)
            {
                if (npc.localAI[1] == 1f)
                {
                    modifiers.FinalDamage *= 2.0f; // node broken = 200% damage
                }
                else
                {
                    modifiers.FinalDamage *= 0.05f; // node alive = 95% DR
                    // Deal damage to local node HP (flat reduction per hit to act as hit counter)
                    npc.localAI[0] -= 25f;
                    if (npc.localAI[0] <= 0f)
                    {
                        npc.localAI[1] = 1f; // mark node broken
                        SoundEngine.PlaySound(SoundID.NPCHit53, npc.Center);
                        // Spawn gold star dust
                        for (int k = 0; k < 12; k++)
                        {
                            Dust.NewDust(npc.position, npc.width, npc.height, DustID.GoldFlame, 0f, 0f, 100, default, 1.5f);
                        }
                    }
                }
            }

            if (npc.type == headType && headStunTimer > 0)
            {
                modifiers.FinalDamage *= 1.25f; // takes 125% damage during stun
            }
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            int headType = ModContent.Find<ModNPC>("CalamityMod/AstrumDeusHead").Type;
            int bodyType = ModContent.Find<ModNPC>("CalamityMod/AstrumDeusBody").Type;

            if (npc.type == bodyType)
            {
                if (npc.localAI[1] == 1f)
                {
                    modifiers.FinalDamage *= 2.0f; // node broken = 200% damage
                }
                else
                {
                    modifiers.FinalDamage *= 0.05f; // 95% DR
                    npc.localAI[0] -= 25f;
                    if (npc.localAI[0] <= 0f)
                    {
                        npc.localAI[1] = 1f;
                        SoundEngine.PlaySound(SoundID.NPCHit53, npc.Center);
                        for (int k = 0; k < 12; k++)
                        {
                            Dust.NewDust(npc.position, npc.width, npc.height, DustID.GoldFlame, 0f, 0f, 100, default, 1.5f);
                        }
                    }
                }
            }

            if (npc.type == headType && headStunTimer > 0)
            {
                modifiers.FinalDamage *= 1.25f;
            }
        }
        #endregion

        #region Constellation & Stun Helpers
        private void UpdateConstellationLink(NPC npc, Player target, int tailType)
        {
            constellationTimer++;
            if (constellationTimer >= 360)
            {
                constellationTimer = 0;
            }

            // Find tail
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
                // warning link during frames 300-360
                if (constellationTimer >= 300 && constellationTimer < 340)
                {
                    // draw purple visual line alert (handled client-side or implicitly)
                }
                // laser sweep active during frames 340-360
                else if (constellationTimer >= 340)
                {
                    // check player collision with segment
                    float dist = Vector2.Distance(target.Center, npc.Center);
                    // simple projection of player onto line
                    Vector2 ab = tail.Center - npc.Center;
                    Vector2 ac = target.Center - npc.Center;
                    float abLen = ab.Length();
                    if (abLen > 0f)
                    {
                        float proj = Vector2.Dot(ac, ab) / abLen;
                        proj = Math.Clamp(proj, 0f, abLen);
                        Vector2 closestPoint = npc.Center + SafeNormalize(ab, Vector2.Zero) * proj;
                        float dLine = Vector2.Distance(target.Center, closestPoint);

                        if (dLine < 25f)
                        {
                            target.AddBuff(BuffID.Cursed, 60);
                            target.Hurt(Terraria.DataStructures.PlayerDeathReason.ByNPC(npc.whoAmI), 22, 0);
                        }
                    }
                }
            }
        }

        private void UpdateNodeStun(NPC npc, int bodyType)
        {
            if (headStunTimer > 0)
            {
                headStunTimer--;
                npc.defense = 0;
                return;
            }

            // count broken nodes
            int brokenCount = 0;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                if (Main.npc[i].active && Main.npc[i].type == bodyType && Main.npc[i].localAI[1] == 1f)
                {
                    brokenCount++;
                }
            }

            if (brokenCount >= 10)
            {
                // Reset all nodes
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i].active && Main.npc[i].type == bodyType)
                    {
                        Main.npc[i].localAI[0] = 250f;
                        Main.npc[i].localAI[1] = 0f;
                    }
                }
                headStunTimer = 360; // 6s stun
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
                        AttackState.TheMicrowave => AttackState.StarSputter,
                        AttackState.StarSputter => AttackState.StarShower,
                        AttackState.StarShower => AttackState.StarspawnHelix,
                        AttackState.StarspawnHelix => AttackState.RegulusRiot,
                        _ => AttackState.TheMicrowave
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
                    AttackState.AstralPike => AttackState.AstralStaff,
                    AttackState.AstralStaff => AttackState.TrueBiomeBlade,
                    _ => AttackState.AstralPike
                };
                npc.ai[1] = (float)next;
                npc.ai[2] = 0;
                npc.ai[3] = 0;
            }
            npc.netUpdate = true;
        }
        #endregion

        #region Attack State Machine
        private void ExecuteMicrowave(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer >= 50 && timer <= 170 && timer % 4 == 0)
            {
                int dmg = npc.damage / 3;
                float angle = MathHelper.Lerp(-0.25f, 0.25f, (float)Math.Sin(timer * 0.1f));
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY).RotatedBy(angle) * 12f;
                // sweeping microwave orange beam with knockback
                SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/AstrumDeusMicrowave", dmg, 12f);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.TheMicrowave);
            }
        }

        private void ExecuteStarSputter(NPC npc, Player target, ref float timer, ref float tracker, int phase, int bodyType)
        {
            timer++;

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                // Eject stars from body segments
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i].active && Main.npc[i].type == bodyType && i % 4 == 0)
                    {
                        Vector2 vel = SafeNormalize(Main.npc[i].velocity.RotatedBy(MathHelper.PiOver2), Vector2.UnitY) * 6f;
                        int idx1 = SpawnHostile(npc, Main.npc[i].Center, vel, "Projectiles/Boss/AstrumDeusStar", dmg);
                        int idx2 = SpawnHostile(npc, Main.npc[i].Center, -vel, "Projectiles/Boss/AstrumDeusStar", dmg);
                        if (idx1 >= 0 && idx1 < Main.maxProjectiles) Main.projectile[idx1].ai[0] = 1f; // magnet back trigger
                        if (idx2 >= 0 && idx2 < Main.maxProjectiles) Main.projectile[idx2].ai[0] = 1f;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.StarSputter);
            }
        }

        private void ExecuteStarShower(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // staggered odd/even columns
                for (int i = 0; i < 8; i++)
                {
                    Vector2 spawn = target.Center + new Vector2(i * 140f - 490f, -400f);
                    int idx = SpawnHostile(npc, spawn, new Vector2(0f, 10f), "Projectiles/Boss/AstrumDeusStar", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[1] = i % 2 * 30 + 10; // staggered drop delay
                        Main.projectile[idx].timeLeft = 240;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.StarShower);
            }
        }

        private void ExecuteStarspawnHelix(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 40)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 2; i++)
                {
                    float offset = i * MathHelper.Pi;
                    int idx = SpawnHostile(npc, target.Center + new Vector2(-400f, 0f), new Vector2(10f, 0f), "Projectiles/Boss/AstrumDeusHelix", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = offset; // helix phase offset
                        Main.projectile[idx].timeLeft = 160;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.StarspawnHelix);
            }
        }

        private void ExecuteRegulusRiot(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                for (int i = 0; i < 5; i++)
                {
                    float angle = i * MathHelper.TwoPi / 5f;
                    int idx = SpawnHostile(npc, target.Center + angle.ToRotationVector2() * 240f, Vector2.Zero, "Projectiles/Boss/AstrumDeusStar", dmg);
                    if (idx >= 0 && idx < Main.maxProjectiles)
                    {
                        Main.projectile[idx].ai[0] = 2f; // converge-on-player trigger
                        Main.projectile[idx].timeLeft = 200;
                    }
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.RegulusRiot);
            }
        }

        private void ExecuteAstralPike(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                Vector2 vel = SafeNormalize(target.Center - npc.Center, Vector2.UnitY) * 20f;
                int idx = SpawnHostile(npc, npc.Center, vel, "Projectiles/Boss/AstralPikeStar", dmg);
                if (idx >= 0 && idx < Main.maxProjectiles)
                {
                    Main.projectile[idx].ai[0] = 1f; // pike lunge + 8-way split
                    Main.projectile[idx].timeLeft = 120;
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.AstralPike);
            }
        }

        private void ExecuteAstralStaff(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 50)
            {
                int dmg = npc.damage / 3;
                // falling meteor circle法阵
                for (int i = 0; i < 6; i++)
                {
                    float a = i * MathHelper.TwoPi / 6f;
                    Vector2 pos = target.Center + a.ToRotationVector2() * 300f;
                    SpawnHostile(npc, pos, new Vector2(0f, 12f), "Projectiles/Boss/AstrumDeusMeteor", dmg);
                }
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.AstralStaff);
            }
        }

        private void ExecuteTrueBiomeBlade(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;

            if (timer == 60)
            {
                int dmg = npc.damage / 3;
                // Diagonal cross slash grid lines
                SpawnHostile(npc, target.Center + new Vector2(-400f, -400f), new Vector2(10f, 10f), "Projectiles/Boss/DeusBladeGrid", dmg);
                SpawnHostile(npc, target.Center + new Vector2(400f, -400f), new Vector2(-10f, 10f), "Projectiles/Boss/DeusBladeGrid", dmg);
            }

            if (timer >= 220)
            {
                RotateAttack(npc, phase, AttackState.TrueBiomeBlade);
            }
        }

        private void ExecuteTransition(NPC npc, Player target, ref float timer, ref float tracker, int phase)
        {
            timer++;
            headStunTimer = 0; // reset stun during transitions
            npc.velocity *= 0.9f;

            if (timer == 45)
            {
                SoundEngine.PlaySound(SoundID.NPCDeath10, npc.Center);
                target.Calamity().GeneralScreenShakePower = 8f;
                // split logic is handled by Calamity Mod itself, we just transition to P2 states
            }

            if (timer >= 90)
            {
                AttackState next = AttackState.AstralPike;
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
            int headType = ModContent.Find<ModNPC>("CalamityMod/AstrumDeusHead").Type;
            if (npc.type != headType)
                return true; // vanilla draws body/tail segments

            Texture2D tex = TextureAssets.Npc[npc.type].Value;
            Rectangle frame = npc.frame;
            Vector2 origin = frame.Size() / 2f;

            for (int i = 0; i < oldPositions.Length; i++)
            {
                int idx = (oldPositionsIndex - i - 1 + oldPositions.Length) % oldPositions.Length;
                if (oldPositions[idx] == Vector2.Zero) continue;
                float alpha = (1f - i / (float)oldPositions.Length) * 0.55f;
                Color trailColor = new Color(160, 130, 255, 0) * alpha;
                spriteBatch.Draw(tex, oldPositions[idx] - screenPos, frame, trailColor, npc.rotation, origin, npc.scale, SpriteEffects.None, 0f);
            }

            return true;
        }

        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            int headType = ModContent.Find<ModNPC>("CalamityMod/AstrumDeusHead").Type;
            if (npc.type != headType)
                return;

            Texture2D tex = TextureAssets.Npc[npc.type].Value;
            Rectangle frame = npc.frame;
            Vector2 origin = frame.Size() / 2f;

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);

            Color glowColor = new Color(160, 130, 255, 0) * 0.35f;
            spriteBatch.Draw(tex, npc.Center - screenPos, frame, glowColor, npc.rotation, origin, npc.scale * 1.08f, SpriteEffects.None, 0f);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
        }
        #endregion
    }
}
