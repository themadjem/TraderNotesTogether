using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TraderMapTooltip;
using TraderNotesTogether.NetworkPackets;
using TraderNotesTogether.ProtoTrader;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace TraderNotesTogether
{
    class TraderNotesTogetherMod : ModSystem
    {
        private ICoreClientAPI capi;
        private ICoreServerAPI sapi;

        private IClientNetworkChannel clientChannel;
        private IServerNetworkChannel serverChannel;

        private CacheObserver cacheObserver;
        private ServerCacheStore cacheStore;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.Logger.Notification(Util.ModMessage("ModSystem started!"));
        }

        #region ClientSide
        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            capi.Logger.Notification(Util.ModMessage("Starting Client Logic..."));
            clientChannel = capi
                .Network.RegisterChannel(Util.Modid)
                .RegisterMessageType<TraderUpdatePacket>()
                .RegisterMessageType<TraderSyncPacket>()
                .RegisterMessageType<TraderBulkSyncPacket>()
                .SetMessageHandler<TraderSyncPacket>(OnTraderSyncFromServer)
                .SetMessageHandler<TraderBulkSyncPacket>(OnTraderBulkSyncFromServer);

            cacheObserver = new CacheObserver(capi);
            capi.Event.RegisterGameTickListener(OnClientTick, 5000);
        }

        /// Called when the client receives an sync packet from the server
        private void OnTraderSyncFromServer(TraderSyncPacket packet)
        {
            capi.World.Logger.Debug(
                Util.ModMessage($"Recieved update for trader {packet.Trader.EntityId}")
            );
            TraderMapMod.Cache[packet.Trader.EntityId] = packet.Trader.ToSavedTrader();
        }

        /// Called when the client receives a bulk sync packet from the server (usually on initially joining a world)
        private void OnTraderBulkSyncFromServer(TraderBulkSyncPacket packet)
        {
            capi.World.Logger.Debug(
                Util.ModMessage(
                    $"Received bulk update for traders {string.Join(",", packet.Traders.Select(trader => trader.EntityId))}"
                )
            );
            foreach (ProtoTraderEntity trader in packet.Traders)
            {
                if (
                    !TraderMapMod.Cache.TryGetValue(trader.EntityId, out var local)
                    || trader.LastUpdatedTotalDays > local.LastUpdatedTotalDays
                )
                    TraderMapMod.Cache[trader.EntityId] = trader.ToSavedTrader();
            }
        }

        /// Called when the client processes a scheduled tick
        private void OnClientTick(float dt)
        {
            capi.Logger.Debug(Util.ModMessage($"Client Tick {dt}"));
            cacheObserver.Tick(dt);
        }

        private void SendTraderUpdateToServer(SavedTrader trader)
        {
            capi.Logger.Debug($"Sending update for trader {trader.EntityId}");
            var packet = new TraderUpdatePacket
            {
                Trader = ProtoTraderEntity.FromSavedTrader(trader),
            };
            capi.Network.GetChannel(Util.Modid).SendPacket(packet);
        }

        #endregion
        #region ServerSide
        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            sapi.Logger.Debug(Util.ModMessage("Starting Server Logic..."));
            serverChannel = sapi
                .Network.RegisterChannel(Util.Modid)
                .RegisterMessageType<TraderUpdatePacket>()
                .RegisterMessageType<TraderSyncPacket>()
                .RegisterMessageType<TraderBulkSyncPacket>()
                .SetMessageHandler<TraderUpdatePacket>(OnTraderUpdateFromClient);

            sapi.Event.PlayerJoin += OnPlayerJoin;
            cacheStore = new ServerCacheStore(sapi);
            sapi.Logger.Debug(Util.ModMessage("Server side startup complete"));
        }

        /// Called when a player joins a world
        private void OnPlayerJoin(IServerPlayer player)
        {
            sapi.World.Logger.Debug(Util.ModMessage($"Player {player.PlayerName} joined"));
            if (!CanReceive(player))
            {
                sapi.World.Logger.Debug(
                    Util.ModMessage($"Player, {player.PlayerName}, cannot receive updates")
                );
                return;
            }

            //       SendBulkUpdateToPlayer(player);
        }

        /// Called when the server receives an update packet from a client
        private void OnTraderUpdateFromClient(IServerPlayer fromPlayer, TraderUpdatePacket packet)
        {
            sapi.World.Logger.Debug(
                Util.ModMessage(
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

        private void SendTraderUpdateToClient(IServerPlayer toPlayer, SavedTrader trader)
        {
            sapi.World.Logger.Debug(
                Util.ModMessage(
                    $"Sending update for trader {trader.EntityId} to {toPlayer.PlayerName}"
                )
            );
            var packet = new TraderSyncPacket
            {
                Trader = ProtoTraderEntity.FromSavedTrader(trader),
            };
            if (!CanReceive(toPlayer))
                return;

            sapi.Network.GetChannel(Util.Modid).SendPacket(packet, toPlayer);
        }

        private void SendTraderUpdateToClient(SavedTrader trader)
        {
            var packet = new TraderSyncPacket
            {
                Trader = ProtoTraderEntity.FromSavedTrader(trader),
            };
            foreach (IServerPlayer player in sapi.World.AllOnlinePlayers)
            {
                if (!CanReceive(player))
                    continue;

                sapi.Network.GetChannel(Util.Modid).SendPacket(packet, player);
            }
        }

        public void SendBulkUpdateToPlayer(IServerPlayer player)
        {
            var snapshots = cacheStore
                .Cache.Values.Select(ProtoTraderEntity.FromSavedTrader)
                .ToList();
            if (snapshots.Count > 0)
            {
                sapi.World.Logger.Debug(Util.ModMessage("Sending player bulk update"));
                sapi.Network.GetChannel(Util.Modid)
                    .SendPacket(new TraderBulkSyncPacket { Traders = snapshots }, player);
            }
        }

        public bool CanShare(IPlayer player)
        {
            string priv = "sharetradernotes";
            bool share = player.Role.Privileges.Contains(priv) || player.Privileges.Contains(priv);
            string verb = share ? "can" : "cannot";
            sapi.World.Logger.Debug(
                Util.ModMessage($"Player {player.PlayerName} {verb} share notes")
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
                Util.ModMessage($"Player {player.PlayerName} {verb} receive notes")
            );
            return receive;
        }
        #endregion
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
            capi.Logger.Debug(Util.ModMessage("CacheObserver Tick"));
            var cache = TraderMapMod.Cache;
            if (cache == null)
            {
                capi.Logger.Error(Util.ModMessage("Null cache from TraderMapMod"));
                return;
            }
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

    class Util
    {
        internal const string Modid = "tradernotestogether";

        internal static string ModMessage(string message)
        {
            return string.Concat($"[{Modid}] ", message);
        }
    }
}
