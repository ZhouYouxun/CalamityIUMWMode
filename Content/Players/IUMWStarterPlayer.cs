using CalamityIUMWMode.Content.Items;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace CalamityIUMWMode.Content.Players
{
    public class IUMWStarterPlayer : ModPlayer
    {
        private bool receivedMatrixTablet;

        public override void OnEnterWorld()
        {
            if (receivedMatrixTablet)
                return;

            Player.QuickSpawnItem(Player.GetSource_Misc("CalamityIUMWModeStart"), ModContent.ItemType<IUMWMatrixTablet>());
            receivedMatrixTablet = true;
        }

        public override void SaveData(TagCompound tag)
        {
            if (receivedMatrixTablet)
                tag["ReceivedIUMWMatrixTablet"] = true;
        }

        public override void LoadData(TagCompound tag)
        {
            receivedMatrixTablet = tag.GetBool("ReceivedIUMWMatrixTablet");
        }
    }
}
