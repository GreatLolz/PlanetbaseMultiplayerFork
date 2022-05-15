﻿using PlanetbaseMultiplayer.Model;
using PlanetbaseMultiplayer.Model.Packets.World;
using PlanetbaseMultiplayer.Model.Players;
using PlanetbaseMultiplayer.Model.World;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PlanetbaseMultiplayer.Server.World
{
    public class WorldStateManager : IManager
    {
        private WorldStateData worldStateData;
        private Server server;
        private bool dataRequestInProgress;
        public bool IsInitialized { get; private set; }

        public event EventHandler WorldDataRequestSent;
        public event EventHandler WorldDataUpdated;
        public event EventHandler WorldDataRequestFailed;

        public WorldStateManager(Server server, WorldStateData worldStateData)
        {
            this.server = server ?? throw new ArgumentNullException(nameof(server));
            this.worldStateData = worldStateData ?? throw new ArgumentNullException(nameof(worldStateData));
        }

        public bool Initialize()
        {
            server.SimulationManager.SimulationOwnerUpdated += OnSimulationOwnerUpdated;
            IsInitialized = true;
            return true;
        }

        public bool RequestWorldData()
        {
            Player? player = server.SimulationManager.GetSimulationOwner();
            if (player == null)
                return false; // Can't request world data, there are no simulation owners

            if (dataRequestInProgress)
                return true;

            WorldDataRequestSent?.Invoke(this, new System.EventArgs());
            dataRequestInProgress = true;
            WorldDataRequestPacket worldDataRequestPacket = new WorldDataRequestPacket();
            server.SendPacketToPlayer(worldDataRequestPacket, player.Value.Id);
            return true;
        }

        public void UpdateWorldData(WorldStateData worldStateData)
        {
            this.worldStateData = worldStateData;
            WorldDataUpdated?.Invoke(this, new System.EventArgs());
        }

        public WorldStateData GetWorldData()
        {
            return worldStateData;
        }
        
        public void OnWorldDataReceived(WorldStateData worldStateData)
        {
            Console.WriteLine("Received world data, updating...");
            dataRequestInProgress = false;
            UpdateWorldData(worldStateData);
        }

        private void OnSimulationOwnerUpdated(object sender, EventArgs.SimulationOwnerUpdatedEventArgs e)
        {
            if (!dataRequestInProgress)
                return;

            // Handle edge cases
            // A simulation owner may disconnect while a request is in progress
            // In this case we attempt to resend it
            dataRequestInProgress = false;
            if (!RequestWorldData())
            {
                // If there are no simulation owners present, we let event listeners know that the previous request failed.
                WorldDataRequestFailed?.Invoke(this, new System.EventArgs());
            }
        }
    }
}
