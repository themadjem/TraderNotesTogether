using System.Collections.Generic;
using ProtoBuf;
using TraderMapTooltip;

namespace TraderNotesTogether.ProtoTrader
{
    [ProtoContract]
    public class ProtoTraderItem
    {
        [ProtoMember(1)]
        public string Name;

        [ProtoMember(2)]
        public int Stock;

        [ProtoMember(3)]
        public int StackSize;

        [ProtoMember(4)]
        public int Price;

        [ProtoMember(5)]
        public bool IsSoldOut;
    }

    [ProtoContract]
    public class ProtoTraderEntity
    {
        [ProtoMember(1)]
        public string Name;

        [ProtoMember(2)]
        public string TraderType;

        [ProtoMember(3)]
        public long EntityId;

        [ProtoMember(4)]
        public int Money;

        [ProtoMember(5)]
        public bool IsDiscovered;

        [ProtoMember(6)]
        public double LastUpdatedTotalDays;

        [ProtoMember(7)]
        public double NextRefreshTotalDays;

        [ProtoMember(8)]
        public List<ProtoTraderItem> Sells = new List<ProtoTraderItem>();

        [ProtoMember(9)]
        public List<ProtoTraderItem> Wants = new List<ProtoTraderItem>();

        [ProtoMember(10)]
        public double X;

        [ProtoMember(11)]
        public double Y;

        [ProtoMember(12)]
        public double Z;

        public SavedTrader ToSavedTrader()
        {
            var trader = new SavedTrader()
            {
                Name = this.Name,
                TraderType = this.TraderType,
                EntityId = this.EntityId,
                Money = this.Money,
                IsDiscovered = this.IsDiscovered,
                LastUpdatedTotalDays = this.LastUpdatedTotalDays,
                NextRefreshTotalDays = this.NextRefreshTotalDays,
                X = this.X,
                Y = this.Y,
                Z = this.Z,
            };

            foreach (var item in this.Sells)
            {
                trader.Sells.Add(
                    new CachedTradeItem
                    {
                        Name = item.Name,
                        Stock = item.Stock,
                        StackSize = item.StackSize,
                        Price = item.Price,
                        IsSoldOut = item.IsSoldOut,
                    }
                );
            }

            foreach (var item in this.Wants)
            {
                trader.Wants.Add(
                    new CachedTradeItem
                    {
                        Name = item.Name,
                        Stock = item.Stock,
                        StackSize = item.StackSize,
                        Price = item.Price,
                        IsSoldOut = item.IsSoldOut,
                    }
                );
            }

            return trader;
        }

        public static ProtoTraderEntity FromSavedTrader(SavedTrader trader)
        {
            var snapshot = new ProtoTraderEntity
            {
                Name = trader.Name,
                TraderType = trader.TraderType,
                EntityId = trader.EntityId,
                Money = trader.Money,
                IsDiscovered = trader.IsDiscovered,
                LastUpdatedTotalDays = trader.LastUpdatedTotalDays,
                NextRefreshTotalDays = trader.NextRefreshTotalDays,
                X = trader.X,
                Y = trader.Y,
                Z = trader.Z,
            };

            foreach (var item in trader.Sells)
            {
                snapshot.Sells.Add(
                    new ProtoTraderItem
                    {
                        Name = item.Name,
                        Stock = item.Stock,
                        StackSize = item.StackSize,
                        Price = item.Price,
                        IsSoldOut = item.IsSoldOut,
                    }
                );
            }

            foreach (var item in trader.Wants)
            {
                snapshot.Wants.Add(
                    new ProtoTraderItem
                    {
                        Name = item.Name,
                        Stock = item.Stock,
                        StackSize = item.StackSize,
                        Price = item.Price,
                        IsSoldOut = item.IsSoldOut,
                    }
                );
            }

            return snapshot;
        }
    }
}
