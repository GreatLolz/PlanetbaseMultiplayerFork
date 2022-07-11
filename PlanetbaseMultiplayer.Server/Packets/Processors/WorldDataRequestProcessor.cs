﻿using PlanetbaseMultiplayer.Model.Packets;
using PlanetbaseMultiplayer.Model.Packets.Processors.Abstract;
using PlanetbaseMultiplayer.Model.Packets.World;
using PlanetbaseMultiplayer.Model.Players;
using PlanetbaseMultiplayer.Model.World;
using PlanetbaseMultiplayer.Server.Players;
using PlanetbaseMultiplayer.Server.Simulation;
using PlanetbaseMultiplayer.Server.Time;
using PlanetbaseMultiplayer.Server.World;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PlanetbaseMultiplayer.Server.Packets.Processors
{
    public class WorldDataRequestProcessor : PacketProcessor
    {
        public override Type GetProcessedPacketType()
        {
            return typeof(WorldDataRequestPacket);
        }

        public override void ProcessPacket(Guid sourcePlayerId, Packet packet, IProcessorContext context)
        {
            WorldDataRequestPacket worldDataRequestPacket = (WorldDataRequestPacket)packet;
            ServerProcessorContext processorContext = (ServerProcessorContext)context;
            PlayerManager playerManager = processorContext.ServiceLocator.LocateService<PlayerManager>();
            SimulationManager simulationManager = processorContext.ServiceLocator.LocateService<SimulationManager>();
            TimeManager timeManager = processorContext.ServiceLocator.LocateService<TimeManager>();
            WorldRequestQueueManager worldRequestQueueManager = processorContext.ServiceLocator.LocateService<WorldRequestQueueManager>();
            WorldStateManager worldStateManager = processorContext.ServiceLocator.LocateService<WorldStateManager>();

            if (!playerManager.PlayerExists(sourcePlayerId))
            {
                // Players can not ask for a copy of the world before they authenticate
                return;
            }

            Player sourcePlayer = playerManager.GetPlayer(sourcePlayerId);
            if (sourcePlayer.State != PlayerState.ConnectedMainMenu)
            {
                // Invalid state
                return;
            }

            // We pause the game and lock time management until everyone has finished loading
            timeManager.FreezeTime();

            Player? simulationOwner = simulationManager.GetSimulationOwner();
            if (simulationOwner != null && simulationOwner.Value != sourcePlayer && worldStateManager.RequestWorldData())
            {
                // The server can get a newer world state
                // add the client to the queue and wait
                worldRequestQueueManager.EnqueuePlayer(sourcePlayer);
            }
            else
            {
                // The state we have is already the newest
                WorldStateData worldStateData = worldStateManager.GetWorldData();
                WorldDataPacket worldDataPacket = new WorldDataPacket(worldStateData);
                processorContext.Server.SendPacketToPlayer(worldDataPacket, sourcePlayerId);
            }
        }
    }
}
