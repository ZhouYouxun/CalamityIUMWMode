using System;
using System.Collections.Generic;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.UI.Chat;
using Terraria.UI;

namespace CalamityIUMWMode.Core.Systems
{
    public class IUMWDebugSystem : ModSystem
    {
        private static IUMWDebugInfo currentInfo;

        private static float currentDistance;

        public override void PreUpdateNPCs() => ClearFrame();

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            int mouseTextIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text", StringComparison.Ordinal));
            if (mouseTextIndex < 0)
                return;

            layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer("CalamityIUMWMode: IUMW Debug", DrawDebugText, InterfaceScaleType.UI));
        }

        public static void Clear()
        {
            currentInfo = null;
            currentDistance = float.MaxValue;
        }

        internal static void Report(NPC npc, IUMWBossAI ai, IUMWGlobalNPC data)
        {
            if (Main.dedServ || Main.LocalPlayer is null || !npc.active)
                return;

            float distance = Vector2.Distance(Main.LocalPlayer.Center, npc.Center);
            if (currentInfo is not null && distance >= currentDistance)
                return;

            currentDistance = distance;
            currentInfo = new IUMWDebugInfo(
                ai.BossName,
                data.CurrentPhase,
                ai.PhaseLifeRatios.Length + 1,
                ai.PhaseName(data.CurrentPhase),
                ai.StateName(data.AttackState),
                data.AttackTimer,
                npc.lifeMax <= 0 ? 1f : npc.life / (float)npc.lifeMax,
                ai.DebugColor);
        }

        private static void ClearFrame()
        {
            currentInfo = null;
            currentDistance = float.MaxValue;
        }

        private static bool DrawDebugText()
        {
            if (!IUMWWorldSystem.IUMWModeEnabled || Main.gameMenu)
                return true;

            string text = currentInfo is null
                ? "IUMW DEBUG\nBoss: none\nMode: waiting for tracked boss"
                : $"IUMW DEBUG\nBoss: {currentInfo.BossName}\nPhase: {currentInfo.Phase}/{currentInfo.MaxPhase} - {currentInfo.PhaseName}\nState: {currentInfo.StateName}\nTimer: {currentInfo.Timer}\nLife: {currentInfo.LifeRatio:P0}";

            DrawLines(text, new Vector2(18f, 18f), currentInfo?.Color ?? new Color(88, 255, 211));
            return true;
        }

        private static void DrawLines(string text, Vector2 position, Color color)
        {
            DynamicSpriteFont font = FontAssets.MouseText.Value;
            string[] lines = text.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                Vector2 linePosition = position + Vector2.UnitY * i * 18f;
                ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, font, lines[i], linePosition, color, 0f, Vector2.Zero, Vector2.One * 0.85f);
            }
        }

        private sealed record IUMWDebugInfo(string BossName, int Phase, int MaxPhase, string PhaseName, string StateName, int Timer, float LifeRatio, Color Color);
    }
}
