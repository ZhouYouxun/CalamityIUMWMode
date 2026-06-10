using System.IO;
using CalamityIUMWMode.Core.Systems;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityIUMWMode.Core.Netcode
{
    internal enum IUMWPacketType : byte
    {
        SyncMode
    }

    internal static class IUMWPacketHandler
    {
        public static void SendModeSync(int toClient = -1, int ignoreClient = -1)
        {
            if (Main.netMode == NetmodeID.SinglePlayer || CalamityIUMWMode.Instance is null)
                return;

            ModPacket packet = CalamityIUMWMode.Instance.GetPacket();
            packet.Write((byte)IUMWPacketType.SyncMode);
            packet.Write(IUMWWorldSystem.IUMWModeEnabled);
            packet.Send(toClient, ignoreClient);
        }

        public static void HandlePacket(BinaryReader reader, int whoAmI)
        {
            IUMWPacketType packetType = (IUMWPacketType)reader.ReadByte();

            if (packetType == IUMWPacketType.SyncMode)
            {
                bool enabled = reader.ReadBoolean();
                IUMWWorldSystem.SetModeEnabled(enabled, sync: false);

                if (Main.netMode == NetmodeID.Server)
                    SendModeSync(ignoreClient: whoAmI);
            }
        }
    }
}
