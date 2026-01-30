using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
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
            api.World.Logger.Notification("Trader Notes Together Mod Systems Started!");
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            serverChannel = api
                .Network.RegisterChannel("tradernotestogether")
                .RegisterMessageType<TraderUpdatePacket>()
                .RegisterMessageType<TraderSyncPacket>()
                .RegisterMessageType<TraderBulkSyncPacket>()
                .SetMessageHandler<TraderUpdatePacket>(OnTraderUpdateFromClient);

            api.Event.PlayerJoin += OnPlayerJoin;
            cacheStore = new ServerCacheStore(api);
        }

        private void OnPlayerJoin(IServerPlayer player)
        {
            if (!CanReceive(player))
                return;
            serverChannel.SendPacket(
                new TraderBulkSyncPacket { Traders = cacheStore.Cache.Values.ToList() },
                player
            );
        }

        private void OnTraderUpdateFromClient(IServerPlayer fromPlayer, TraderUpdatePacket packet)
        {
            if (!CanShare(fromPlayer))
                return;
            long EntityId = packet.Trader.EntityId;
            if (
                cacheStore.TraderExists(EntityId)
                && cacheStore.GetTrader(EntityId).LastUpdatedTotalDays
                    >= packet.Trader.LastUpdatedTotalDays
            )
                return;

            cacheStore.UpdateTrader(packet.Trader);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            if (capi.IsSinglePlayer)
            {
                capi.Logger.Debug(
                    "[TraderNotesTogether] Single-player detected, networking disabled."
                );
                return;
            }
            clientChannel = api
                .Network.RegisterChannel("tradernotestogether")
                .RegisterMessageType<TraderUpdatePacket>()
                .RegisterMessageType<TraderSyncPacket>()
                .RegisterMessageType<TraderBulkSyncPacket>()
                .SetMessageHandler<TraderSyncPacket>(OnTraderUpdateFromServer)
                .SetMessageHandler<TraderBulkSyncPacket>(OnTraderUpdateFromServer);

            cacheObserver = new CacheObserver();
            cacheObserver.OnTraderUpdated += trader =>
            {
                SendTraderUpdateToServer(trader);
            };

            api.Event.RegisterGameTickListener(OnClientTick, 1000);
        }

        private void OnTraderUpdateFromServer(TraderSyncPacket packet)
        {
            TraderMapMod.Cache[packet.Trader.EntityId] = packet.Trader;
        }

        private void OnTraderUpdateFromServer(TraderBulkSyncPacket packet)
        {
            foreach (SavedTrader trader in packet.Traders)
            {
                if (
                    !TraderMapMod.Cache.TryGetValue(trader.EntityId, out var local)
                    || trader.LastUpdatedTotalDays > local.LastUpdatedTotalDays
                )
                    TraderMapMod.Cache[trader.EntityId] = trader;
            }
        }

        private void OnClientTick(float dt)
        {
            cacheObserver.Tick();
        }

        private void SendTraderUpdateToServer(SavedTrader trader)
        {
            var packet = new TraderUpdatePacket { Trader = trader };
            clientChannel.SendPacket(packet);
        }

        private void SendTraderUpdateToClient(IServerPlayer toPlayer, SavedTrader trader)
        {
            var packet = new TraderSyncPacket { Trader = trader };
            if (!CanReceive(toPlayer))
                return;

            serverChannel.SendPacket(packet, toPlayer);
        }

        private void SendTraderUpdateToClient(SavedTrader trader)
        {
            var packet = new TraderSyncPacket { Trader = trader };
            foreach (IServerPlayer player in sapi.World.AllOnlinePlayers)
            {
                if (!CanReceive(player))
                    continue;

                serverChannel.SendPacket(packet, player);
            }
        }

        public bool CanShare(IPlayer player)
        {
            return player.Role.Privileges.Contains("sendtradernotes");
        }

        public bool CanReceive(IPlayer player)
        {
            return player.Role.Privileges.Contains("recievetradernotes");
        }
    }

    public class TraderUpdatePacket
    {
        public SavedTrader Trader;
    }

    public class TraderSyncPacket
    {
        public SavedTrader Trader;
    }

    public class TraderBulkSyncPacket
    {
        public List<SavedTrader> Traders;
    }

    public class CacheObserver
    {
        private Dictionary<long, double> knownTraders = new();

        public Action<SavedTrader> OnTraderUpdated;

        public void Tick()
        {
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
}
