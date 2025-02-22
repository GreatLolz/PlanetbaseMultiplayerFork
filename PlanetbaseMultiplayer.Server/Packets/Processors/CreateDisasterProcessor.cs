﻿using PlanetbaseMultiplayer.Model.Packets;
using PlanetbaseMultiplayer.Model.Packets.Environment;
using PlanetbaseMultiplayer.Model.Packets.Processors.Abstract;
using PlanetbaseMultiplayer.Model.Players;
using PlanetbaseMultiplayer.Server.Environment;
using PlanetbaseMultiplayer.Server.Simulation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PlanetbaseMultiplayer.Server.Packets.Processors
{
    public class CreateDisasterProcessor : PacketProcessor
    {
        public override Type GetProcessedPacketType()
        {
            return typeof(CreateDisasterPacket);
        }

        public override void ProcessPacket(Guid sourcePlayerId, Packet packet, IProcessorContext context)
        {
            CreateDisasterPacket createDisasterPacket = (CreateDisasterPacket)packet;
            ServerProcessorContext processorContext = (ServerProcessorContext)context;
            SimulationManager simulationManager = processorContext.Server.SimulationManager;
            DisasterManager disasterManager = processorContext.Server.DisasterManager;

            Player? simulationOwner = simulationManager.GetSimulationOwner();
            if (simulationOwner == null || sourcePlayerId != simulationOwner.Value.Id)
            {
                //Deny request if client isn't the simulation owner
                return;
            }

            disasterManager.CreateDisaster(createDisasterPacket.Disaster);
        }
    }
}
