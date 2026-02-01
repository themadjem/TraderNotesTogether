using System.Collections.Generic;
using ProtoBuf;
using TraderNotesTogether.ProtoTrader;

namespace TraderNotesTogether.NetworkPackets
{
    [ProtoContract]
    public class TraderUpdatePacket
    {
        [ProtoMember(1)]
        public ProtoTraderEntity Trader;
    }

    [ProtoContract]
    public class TraderSyncPacket
    {
        [ProtoMember(1)]
        public ProtoTraderEntity Trader;
    }

    [ProtoContract]
    public class TraderBulkSyncPacket
    {
        [ProtoMember(1)]
        public List<ProtoTraderEntity> Traders;
    }
}
