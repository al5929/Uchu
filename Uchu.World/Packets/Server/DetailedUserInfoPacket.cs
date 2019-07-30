using RakDotNet.IO;
using Uchu.Core;
using Uchu.World.Collections;

namespace Uchu.World
{
    public class DetailedUserInfoPacket : Packet
    {
        public override RemoteConnectionType RemoteConnectionType => RemoteConnectionType.Server;

        public override uint PacketId => 0x4;
        
        public LegoDataDictionary Data { get; set; }

        public override void SerializePacket(BitWriter writer)
        {
            writer.WriteLDFCompressed(Data);
        }
    }
}