using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ProtoBuf;
using TraderMapTooltip;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace TraderNotesTogether
{
    class TraderNotesTogetherMod : ModSystem
    {
        private ICoreClientAPI capi;
        private ICoreServerAPI sapi;
        private IServerNetworkChannel serverChannel;
        private IClientNetworkChannel clientChannel;
        private CacheObserver cacheObserver;
        private ServerCacheStore cacheStore;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.World.Logger.Notification(LogUtil.ModMessage("ModSystem started!"));
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            sapi.World.Logger.Debug(LogUtil.ModMessage("Starting server side"));
            serverChannel = api
                .Network.RegisterChannel("tradernotestogether")
                .RegisterMessageType<TraderUpdatePacket>()
                .RegisterMessageType<TraderSyncPacket>()
                .RegisterMessageType<TraderBulkSyncPacket>()
                .SetMessageHandler<TraderUpdatePacket>(OnTraderUpdateFromClient);

            api.Event.PlayerJoin += OnPlayerJoin;
            cacheStore = new ServerCacheStore(api);
            sapi.World.Logger.Debug(LogUtil.ModMessage("Server side startup complete"));
        }

        private void OnPlayerJoin(IServerPlayer player)
        {
            sapi.World.Logger.Debug(LogUtil.ModMessage($"Player {player.PlayerName} joined"));
            sapi.World.Logger.Debug(
                LogUtil.ModMessage(
                    $"Player Role ({player.Role.Name}) priv: {string.Join(",", player.Role.Privileges)}"
                )
            );
            sapi.World.Logger.Debug(
                LogUtil.ModMessage($"Player priv: {string.Join(",", player.Privileges)}")
            );
            if (!CanReceive(player))
            {
                sapi.World.Logger.Debug(LogUtil.ModMessage("Player cannot receive updates"));
                return;
            }
            var snapshots = cacheStore.Cache.Values.Select(TraderSnapshot.FromSavedTrader).ToList();
            if (snapshots.Count > 0)
            {
                sapi.World.Logger.Debug(LogUtil.ModMessage("Sending player bulk update"));
                serverChannel.SendPacket(new TraderBulkSyncPacket { Traders = snapshots }, player);
            }
        }

        private void OnTraderUpdateFromClient(IServerPlayer fromPlayer, TraderUpdatePacket packet)
        {
            sapi.World.Logger.Debug(
                LogUtil.ModMessage(
                    $"Received update from {fromPlayer.PlayerName} for trader {packet.Trader.EntityId}"
                )
            );
            if (!CanShare(fromPlayer))
                return;
            long EntityId = packet.Trader.EntityId;
            if (
                cacheStore.TraderExists(EntityId)
                && cacheStore.GetTrader(EntityId).LastUpdatedTotalDays
                    >= packet.Trader.LastUpdatedTotalDays
            )
                return;

            cacheStore.UpdateTrader(packet.Trader.ToSavedTrader());
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            capi.Logger.Debug(LogUtil.ModMessage("Starting Client side"));
            /*
             * if (capi.IsSinglePlayer)
            {
                capi.Logger.Debug(
                    LogUtil.ModMessage("Single-player detected, networking disabled.")
                );
                return;
            }
            */
            clientChannel = api
                .Network.RegisterChannel("tradernotestogether")
                .RegisterMessageType<TraderUpdatePacket>()
                .RegisterMessageType<TraderSyncPacket>()
                .RegisterMessageType<TraderBulkSyncPacket>()
                .SetMessageHandler<TraderSyncPacket>(OnTraderUpdateFromServer)
                .SetMessageHandler<TraderBulkSyncPacket>(OnTraderUpdateFromServer);

            cacheObserver = new CacheObserver(capi);
            cacheObserver.OnTraderUpdated += trader =>
            {
                capi.Logger.Debug(LogUtil.ModMessage($"Update to trader {trader.EntityId}"));
                SendTraderUpdateToServer(trader);
            };
            if (cacheObserver.Equals(null))
            {
                capi.Logger.Error(LogUtil.ModMessage("Cache Observer not instantiated"));
            }
            capi.Event.RegisterGameTickListener(OnClientTick, 1000);
            capi.Logger.Debug(LogUtil.ModMessage("Client startup completed"));
        }

        private void OnTraderUpdateFromServer(TraderSyncPacket packet)
        {
            capi.Logger.Debug(
                LogUtil.ModMessage($"Recieved update for trader {packet.Trader.EntityId}")
            );
            TraderMapMod.Cache[packet.Trader.EntityId] = packet.Trader.ToSavedTrader();
        }

        private void OnTraderUpdateFromServer(TraderBulkSyncPacket packet)
        {
            capi.Logger.Debug(
                LogUtil.ModMessage(
                    $"Received bulk update for traders {string.Join(",", packet.Traders.Select(trader => trader.EntityId))}"
                )
            );
            foreach (TraderSnapshot trader in packet.Traders)
            {
                if (
                    !TraderMapMod.Cache.TryGetValue(trader.EntityId, out var local)
                    || trader.LastUpdatedTotalDays > local.LastUpdatedTotalDays
                )
                    TraderMapMod.Cache[trader.EntityId] = trader.ToSavedTrader();
            }
        }

        private void OnClientTick(float dt)
        {
            capi.Logger.Debug(LogUtil.ModMessage($"Client Tick {dt}"));
            cacheObserver.Tick(dt);
        }

        private void SendTraderUpdateToServer(SavedTrader trader)
        {
            capi.Logger.Debug($"Sending update for trader {trader.EntityId}");
            var packet = new TraderUpdatePacket { Trader = TraderSnapshot.FromSavedTrader(trader) };
            clientChannel.SendPacket(packet);
        }

        private void SendTraderUpdateToClient(IServerPlayer toPlayer, SavedTrader trader)
        {
            sapi.World.Logger.Debug(
                LogUtil.ModMessage(
                    $"Sending update for trader {trader.EntityId} to {toPlayer.PlayerName}"
                )
            );
            var packet = new TraderSyncPacket { Trader = TraderSnapshot.FromSavedTrader(trader) };
            if (!CanReceive(toPlayer))
                return;

            serverChannel.SendPacket(packet, toPlayer);
        }

        private void SendTraderUpdateToClient(SavedTrader trader)
        {
            var packet = new TraderSyncPacket { Trader = TraderSnapshot.FromSavedTrader(trader) };
            foreach (IServerPlayer player in sapi.World.AllOnlinePlayers)
            {
                if (!CanReceive(player))
                    continue;

                serverChannel.SendPacket(packet, player);
            }
        }

        public bool CanShare(IPlayer player)
        {
            string priv = "sharetradernotes";
            bool share = player.Role.Privileges.Contains(priv) || player.Privileges.Contains(priv);
            string verb = share ? "can" : "cannot";
            sapi.World.Logger.Debug(
                LogUtil.ModMessage($"Player {player.PlayerName} {verb} share notes")
            );
            return share;
        }

        public bool CanReceive(IPlayer player)
        {
            string priv = "receivetradernotes";
            bool receive =
                player.Role.Privileges.Contains(priv) || player.Privileges.Contains(priv);
            string verb = receive ? "can" : "cannot";
            sapi.World.Logger.Debug(
                LogUtil.ModMessage($"Player {player.PlayerName} {verb} receive notes")
            );
            return receive;
        }
    }

    [ProtoContract]
    public class TraderUpdatePacket
    {
        [ProtoMember(1)]
        public TraderSnapshot Trader;
    }

    [ProtoContract]
    public class TraderSyncPacket
    {
        [ProtoMember(1)]
        public TraderSnapshot Trader;
    }

    [ProtoContract]
    public class TraderBulkSyncPacket
    {
        [ProtoMember(1)]
        public List<TraderSnapshot> Traders;
    }

    public class CacheObserver
    {
        private readonly ICoreClientAPI capi;
        private Dictionary<long, double> knownTraders = new();
        public Action<SavedTrader> OnTraderUpdated;

        public CacheObserver(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        public void Tick(float dt)
        {
            capi.Logger.Debug(LogUtil.ModMessage("CacheObserver Tick"));
            foreach (var trader in TraderMapMod.Cache.Values.ToList())
            {
                if (
                    !knownTraders.TryGetValue(trader.EntityId, out var last)
                    || trader.LastUpdatedTotalDays > last
                )
                {
                    knownTraders[trader.EntityId] = trader.LastUpdatedTotalDays;
                    OnTraderUpdated?.Invoke(trader);
                }
            }
        }
    }

    public class ServerCacheStore
    {
        internal Dictionary<long, SavedTrader> Cache = new();
        private string savePath;

        public ServerCacheStore(ICoreServerAPI sapi)
        {
            string worldId = sapi.World.SavegameIdentifier;
            savePath = Path.Combine(
                sapi.DataBasePath,
                "ModData",
                $"tradernotestogether_cache.{worldId}.json"
            );
            LoadCache();
        }

        public bool TraderExists(long EntityId)
        {
            return Cache.ContainsKey(EntityId);
        }

        public void UpdateTrader(SavedTrader Trader)
        {
            Cache[Trader.EntityId] = Trader;
            SaveCache();
        }

        private void LoadCache()
        {
            if (string.IsNullOrEmpty(savePath) || !File.Exists(savePath))
                return;
            try
            {
                Cache =
                    JsonConvert.DeserializeObject<Dictionary<long, SavedTrader>>(
                        File.ReadAllText(savePath)
                    ) ?? new Dictionary<long, SavedTrader>();
            }
            catch { }
        }

        public void SaveCache()
        {
            if (string.IsNullOrEmpty(savePath))
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savePath));
                File.WriteAllText(
                    savePath,
                    JsonConvert.SerializeObject(Cache, Formatting.Indented)
                );
            }
            catch { }
        }

        internal SavedTrader GetTrader(long entityId)
        {
            if (TraderExists(entityId))
                return Cache[entityId];
            return null;
        }
    }

    [ProtoContract]
    public class TraderItemSnapshot
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
    public class TraderSnapshot
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
        public List<TraderItemSnapshot> Sells = new List<TraderItemSnapshot>();

        [ProtoMember(9)]
        public List<TraderItemSnapshot> Wants = new List<TraderItemSnapshot>();

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

        public static TraderSnapshot FromSavedTrader(SavedTrader trader)
        {
            var snapshot = new TraderSnapshot
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
                    new TraderItemSnapshot
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
                    new TraderItemSnapshot
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

    class LogUtil
    {
        internal static string ModMessage(string message)
        {
            return string.Concat("[tradernotestogether] ", message);
        }
    }
}
