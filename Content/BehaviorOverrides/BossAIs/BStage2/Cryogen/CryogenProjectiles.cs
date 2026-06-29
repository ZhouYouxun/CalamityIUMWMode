using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.BStage2.Cryogen
{
    // =====================================================================================================================
    // CRYO STONE BARRIER - HORIZONTAL ARENA FLOOR
    // =====================================================================================================================
    public class CryoStoneBarrier : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/InvisibleProj";

        public override void SetDefaults()
        {
            Projectile.width = 1600;
            Projectile.height = 16;
            Projectile.hostile = false;
            Projectile.friendly = false;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 99999;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override bool? CanDamage() => false;

        public override void AI()
        {
            // Keep the barrier active only if Cryogen is active
            NPC parent = Main.npc[(int)Projectile.ai[0]];
            if (!parent.active || parent.type != ModContent.NPCType<CalamityMod.NPCs.Cryogen.Cryogen>())
            {
                Projectile.Kill();
                return;
            }

            // Snap parent phase. If phase > 3 (Phase 4+), barrier disappears
            if (parent.ai[0] > 3f)
            {
                Projectile.Kill();
                return;
            }

            // Apply solid collision logic for the local player
            Player player = Main.LocalPlayer;
            if (player.active && !player.dead)
            {
                float leftBound = Projectile.Center.X - 800f;
                float rightBound = Projectile.Center.X + 800f;
                float floorY = Projectile.Center.Y;

                // Check if player is horizontally within the barrier
                if (player.Center.X >= leftBound && player.Center.X <= rightBound)
                {
                    // Check if player is landing on the floor
                    float playerFeetY = player.position.Y + player.height;
                    float prevPlayerFeetY = player.oldPosition.Y + player.height;

                    if (playerFeetY >= floorY - 2f && prevPlayerFeetY <= floorY + 8f)
                    {
                        if (player.velocity.Y >= 0f)
                        {
                            player.position.Y = floorY - player.height;
                            player.velocity.Y = 0f;
                            player.fallStart = (int)(player.position.Y / 16f);
                            player.wingTime = player.wingTimeMax;
                        }
                    }
                }
            }

            // Periodically release subtle particles along the barrier
            if (Main.rand.NextBool(5))
            {
                float rx = Main.rand.NextFloat(-800f, 800f);
                Dust d = Dust.NewDustPerfect(
                    new Vector2(Projectile.Center.X + rx, Projectile.Center.Y + Main.rand.NextFloat(-4f, 4f)),
                    DustID.Ice,
                    new Vector2(Main.rand.NextFloat(-0.5f, 0.5f), Main.rand.NextFloat(-1f, 0f)),
                    100,
                    Color.DeepSkyBlue,
                    Main.rand.NextFloat(0.6f, 1.2f)
                );
                d.noGravity = true;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            SpriteBatch spriteBatch = Main.spriteBatch;
            Vector2 start = new Vector2(Projectile.Center.X - 800f, Projectile.Center.Y);
            Vector2 end = new Vector2(Projectile.Center.X + 800f, Projectile.Center.Y);

            // Draw a thick additively blended blue glowing line
            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);

            float pulse = 0.8f + 0.2f * (float)Math.Sin(Main.GameUpdateCount * 0.15f);
            Color drawColor = Color.DeepSkyBlue * pulse * 0.8f;
            Color coreColor = Color.White * 0.9f;

            // Draw outer glow lines
            DrawLineHelper(spriteBatch, start, end, drawColor, 8f);
            // Draw core bright line
            DrawLineHelper(spriteBatch, start, end, coreColor, 2f);

            // Draw end cap runs/nodes
            Texture2D capTex = TextureAssets.Npc[ModContent.NPCType<CalamityMod.NPCs.Cryogen.Cryogen>()].Value;
            if (capTex != null)
            {
                Vector2 origin = capTex.Size() * 0.5f;
                float capScale = 0.25f;
                spriteBatch.Draw(capTex, start - Main.screenPosition, null, drawColor, Main.GameUpdateCount * 0.02f, origin, capScale, SpriteEffects.None, 0f);
                spriteBatch.Draw(capTex, end - Main.screenPosition, null, drawColor, -Main.GameUpdateCount * 0.02f, origin, capScale, SpriteEffects.None, 0f);
            }

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);

            return false;
        }

        private static void DrawLineHelper(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, float width)
        {
            Vector2 delta = end - start;
            float length = delta.Length();
            float rotation = delta.ToRotation();
            spriteBatch.Draw(
                TextureAssets.MagicPixel.Value,
                start - Main.screenPosition,
                null,
                color,
                rotation,
                new Vector2(0f, 0.5f),
                new Vector2(length, width),
                SpriteEffects.None,
                0f
            );
        }
    }

    // =====================================================================================================================
    // WHITE FROST BOW ARROW (P1 ATTACK 1)
    // =====================================================================================================================
    public class CryogenMistArrow : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/Ranged/MistArrow";

        public override void SetStaticDefaults()
        {
            Main.projFrames[Projectile.type] = 3;
        }

        public override void SetDefaults()
        {
            Projectile.width = 14;
            Projectile.height = 36;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = 1;
            Projectile.timeLeft = 100;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            // Simple frame animation
            Projectile.frameCounter++;
            if (Projectile.frameCounter >= 5)
            {
                Projectile.frame = (Projectile.frame + 1) % 3;
                Projectile.frameCounter = 0;
            }

            // Curve slightly
            Projectile.velocity = Projectile.velocity.RotatedBy(MathF.Sin(Projectile.timeLeft * 0.04f) * 0.003f);
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;

            // Emit frost particles
            if (Main.rand.NextBool(3))
            {
                Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.Ice, -Projectile.velocity * 0.2f, 100, Color.Cyan, 0.8f);
                d.noGravity = true;
            }
        }

        public override void OnKill(int timeLeft)
        {
            SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.5f, Pitch = 0.1f }, Projectile.Center);
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            // Spawn 3 lingering frost mists
            for (int i = 0; i < 3; i++)
            {
                Vector2 vel = new Vector2(Main.rand.NextFloat(-1f, 1f), Main.rand.NextFloat(-2f, 0f));
                Projectile.NewProjectile(
                    Projectile.GetSource_FromThis(),
                    Projectile.Center,
                    vel,
                    ModContent.ProjectileType<CryogenFrostMist>(),
                    Projectile.damage,
                    0f,
                    Main.myPlayer
                );
            }
        }
    }

    // =====================================================================================================================
    // LINGERING FROST MIST (P1 ATTACK 1)
    // =====================================================================================================================
    public class CryogenFrostMist : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/Ranged/MistArrowFrostMist";

        public override void SetDefaults()
        {
            Projectile.width = 30;
            Projectile.height = 30;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 160;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.velocity *= 0.96f;
            Projectile.rotation += 0.012f;

            // Fade in and out
            float progress = (160f - Projectile.timeLeft) / 160f;
            Projectile.alpha = (int)(255 * (1f - MathF.Sin(progress * MathHelper.Pi)));
            Projectile.scale = 0.6f + progress * 0.8f;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // Dynamically load the frost mist texture
            Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Projectiles/Ranged/MistArrowFrostMist", AssetRequestMode.ImmediateLoad).Value;
            if (tex == null)
                return true;

            SpriteEffects effects = Projectile.whoAmI % 2 == 0 ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
            Color drawColor = Color.Lerp(Color.DeepSkyBlue, Color.Cyan, 0.5f) * ((255 - Projectile.alpha) / 255f) * 0.6f;
            
            Main.spriteBatch.Draw(
                tex,
                Projectile.Center - Main.screenPosition,
                null,
                drawColor,
                Projectile.rotation,
                tex.Size() * 0.5f,
                Projectile.scale,
                effects,
                0f
            );
            return false;
        }
    }

    // =====================================================================================================================
    // ICEBREAKER BOOMERANG HAMMER (P1 ATTACK 2)
    // =====================================================================================================================
    public class CryogenIcebreaker : ModProjectile
    {
        public override string Texture => "CalamityMod/Items/Weapons/Rogue/Icebreaker";

        public override void SetDefaults()
        {
            Projectile.width = 38;
            Projectile.height = 38;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 300;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.rotation += 0.18f;
            Projectile.localAI[0]++; // Local timer

            // Check if Y coordinate crosses the barrier
            // Find active CryoStoneBarrier
            float barrierY = -9999f;
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.type == ModContent.ProjectileType<CryoStoneBarrier>())
                {
                    barrierY = p.Center.Y;
                    break;
                }
            }

            if (barrierY != -9999f && Projectile.Center.Y >= barrierY && Projectile.velocity.Y > 0f)
            {
                // Hit the floor, explode!
                Projectile.Kill();
                return;
            }

            // Normal flying boomerang AI
            if (Projectile.localAI[0] < 80f)
            {
                // Straight path
            }
            else if (Projectile.localAI[0] < 110f)
            {
                // Slow down
                Projectile.velocity *= 0.92f;
            }
            else if (Projectile.localAI[0] < 128f)
            {
                // Pause and float in place
                Projectile.velocity = Vector2.Zero;
            }
            else if (Projectile.localAI[0] == 128f)
            {
                // Play warning flash cue
                SoundEngine.PlaySound(SoundID.Item30 with { Volume = 0.5f, Pitch = -0.2f }, Projectile.Center);
            }
            else
            {
                // Accelerate back towards the player
                Player player = Main.player[Projectile.owner];
                if (player.active && !player.dead)
                {
                    Vector2 destDir = (player.Center - Projectile.Center).SafeNormalize(Vector2.UnitY);
                    float curSpeed = Projectile.velocity.Length();
                    Projectile.velocity = Vector2.Lerp(Projectile.velocity, destDir * Math.Min(curSpeed + 1.2f, 18f), 0.15f);
                }
            }
        }

        public override void OnKill(int timeLeft)
        {
            SoundEngine.PlaySound(SoundID.Item27, Projectile.Center);
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            // Explode into shards flying upwards/diagonally
            for (int i = 0; i < 5; i++)
            {
                Vector2 vel = new Vector2(Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-8f, -4f));
                Projectile.NewProjectile(
                    Projectile.GetSource_FromThis(),
                    Projectile.Center,
                    vel,
                    ModContent.ProjectileType<CryogenJavelinShard>(),
                    Projectile.damage,
                    0f,
                    Main.myPlayer
                );
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // Draw with ice-blue glow
            Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Items/Weapons/Rogue/Icebreaker", AssetRequestMode.ImmediateLoad).Value;
            if (tex == null)
                return true;

            Color glowColor = Color.DeepSkyBlue * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 offset = (MathHelper.TwoPi * i / 4f).ToRotationVector2() * 2f;
                Main.spriteBatch.Draw(
                    tex,
                    Projectile.Center + offset - Main.screenPosition,
                    null,
                    glowColor,
                    Projectile.rotation,
                    tex.Size() * 0.5f,
                    Projectile.scale * 1.5f,
                    SpriteEffects.None,
                    0f
                );
            }

            Main.spriteBatch.Draw(
                tex,
                Projectile.Center - Main.screenPosition,
                null,
                Color.White,
                Projectile.rotation,
                tex.Size() * 0.5f,
                Projectile.scale * 1.5f,
                SpriteEffects.None,
                0f
            );
            return false;
        }
    }

    // =====================================================================================================================
    // ICE BOMB (P1 ATTACK 3 / AVALANCHE SHARDS)
    // =====================================================================================================================
    public class CryogenIceBomb : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/Boss/IceBomb";

        public override void SetDefaults()
        {
            Projectile.width = 24;
            Projectile.height = 24;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 120;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.velocity = Vector2.Zero;
            Projectile.rotation += 0.04f;

            // Grow scale and fade in
            float progress = (120f - Projectile.timeLeft) / 120f;
            Projectile.scale = 0.5f + progress * 0.7f;
            Projectile.alpha = (int)(255 * (1f - progress));
        }

        public override void OnKill(int timeLeft)
        {
            SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.7f, Pitch = -0.1f }, Projectile.Center);
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            // Explode into 8 ice shards in a circular ring
            for (int i = 0; i < 8; i++)
            {
                Vector2 vel = (MathHelper.TwoPi * i / 8f).ToRotationVector2() * 8f;
                Projectile.NewProjectile(
                    Projectile.GetSource_FromThis(),
                    Projectile.Center,
                    vel,
                    ModContent.ProjectileType<CryogenIceShard>(),
                    Projectile.damage,
                    0f,
                    Main.myPlayer
                );
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Projectiles/Boss/IceBomb", AssetRequestMode.ImmediateLoad).Value;
            if (tex == null)
                return true;

            Color c = Color.Lerp(Color.Cyan, Color.White, 0.4f) * ((255 - Projectile.alpha) / 255f);
            Main.spriteBatch.Draw(
                tex,
                Projectile.Center - Main.screenPosition,
                null,
                c,
                Projectile.rotation,
                tex.Size() * 0.5f,
                Projectile.scale,
                SpriteEffects.None,
                0f
            );
            return false;
        }
    }

    // =====================================================================================================================
    // FALLING SHARDS & STARS
    // =====================================================================================================================
    public class CryogenIceShard : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/Rogue/CrystalPiercerShard";

        public override void SetDefaults()
        {
            Projectile.width = 10;
            Projectile.height = 10;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = 1;
            Projectile.timeLeft = 90;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.velocity.Y += 0.15f; // Gravity
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Projectiles/Rogue/CrystalPiercerShard", AssetRequestMode.ImmediateLoad).Value;
            if (tex == null)
                return true;

            Main.spriteBatch.Draw(
                tex,
                Projectile.Center - Main.screenPosition,
                null,
                Color.White * 0.9f,
                Projectile.rotation,
                tex.Size() * 0.5f,
                Projectile.scale,
                SpriteEffects.None,
                0f
            );
            return false;
        }
    }

    public class CryogenIceStar : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/Typeless/KelvinCatalystStar";

        public override void SetDefaults()
        {
            Projectile.width = 12;
            Projectile.height = 12;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = 1;
            Projectile.timeLeft = 140;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.velocity.Y += 0.1f;
            Projectile.rotation += 0.05f;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Projectiles/Typeless/KelvinCatalystStar", AssetRequestMode.ImmediateLoad).Value;
            if (tex == null)
                return true;

            Main.spriteBatch.Draw(
                tex,
                Projectile.Center - Main.screenPosition,
                null,
                Color.DeepSkyBlue * 0.95f,
                Projectile.rotation,
                tex.Size() * 0.5f,
                Projectile.scale,
                SpriteEffects.None,
                0f
            );
            return false;
        }
    }

    // =====================================================================================================================
    // SNOWSTORM SNOWFLAKE (P1 ATTACK 4)
    // =====================================================================================================================
    public class CryogenSnowflake : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/Magic/Snowflake";

        public override void SetDefaults()
        {
            Projectile.width = 44;
            Projectile.height = 44;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 350;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.rotation += 0.04f;
            Projectile.localAI[0]++;

            // Orbit math
            NPC parent = Main.npc[(int)Projectile.ai[0]];
            if (!parent.active || parent.type != ModContent.NPCType<CalamityMod.NPCs.Cryogen.Cryogen>())
            {
                Projectile.Kill();
                return;
            }

            Player player = Main.player[parent.target];
            if (player.active && !player.dead)
            {
                // Orbit the player
                float speed = 0.018f;
                float angle = Projectile.ai[1] + Projectile.localAI[0] * speed;
                Vector2 desiredPos = player.Center + angle.ToRotationVector2() * 280f;
                
                // Weak snap to target orbit point
                Projectile.Center = Vector2.Lerp(Projectile.Center, desiredPos, 0.08f);

                // Shoot Kelvin stars in flight direction every 45 frames
                if (Projectile.localAI[0] % 45f == 0f && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    // Direction is perpendicular to radial vector (tangential vector)
                    Vector2 radial = Projectile.Center - player.Center;
                    Vector2 tang = new Vector2(-radial.Y, radial.X).SafeNormalize(Vector2.UnitY);
                    
                    float spread = MathHelper.ToRadians(30f);
                    for (int i = -1; i <= 1; i++)
                    {
                        Vector2 vel = tang.RotatedBy(i * spread) * 5f;
                        Projectile.NewProjectile(
                            Projectile.GetSource_FromThis(),
                            Projectile.Center,
                            vel,
                            ModContent.ProjectileType<CryogenIceStar>(),
                            Projectile.damage,
                            0f,
                            Main.myPlayer
                        );
                    }
                    SoundEngine.PlaySound(SoundID.Item28 with { Volume = 0.4f, Pitch = 0.1f }, Projectile.Center);
                }
            }
        }

        public override void OnKill(int timeLeft)
        {
            SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.8f }, Projectile.Center);
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            // Explode into 6 stars in a circle
            for (int i = 0; i < 6; i++)
            {
                Vector2 vel = (MathHelper.TwoPi * i / 6f).ToRotationVector2() * 6f;
                Projectile.NewProjectile(
                    Projectile.GetSource_FromThis(),
                    Projectile.Center,
                    vel,
                    ModContent.ProjectileType<CryogenIceStar>(),
                    Projectile.damage,
                    0f,
                    Main.myPlayer
                );
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Projectiles/Magic/Snowflake", AssetRequestMode.ImmediateLoad).Value;
            if (tex == null)
                return true;

            Main.spriteBatch.Draw(
                tex,
                Projectile.Center - Main.screenPosition,
                null,
                Color.White * 0.95f,
                Projectile.rotation,
                tex.Size() * 0.5f,
                2.5f,
                SpriteEffects.None,
                0f
            );
            return false;
        }
    }

    // =====================================================================================================================
    // SOUL OF CRYOGEN SHARD (P1 ATTACK 5)
    // =====================================================================================================================
    public class CryogenSoulShard : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/Rogue/FrostShardFriendly";

        public override void SetDefaults()
        {
            Projectile.width = 14;
            Projectile.height = 24;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = 1;
            Projectile.timeLeft = 130;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.velocity.Y += 0.2f; // Downwards gravity acceleration
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;

            if (Main.rand.NextBool(4))
            {
                Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.Ice, -Projectile.velocity * 0.1f, 100, Color.Cyan, 0.7f);
                d.noGravity = true;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Projectiles/Rogue/FrostShardFriendly", AssetRequestMode.ImmediateLoad).Value;
            if (tex == null)
                return true;

            Main.spriteBatch.Draw(
                tex,
                Projectile.Center - Main.screenPosition,
                null,
                Color.DeepSkyBlue * 0.9f,
                Projectile.rotation,
                tex.Size() * 0.5f,
                Projectile.scale * 1.2f,
                SpriteEffects.None,
                0f
            );
            return false;
        }
    }

    // =====================================================================================================================
    // GLACIAL EMBRACE SPIKE (P1 ATTACK 6)
    // =====================================================================================================================
    public class CryogenGlbraceSpike : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/Summon/GlacialEmbracePointyThing";

        public override void SetDefaults()
        {
            Projectile.width = 14;
            Projectile.height = 36;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 360;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.localAI[0]++;

            NPC parent = Main.npc[(int)Projectile.ai[0]];
            if (!parent.active || parent.type != ModContent.NPCType<CalamityMod.NPCs.Cryogen.Cryogen>())
            {
                Projectile.Kill();
                return;
            }

            if (Projectile.localAI[0] < 240f)
            {
                // Orbit and contract
                float progress = Math.Min(Projectile.localAI[0] / 220f, 1f);
                float radius = MathHelper.Lerp(200f, 100f, progress);
                float orbitAngle = Projectile.ai[1] + Projectile.localAI[0] * 0.04f;

                Projectile.Center = parent.Center + orbitAngle.ToRotationVector2() * radius;
                Projectile.rotation = Projectile.ai[1] + Projectile.localAI[0] * 0.06f;
                Projectile.velocity = Vector2.Zero;
            }
            else if (Projectile.localAI[0] == 240f)
            {
                // Shoot outward in radial direction
                float finalAngle = Projectile.ai[1] + Projectile.localAI[0] * 0.04f;
                Projectile.velocity = finalAngle.ToRotationVector2() * 18f;
                Projectile.rotation = finalAngle + MathHelper.PiOver2;
                SoundEngine.PlaySound(SoundID.Item30 with { Volume = 0.5f }, Projectile.Center);
            }
            else
            {
                // Constant velocity, fades after traveling 560px
                float distTraveled = (Projectile.localAI[0] - 240f) * 18f;
                if (distTraveled >= 560f)
                {
                    Projectile.Kill();
                }
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Projectiles/Summon/GlacialEmbracePointyThing", AssetRequestMode.ImmediateLoad).Value;
            if (tex == null)
                return true;

            // Draw with bright ice outline trail
            Color glowColor = Color.Cyan * 0.45f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 offset = (MathHelper.TwoPi * i / 4f).ToRotationVector2() * 2f;
                Main.spriteBatch.Draw(
                    tex,
                    Projectile.Center + offset - Main.screenPosition,
                    null,
                    glowColor,
                    Projectile.rotation,
                    tex.Size() * 0.5f,
                    1.3f,
                    SpriteEffects.None,
                    0f
                );
            }

            Main.spriteBatch.Draw(
                tex,
                Projectile.Center - Main.screenPosition,
                null,
                Color.White,
                Projectile.rotation,
                tex.Size() * 0.5f,
                1.3f,
                SpriteEffects.None,
                0f
            );
            return false;
        }
    }

    // =====================================================================================================================
    // DARKLIGHT GREATSWORD BLUE/PURPLE BEAMS (P2 ATTACK 7)
    // =====================================================================================================================
    public class CryogenDarkBeam : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/Melee/DarkBeam";

        public override void SetDefaults()
        {
            Projectile.width = 30;
            Projectile.height = 30;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 280;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.localAI[0]++;

            if (Projectile.localAI[0] < 36f)
            {
                // Slow phase
                Projectile.velocity = Projectile.velocity.SafeNormalize(Vector2.UnitX) * 3f;
                Projectile.scale = 2.0f;
                Projectile.alpha = 150; // Semi-transparent
            }
            else if (Projectile.localAI[0] == 36f)
            {
                // Trigger acceleration leap!
                Projectile.velocity = Projectile.velocity.SafeNormalize(Vector2.UnitX) * 28f;
                Projectile.scale = 1.0f;
                Projectile.alpha = 0;
                SoundEngine.PlaySound(SoundID.Item71 with { Volume = 0.7f, Pitch = 0.1f }, Projectile.Center);
            }

            Projectile.rotation = Projectile.velocity.ToRotation();
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Projectiles/Melee/DarkBeam", AssetRequestMode.ImmediateLoad).Value;
            if (tex == null)
                return true;

            Color c = Color.Purple * ((255 - Projectile.alpha) / 255f) * 0.8f;
            Main.spriteBatch.Draw(
                tex,
                Projectile.Center - Main.screenPosition,
                null,
                c,
                Projectile.rotation,
                tex.Size() * 0.5f,
                Projectile.scale,
                SpriteEffects.None,
                0f
            );
            return false;
        }
    }

    public class CryogenLightBeam : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/Melee/LightBeam";

        public override void SetDefaults()
        {
            Projectile.width = 24;
            Projectile.height = 24;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 240;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.velocity = Projectile.velocity.SafeNormalize(Vector2.UnitX) * 16f;
            Projectile.rotation = Projectile.velocity.ToRotation();
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Projectiles/Melee/LightBeam", AssetRequestMode.ImmediateLoad).Value;
            if (tex == null)
                return true;

            Main.spriteBatch.Draw(
                tex,
                Projectile.Center - Main.screenPosition,
                null,
                Color.DeepSkyBlue * 0.85f,
                Projectile.rotation,
                tex.Size() * 0.5f,
                Projectile.scale,
                SpriteEffects.None,
                0f
            );
            return false;
        }
    }

    // =====================================================================================================================
    // STARNIGHT LANCE BEAM & TELEGRAPH (P2 ATTACK 8)
    // =====================================================================================================================
    public class CryogenStarnightBeam : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/Melee/StarnightBeam";

        public override void SetDefaults()
        {
            Projectile.width = 18;
            Projectile.height = 36;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 120;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            // Laser speed
            Projectile.velocity = Projectile.velocity.SafeNormalize(Vector2.UnitX) * 40f;
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Projectiles/Melee/StarnightBeam", AssetRequestMode.ImmediateLoad).Value;
            if (tex == null)
                return true;

            Main.spriteBatch.Draw(
                tex,
                Projectile.Center - Main.screenPosition,
                null,
                Color.Cyan * 0.95f,
                Projectile.rotation,
                tex.Size() * 0.5f,
                Projectile.scale,
                SpriteEffects.None,
                0f
            );
            return false;
        }
    }

    // =====================================================================================================================
    // SHADECRYSTAL BARRAGE (P2 ATTACK 9)
    // =====================================================================================================================
    public class CryogenShadecrystal : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/Magic/ShadecrystalProjectile";

        public override void SetDefaults()
        {
            Projectile.width = 16;
            Projectile.height = 16;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = 1;
            Projectile.timeLeft = 220;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;

            if (Main.rand.NextBool(4))
            {
                Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.Vortex, -Projectile.velocity * 0.1f, 120, Color.Purple, 0.8f);
                d.noGravity = true;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Projectiles/Magic/ShadecrystalProjectile", AssetRequestMode.ImmediateLoad).Value;
            if (tex == null)
                return true;

            Main.spriteBatch.Draw(
                tex,
                Projectile.Center - Main.screenPosition,
                null,
                Color.MediumPurple * 0.95f,
                Projectile.rotation,
                tex.Size() * 0.5f,
                Projectile.scale,
                SpriteEffects.None,
                0f
            );
            return false;
        }
    }

    // =====================================================================================================================
    // DAEDALUS GOLEM PELLET & LIGHTNING (P2 ATTACK 10)
    // =====================================================================================================================
    public class CryogenDaedalusPellet : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/Summon/DaedalusPellet";

        public override void SetDefaults()
        {
            Projectile.width = 14;
            Projectile.height = 14;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = 1;
            Projectile.timeLeft = 180;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.velocity.Y += 0.08f; // Falling gravity
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Projectiles/Summon/DaedalusPellet", AssetRequestMode.ImmediateLoad).Value;
            if (tex == null)
                return true;

            Main.spriteBatch.Draw(
                tex,
                Projectile.Center - Main.screenPosition,
                null,
                Color.LightBlue,
                Projectile.rotation,
                tex.Size() * 0.5f,
                Projectile.scale,
                SpriteEffects.None,
                0f
            );
            return false;
        }
    }

    public class CryogenDaedalusLightning : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/InvisibleProj";

        public override void SetDefaults()
        {
            Projectile.width = 1600;
            Projectile.height = 40;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 48; // 18 frames telegraph, 30 frames active
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override bool? CanDamage() => Projectile.timeLeft <= 30;

        public override void AI()
        {
            // Lock Y position to Golem's height or player's targeted Y coordinate
            // ai[0] contains Golem's Y anchor height
            Projectile.Center = new Vector2(Projectile.Center.X, Projectile.ai[0]);
            Projectile.velocity = Vector2.Zero;
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            if (Projectile.timeLeft > 30)
                return false;

            // Simple horizontal line collision check
            float startX = Projectile.Center.X - 800f;
            float endX = Projectile.Center.X + 800f;
            float y = Projectile.Center.Y;

            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(
                targetHitbox.TopLeft(),
                targetHitbox.Size(),
                new Vector2(startX, y),
                new Vector2(endX, y),
                20f,
                ref collisionPoint
            );
        }

        public override bool PreDraw(ref Color lightColor)
        {
            SpriteBatch spriteBatch = Main.spriteBatch;
            Vector2 start = new Vector2(Projectile.Center.X - 800f, Projectile.Center.Y);
            Vector2 end = new Vector2(Projectile.Center.X + 800f, Projectile.Center.Y);

            bool active = Projectile.timeLeft <= 30;
            float opacity = active ? MathHelper.Clamp(Projectile.timeLeft / 15f, 0f, 1f) : 0.42f + 0.18f * (float)Math.Sin(Main.GameUpdateCount * 0.3f);

            Color color = Color.Lerp(Color.DeepSkyBlue, Color.Purple, 0.4f);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);

            if (!active)
            {
                // Draw thin orange warning telegraph line
                DrawLineHelper(spriteBatch, start, end, Color.Red * opacity, 3f);
            }
            else
            {
                // Load Calamity lightning texture if available, else fallback to magic pixel lines
                Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Projectiles/Summon/DaedalusLightning", AssetRequestMode.ImmediateLoad).Value;
                if (tex != null)
                {
                    // Draw repeating lightning texture across the screen
                    float length = 1600f;
                    int segments = 12;
                    float segLen = length / segments;

                    for (int i = 0; i < segments; i++)
                    {
                        Vector2 pos = start + new Vector2(i * segLen + segLen * 0.5f, 0f);
                        spriteBatch.Draw(
                            tex,
                            pos - Main.screenPosition,
                            null,
                            color * opacity,
                            0f,
                            tex.Size() * 0.5f,
                            new Vector2(segLen / tex.Width, 1.2f),
                            SpriteEffects.None,
                            0f
                        );
                    }
                }
                else
                {
                    // Fallback to additive line drawing
                    DrawLineHelper(spriteBatch, start, end, color * opacity, 16f);
                    DrawLineHelper(spriteBatch, start, end, Color.White * opacity, 4f);
                }
            }

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);

            return false;
        }

        private static void DrawLineHelper(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, float width)
        {
            Vector2 delta = end - start;
            float length = delta.Length();
            float rotation = delta.ToRotation();
            spriteBatch.Draw(
                TextureAssets.MagicPixel.Value,
                start - Main.screenPosition,
                null,
                color,
                rotation,
                new Vector2(0f, 0.5f),
                new Vector2(length, width),
                SpriteEffects.None,
                0f
            );
        }
    }

    // =====================================================================================================================
    // SHIMMERSPARK YOYO & STARS (P2 ATTACK 11)
    // =====================================================================================================================
    public class CryogenShimmersparkYoyo : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/Melee/Yoyos/ShimmersparkYoyo";

        public override void SetDefaults()
        {
            Projectile.width = 28;
            Projectile.height = 28;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 400;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.rotation += 0.16f;
            Projectile.localAI[0]++;

            NPC parent = Main.npc[(int)Projectile.ai[0]];
            if (!parent.active || parent.type != ModContent.NPCType<CalamityMod.NPCs.Cryogen.Cryogen>())
            {
                Projectile.Kill();
                return;
            }

            // Orbital expansion logic
            float radius = 140f;
            if (Projectile.timeLeft < 20f)
            {
                float factor = (20f - Projectile.timeLeft) / 20f;
                radius = MathHelper.Lerp(140f, 280f, factor);
            }

            float speed = 0.015f;
            float angle = Projectile.ai[1] + Projectile.localAI[0] * speed;
            Projectile.Center = parent.Center + angle.ToRotationVector2() * radius;

            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            // Emit yoyo stars periodically
            if (Projectile.localAI[0] % 20f == 0f && Projectile.timeLeft >= 20f)
            {
                Vector2 vel = Main.rand.NextFloat(MathHelper.TwoPi).ToRotationVector2() * 4f;
                Projectile.NewProjectile(
                    Projectile.GetSource_FromThis(),
                    Projectile.Center,
                    vel,
                    ModContent.ProjectileType<CryogenYoyoStar>(),
                    Projectile.damage,
                    0f,
                    Main.myPlayer
                );
            }

            // Outward burst at end
            if (Projectile.timeLeft == 20f)
            {
                for (int i = 0; i < 6; i++)
                {
                    Vector2 vel = (MathHelper.TwoPi * i / 6f).ToRotationVector2() * 4f;
                    Projectile.NewProjectile(
                        Projectile.GetSource_FromThis(),
                        Projectile.Center,
                        vel,
                        ModContent.ProjectileType<CryogenYoyoStar>(),
                        Projectile.damage,
                        0f,
                        Main.myPlayer
                    );
                }
                SoundEngine.PlaySound(SoundID.Item28 with { Volume = 0.6f }, Projectile.Center);
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Projectiles/Melee/Yoyos/ShimmersparkYoyo", AssetRequestMode.ImmediateLoad).Value;
            if (tex == null)
                return true;

            // Glowing yoyo trails
            Color glowColor = Color.Magenta * 0.4f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 offset = (MathHelper.TwoPi * i / 4f).ToRotationVector2() * 3f;
                Main.spriteBatch.Draw(
                    tex,
                    Projectile.Center + offset - Main.screenPosition,
                    null,
                    glowColor,
                    Projectile.rotation,
                    tex.Size() * 0.5f,
                    1.2f,
                    SpriteEffects.None,
                    0f
                );
            }

            Main.spriteBatch.Draw(
                tex,
                Projectile.Center - Main.screenPosition,
                null,
                Color.White,
                Projectile.rotation,
                tex.Size() * 0.5f,
                1.2f,
                SpriteEffects.None,
                0f
            );
            return false;
        }
    }

    public class CryogenYoyoStar : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/InvisibleProj";

        public override void SetDefaults()
        {
            Projectile.width = 12;
            Projectile.height = 12;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = 1;
            Projectile.timeLeft = 150;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.rotation += 0.05f;

            // Weak homing towards player
            Player player = Main.player[Player.FindClosest(Projectile.Center, 1, 1)];
            if (player.active && !player.dead)
            {
                Vector2 destDir = (player.Center - Projectile.Center).SafeNormalize(Vector2.UnitY);
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, destDir * 4f, 0.024f);
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // Draw as a purple glowing spark circle
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            Rectangle rect = new Rectangle((int)drawPos.X - 6, (int)drawPos.Y - 6, 12, 12);
            Main.spriteBatch.Draw(pixel, rect, Color.MediumOrchid * 0.8f);
            return false;
        }
    }

    // =====================================================================================================================
    // DARKECHO bow arrow and CRYSTAL DART (P2 ATTACK 11)
    // =====================================================================================================================
    public class CryogenDarkechoArrow : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/InvisibleProj";

        public override void SetDefaults()
        {
            Projectile.width = 10;
            Projectile.height = 10;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = 1;
            Projectile.timeLeft = 200;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;

            if (Main.rand.NextBool(3))
            {
                Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.Vortex, -Projectile.velocity * 0.05f, 100, Color.MediumPurple, 0.8f);
                d.noGravity = true;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // Render a telegraphed purple laser bolt
            Vector2 start = Projectile.Center - Projectile.velocity.SafeNormalize(Vector2.UnitY) * 20f;
            DrawLineHelper(Main.spriteBatch, start, Projectile.Center, Color.MediumPurple * 0.8f, 3f);
            DrawLineHelper(Main.spriteBatch, start, Projectile.Center, Color.White * 0.5f, 1f);
            return false;
        }

        private static void DrawLineHelper(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, float width)
        {
            Vector2 delta = end - start;
            float length = delta.Length();
            float rotation = delta.ToRotation();
            spriteBatch.Draw(
                TextureAssets.MagicPixel.Value,
                start - Main.screenPosition,
                null,
                color,
                rotation,
                new Vector2(0f, 0.5f),
                new Vector2(length, width),
                SpriteEffects.None,
                0f
            );
        }
    }

    public class CryogenCrystalDart : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/InvisibleProj";

        public override void SetDefaults()
        {
            Projectile.width = 12;
            Projectile.height = 12;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 240;
            Projectile.tileCollide = true; // Bounces off solid walls
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            Projectile.ai[0]++; // Count bounces
            if (Projectile.ai[0] > 1)
            {
                Projectile.Kill();
                return true;
            }

            // Single bounce reflection
            Collision.HitTiles(Projectile.position, Projectile.velocity, Projectile.width, Projectile.height);
            SoundEngine.PlaySound(SoundID.Item10, Projectile.Center);

            if (Projectile.velocity.X != oldVelocity.X)
            {
                Projectile.velocity.X = -oldVelocity.X;
            }
            if (Projectile.velocity.Y != oldVelocity.Y)
            {
                Projectile.velocity.Y = -oldVelocity.Y;
            }

            return false;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // Draws as a glowing pink crystal dart
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            Rectangle rect = new Rectangle((int)drawPos.X - 5, (int)drawPos.Y - 5, 10, 10);
            Main.spriteBatch.Draw(pixel, rect, Color.DeepPink * 0.9f);
            return false;
        }
    }

    // =====================================================================================================================
    // CRYSTAL PIERCER JAVELIN & SHARDS (P2 ATTACK 12)
    // =====================================================================================================================
    public class CryogenJavelin : ModProjectile
    {
        public override string Texture => "CalamityMod/Items/Weapons/Rogue/CrystalPiercer";

        public override void SetDefaults()
        {
            Projectile.width = 18;
            Projectile.height = 42;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 300;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.localAI[0]++;

            // Gravity arc behavior or straight path based on ai[0]
            // ai[0] = 0 (gravity arc from left), 1 (straight from right), 2 (vertical straight from top)
            int mode = (int)Projectile.ai[0];

            if (mode == 0)
            {
                Projectile.velocity.Y += 0.25f; // Gravity
            }

            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;

            // Emit shard trailing every 14 frames
            if (Projectile.localAI[0] % 14f == 0f && Main.netMode != NetmodeID.MultiplayerClient)
            {
                Projectile.NewProjectile(
                    Projectile.GetSource_FromThis(),
                    Projectile.Center,
                    new Vector2(0f, 6f), // Vertical fall
                    ModContent.ProjectileType<CryogenJavelinShard>(),
                    Projectile.damage,
                    0f,
                    Main.myPlayer
                );
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Items/Weapons/Rogue/CrystalPiercer", AssetRequestMode.ImmediateLoad).Value;
            if (tex == null)
                return true;

            Main.spriteBatch.Draw(
                tex,
                Projectile.Center - Main.screenPosition,
                null,
                Color.White,
                Projectile.rotation,
                tex.Size() * 0.5f,
                1.4f,
                SpriteEffects.None,
                0f
            );
            return false;
        }
    }

    public class CryogenJavelinShard : ModProjectile
    {
        public override string Texture => "CalamityMod/Projectiles/Rogue/CrystalPiercerShard";

        public override void SetDefaults()
        {
            Projectile.width = 8;
            Projectile.height = 8;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.penetrate = 1;
            Projectile.timeLeft = 80;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            Projectile.velocity = new Vector2(0f, 6f); // Constant vertical falling speed
            Projectile.rotation += 0.05f;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = ModContent.Request<Texture2D>("CalamityMod/Projectiles/Rogue/CrystalPiercerShard", AssetRequestMode.ImmediateLoad).Value;
            if (tex == null)
                return true;

            Main.spriteBatch.Draw(
                tex,
                Projectile.Center - Main.screenPosition,
                null,
                Color.Cyan * 0.85f,
                Projectile.rotation,
                tex.Size() * 0.5f,
                Projectile.scale,
                SpriteEffects.None,
                0f
            );
            return false;
        }
    }
}
