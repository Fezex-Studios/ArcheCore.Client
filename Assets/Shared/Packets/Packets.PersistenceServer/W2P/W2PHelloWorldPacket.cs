using MessagePack;

namespace Shared.Packets.Requests
{
    [MessagePackObject(true)]
    public class W2PHelloWorldPacket
    {
        public string Message;
    }
}