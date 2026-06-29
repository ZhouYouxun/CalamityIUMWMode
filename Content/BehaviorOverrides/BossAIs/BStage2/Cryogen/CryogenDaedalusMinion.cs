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
    public class CryogenDaedalusMinion : ModNPC
    {
        public override string Texture => "CalamityMod/Projectiles/InvisibleProj";

        public override void SetStaticDefaults()
        {
            NPCID.Sets.MustAlwaysDraw[Type] = true;
            NPCID.Sets.CantTakeLunchMoney[Type] = true;
            NPCID.Sets.BossBestiaryPriority.Add(Type);
        }

        public override void SetDefaults()
        {
            NPC.width = 40;
            NPC.height = 46;
            NPC.damage = 50;
            NPC.defense = 10;
            NPC.lifeMax = 600;
            NPC.HitSound = SoundID.NPCHit5;
            NPC.DeathSound = SoundID.NPCDeath15;
            NPC.noGravity = true;
            NPC.noTileCollide = true;
            NPC.knockBackResist = 0f;
            NPC.dontCountMe = true; // Does not count towards world completion or generic boss bar
        }

        public override void AI()
        {
            // ai[0]: Parent index
            // ai[1]: Minion type (0 = Golem A / Pellets, 1 = Golem B / Lightning)
            // ai[2]: Starting angle offset
            int parentIndex = (int)NPC.ai[0];
            if (parentIndex < 0 || parentIndex >= Main.maxNPCs)
            {
                NPC.active = false;
                return;
            }

            NPC parent = Main.npc[parentIndex];
            if (!parent.active || parent.type != ModContent.NPCType<CalamityMod.NPCs.Cryogen.Cryogen>())
            {
                NPC.active = false;
                return;
            }

            // Orbit around parent
            NPC.localAI[0]++; // Timer for rotation
            float orbitRadius = 100f;
            float speed = 0.022f;
            float currentAngle = NPC.ai[2] + NPC.localAI[0] * speed;
            NPC.Center = parent.Center + currentAngle.ToRotationVector2() * orbitRadius;
            NPC.velocity = Vector2.Zero;

            // Face the player
            Player player = Main.player[parent.target];
            if (player.active && !player.dead)
            {
                NPC.direction = NPC.spriteDirection = Math.Sign(player.Center.X - NPC.Center.X);
            }

            // Attack Logic
            int type = (int)NPC.ai[1];
            NPC.localAI[1]++; // Attack timer

            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            if (type == 0) // Golem A: Pellet Spread
            {
                if (NPC.localAI[1] >= 60f)
                {
                    NPC.localAI[1] = 0f;
                    if (player.active && !player.dead)
                    {
                        // Fire a 3-pellet spread towards player's predicted position
                        Vector2 targetPos = player.Center + player.velocity * 12f;
                        Vector2 baseVel = (targetPos - NPC.Center).SafeNormalize(Vector2.UnitY) * 9f;
                        float spread = MathHelper.ToRadians(15f);

                        for (int i = -1; i <= 1; i++)
                        {
                            Vector2 vel = baseVel.RotatedBy(i * spread);
                            Projectile.NewProjectile(
                                NPC.GetSource_FromAI(),
                                NPC.Center,
                                vel,
                                ModContent.ProjectileType<CryogenDaedalusPellet>(),
                                parent.damage / 3,
                                0f,
                                Main.myPlayer
                            );
                        }
                        SoundEngine.PlaySound(SoundID.Item30 with { Volume = 0.6f, Pitch = 0.2f }, NPC.Center);
                    }
                }
            }
            else // Golem B: Lightning
            {
                if (NPC.localAI[1] >= 90f)
                {
                    NPC.localAI[1] = 0f;
                    if (player.active && !player.dead)
                    {
                        // Shoot a horizontal telegraphed lightning bolt at the player's Y level
                        // Spawn the lightning projectile at the golem's position, aligned horizontally
                        Vector2 spawnPos = new Vector2(player.Center.X, player.Center.Y);
                        Projectile.NewProjectile(
                            NPC.GetSource_FromAI(),
                            spawnPos,
                            new Vector2(player.direction, 0f),
                            ModContent.ProjectileType<CryogenDaedalusLightning>(),
                            parent.damage / 2,
                            0f,
                            Main.myPlayer,
                            NPC.Center.Y // Pass the Golem's Y coordinate to anchor the lightning height
                        );
                    }
                }
            }
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            // Dynamically load the Calamity Golem projectile texture (since it is a 18-frame sheet)
            Texture2D texture = ModContent.Request<Texture2D>("CalamityMod/Projectiles/Summon/DaedalusGolem", AssetRequestMode.ImmediateLoad).Value;
            if (texture == null)
                return true;

            // Golem texture has 18 frames. Let's calculate the frame based on localAI[0]
            int frameCount = 18;
            int currentFrame = (int)(NPC.localAI[0] / 4) % frameCount;
            int frameHeight = texture.Height / frameCount;
            Rectangle sourceRect = new Rectangle(0, currentFrame * frameHeight, texture.Width, frameHeight);
            Vector2 origin = sourceRect.Size() * 0.5f;

            SpriteEffects effects = NPC.spriteDirection == 1 ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

            // Draw a light blue glow outline for the minion
            Color glowColor = Color.DeepSkyBlue * 0.45f * NPC.Opacity;
            for (int i = 0; i < 8; i++)
            {
                Vector2 offset = (MathHelper.TwoPi * i / 8f).ToRotationVector2() * 2f;
                spriteBatch.Draw(
                    texture,
                    NPC.Center + offset - screenPos,
                    sourceRect,
                    glowColor,
                    NPC.rotation,
                    origin,
                    NPC.scale,
                    effects,
                    0f
                );
            }

            // Draw the Golem body
            spriteBatch.Draw(
                texture,
                NPC.Center - screenPos,
                sourceRect,
                NPC.GetAlpha(drawColor),
                NPC.rotation,
                origin,
                NPC.scale,
                effects,
                0f
            );

            return false;
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (NPC.life <= 0)
            {
                // Emit ice particles on death
                for (int i = 0; i < 20; i++)
                {
                    Dust d = Dust.NewDustPerfect(NPC.Center, DustID.Ice, Main.rand.NextVector2Circular(6f, 6f), 100, Color.DeepSkyBlue, Main.rand.NextFloat(0.8f, 1.4f));
                    d.noGravity = true;
                }
            }
        }
    }
}
