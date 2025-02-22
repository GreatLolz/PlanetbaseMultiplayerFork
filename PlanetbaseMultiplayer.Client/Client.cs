﻿using Lidgren.Network;
using Planetbase;
using PlanetbaseMultiplayer.Client.GameStates;
using PlanetbaseMultiplayer.Client.Players;
using PlanetbaseMultiplayer.Client.Simulation;
using PlanetbaseMultiplayer.Client.Timers;
using PlanetbaseMultiplayer.Client.Timers.Actions;
using PlanetbaseMultiplayer.Client.UI;
using PlanetbaseMultiplayer.Client.World;
using PlanetbaseMultiplayer.Model;
using PlanetbaseMultiplayer.Model.Packets;
using PlanetbaseMultiplayer.Model.Packets.Processors.Abstract;
using PlanetbaseMultiplayer.Model.Packets.Session;
using PlanetbaseMultiplayer.Model.Packets.World;
using PlanetbaseMultiplayer.Model.Players;
using PlanetbaseMultiplayer.Model.Session;
using PlanetbaseMultiplayer.Model.Utils;
using PlanetbaseMultiplayer.Model.World;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;

namespace PlanetbaseMultiplayer.Client
{
    public class Client
    {
        private GameStateMultiplayer gameStateMultiplayer;
        private ConnectionOptions connectionOptions;
        private NetClient client;
        private ConcurrentQueue<Packet> packetQueue;
        private Player? localPlayer;
        private PacketRouter router;
        private TimerActionManager timer;
        private ClientProcessorContext processorContext;

        private PlayerManager playerManager;
        private SimulationManager simulationManager;
        private Time.TimeManager timeManager;
        private WorldStateManager worldStateManager;
        private Environment.EnvironmentManager environmentManager;
        private Environment.DisasterManager disasterManager;
#if DEBUG
        private Debugging.DebugManager debugManager;
#endif

        public Player? LocalPlayer { get { return localPlayer; } set { localPlayer = value; } }
        public PlayerManager PlayerManager { get { return playerManager; } }
        public SimulationManager SimulationManager { get { return simulationManager; } }
        public Time.TimeManager TimeManager { get { return timeManager; } }
        public WorldStateManager WorldStateManager { get { return worldStateManager; } }
        public Environment.EnvironmentManager EnvironmentManager { get { return environmentManager; } }
        public Environment.DisasterManager DisasterManager { get { return disasterManager; } }
#if DEBUG
        public Debugging.DebugManager DebugManager { get { return debugManager; } }
#endif

        public Client(GameStateMultiplayer gameStateMultiplayer)
        {
            this.gameStateMultiplayer = gameStateMultiplayer;
            packetQueue = new ConcurrentQueue<Packet>();
            processorContext = new ClientProcessorContext(this);

            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            NetPeerConfiguration config = new NetPeerConfiguration("PlanetbaseMultiplayer");
            config.EnableMessageType(NetIncomingMessageType.Data);
            config.EnableMessageType(NetIncomingMessageType.DiscoveryResponse);
            client = new NetClient(config);
            client.RegisterReceivedCallback(new SendOrPostCallback(MessageReceived));

            InitializeProcessors();
            InitializeActions();
            InitializeManagers();
        }

        private void InitializeProcessors()
        {
            router = new PacketRouter(processorContext);
            foreach (PacketProcessor packetProcessor in PacketProcessor.GetProcessors())
                router.RegisterPacketProcessor(packetProcessor);
        }

        private void InitializeActions()
        {
            timer = new TimerActionManager(processorContext);
            timer.RegisterAction(new ProcessPacketsAction(), 1);
            timer.RegisterAction(new SyncEnvironmentDataAction(), 30);
            timer.RegisterAction(new UpdateDisasterAction(), 10);
        }

        private void InitializeManagers()
        {
            playerManager = new PlayerManager(this);
            simulationManager = new SimulationManager(this);
            timeManager = new Time.TimeManager(this);
            worldStateManager = new WorldStateManager(this);
            environmentManager = new Environment.EnvironmentManager(this);
            disasterManager = new Environment.DisasterManager(this);
#if DEBUG
            debugManager = new Debugging.DebugManager(this);
#endif

            playerManager.Initialize();
            simulationManager.Initialize();
            timeManager.Initialize();
            worldStateManager.Initialize();
            environmentManager.Initialize();
            disasterManager.Initialize();
#if DEBUG
            debugManager.Initialize();
#endif
        }

        public bool Connect(ConnectionOptions connectionOptions)
        {
            this.connectionOptions = connectionOptions;
            if (client.Status != NetPeerStatus.Running)
                client.Start();

            client.Connect(connectionOptions.Host, connectionOptions.Port);
            return true;
        }

        public void RequestDisconnect(DisconnectReason reason = DisconnectReason.DisconnectRequest)
        {
            DisconnectRequestPacket disconnectRequestPacket = new DisconnectRequestPacket(reason);
            SendPacket(disconnectRequestPacket);
        }

