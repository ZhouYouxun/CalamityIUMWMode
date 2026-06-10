using System.IO;
using CalamityIUMWMode.Content.BehaviorOverrides.BossAIs.Common;
using CalamityIUMWMode.Content.UI;
using CalamityIUMWMode.Core.Netcode;
using CalamityMod.Systems;
using Terraria.ModLoader;

namespace CalamityIUMWMode
{
	// Please read https://github.com/tModLoader/tModLoader/wiki/Basic-tModLoader-Modding-Guide#mod-skeleton-contents for more information about the various files in a mod.
	public class CalamityIUMWMode : Mod
	{
		internal static CalamityIUMWMode Instance { get; private set; }

		public override void Load() 
		{
			Instance = this;

			IUMWDifficulty difficulty = new();
			DifficultyModeSystem.Difficulties.Add(difficulty);
			DifficultyModeSystem.CalculateDifficultyData();
		}

		public override void HandlePacket(BinaryReader reader, int whoAmI) => IUMWPacketHandler.HandlePacket(reader, whoAmI);

		public override void PostSetupContent()
		{
			IUMWBossAIRegistry.Load();
		}

		public override void Unload()
		{
			IUMWBossAIRegistry.Unload();
			Instance = null;
		}
	}
}