        public void Disconnect()
        {
            if (client.ConnectionStatus == NetConnectionStatus.Connected)
                client.Disconnect("Disconnected");

            if (client.Status == NetPeerStatus.Running)
                client.Shutdown("Disconnected");

            gameStateMultiplayer.OnClientDisconnected();
        }

        // Handle incoming messages
        // Data packets are enqueued for later processing
        public void MessageReceived(object obj)
        {
            NetPeer peer = obj as NetPeer;
            NetIncomingMessage msg = peer.ReadMessage();
            switch (msg.MessageType)
            {
                case NetIncomingMessageType.StatusChanged:
                    OnIncomingStatusChange(peer, msg);
                    break;
                case NetIncomingMessageType.DiscoveryResponse:
                    OnIncomingDiscoveryResponse(peer, msg);
                    break;
                case NetIncomingMessageType.Data:
                    OnIncomingData(peer, msg);
                    break;
                case NetIncomingMessageType.DebugMessage:
                    OnIncomingDebugMessage(peer, msg);
                    break;
                default:
                    Debug.Log($"Unhandled packet received: {msg.MessageType}, Data: {Encoding.UTF8.GetString(msg.ReadBytes(msg.LengthBytes))}");
                    break;
            }
        }

        private void OnIncomingDiscoveryResponse(NetPeer peer, NetIncomingMessage msg)
        {
            string serverType = msg.ReadString();
            if (serverType != "PlanetbaseMultiplayer.Server")
            {
                Debug.Log("Received response was not from a valid server type");
                return;
            }
            Debug.Log($"Found server of type {serverType} at {msg.SenderEndPoint}! Connecting!");
            client.Connect(msg.SenderEndPoint);
        }

        private void OnIncomingStatusChange(NetPeer peer, NetIncomingMessage msg)
        {
            if (client.ConnectionStatus == NetConnectionStatus.Connected)
            {
                Debug.Log("Client connected!");
                SessionDataRequestPacket sessionDataRequestPacket = new SessionDataRequestPacket();
                SendPacket(sessionDataRequestPacket);
                Debug.Log("Sending session data request");
            }
            if (client.ConnectionStatus == NetConnectionStatus.Disconnected && GameManager.getInstance().getGameState() is GameStateGame)
            {
                Debug.Log("Lost connection with the game server!");
                void OnExitConfirm(object parameter)
                {
                    GameManager.getInstance().setGameStateTitle();
                    Disconnect();
                }

                GuiDefinitions.Callback callback = new GuiDefinitions.Callback(OnExitConfirm);
                if (!MessageBoxOk.Show(callback, "Disconnected from server", "Lost connection with the game server."))
                    OnExitConfirm(null); // Failed to show message box
            }
        }

        private void OnIncomingData(NetPeer peer, NetIncomingMessage msg)
        {
            try
            {
                Packet packet = Packet.Deserialize(msg.ReadBytes(msg.LengthBytes));
                packetQueue.Enqueue(packet);
            }
            catch (Exception e)
            {
                Debug.Log($"Error while receiving packet: {e.Message}");
            }
        }

        private void OnIncomingDebugMessage(NetPeer peer, NetIncomingMessage msg)
        {
            Debug.Log($"DEBUG: {msg.ReadString()}");
        }

        public void OnSessionDataReceived(SessionData session)
        {
            Debug.Log("Received session data: " + session.ServerName + ", PasswordProtected: "
                + session.PasswordProtected + ", Player count: " + session.PlayerCount);

            AuthenticateRequestPacket authenticateRequestPacket;
            if (session.PasswordProtected)
            {
                authenticateRequestPacket = new AuthenticateRequestPacket(connectionOptions.Username, connectionOptions.Password);
            }
            else
            {
                authenticateRequestPacket = new AuthenticateRequestPacket(connectionOptions.Username, null);
            }

            Debug.Log("Sending authenticate request");
            SendPacket(authenticateRequestPacket);
        }

        public void OnFixedUpdate()
        {
            // Update timers
            timer.OnTick();
        }

        public void ProcessPackets()
        {
            Packet packet;
            while(packetQueue.TryDequeue(out packet))
            {
                if(!router.ProcessPacket(Guid.Empty, packet))
                {
                    Debug.Log("Unhandled packet received: " + packet.GetType().FullName);
                }
            }
        }

        public void SendPacket(Packet packet, ChannelType channelType = ChannelType.ReliableOrdered)
        {
            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write(packet.Serialize());
#if DEBUG
            Debug.Log($"Send packet: {packet.GetType().FullName}");
#endif
            NetDeliveryMethod deliveryMethod = ChannelTypeUtils.ChannelTypeToLidgren(channelType);
            client.SendMessage(msg, deliveryMethod);
        }
    }
}
