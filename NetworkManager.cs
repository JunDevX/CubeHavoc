using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using LiteNetLib;
using LiteNetLib.Utils;
using Raylib_cs;

namespace CubeHavoc
{
    /// <summary>
    /// Отвечает за локальный (LAN) мультиплеер: хостинг, подключение, обнаружение серверов
    /// в списке серверов и синхронизацию состояния мира между хостом и клиентами.
    /// Хост всегда является источником истины (authoritative) для физики, зомби и предметов.
    /// Клиенты считают только своё движение локально, а действия (атака/подбор/бросок)
    /// отправляют хосту, который их обрабатывает и рассылает всем итоговое состояние.
    /// </summary>
    public class NetworkManager : INetEventListener
    {
        public enum NetMode { None, Host, Client }

        public const int GamePort = 7777;
        private const string ConnectionKey = "CubeHavocLAN_v2";
        private const string DiscoveryRequestMagic = "CUBEHAVOC_DISCOVER_REQ";
        private const string DiscoveryResponseMagic = "CUBEHAVOC_DISCOVER_RES";

        // Типы пакетов
        private const byte PT_Join = 1;
        private const byte PT_Welcome = 2;
        private const byte PT_InputState = 3;
        private const byte PT_Action = 4;
        private const byte PT_PlayerState = 5;
        private const byte PT_WorldObjects = 6;
        private const byte PT_Zombies = 7;
        private const byte PT_ChatSend = 8;
        private const byte PT_ChatBroadcast = 9;
        private const byte PT_PlayerLeft = 10;

        public const byte ActAttack = 1;
        public const byte ActThrowQ = 2;
        public const byte ActThrowE = 3;

        public struct DiscoveredServer
        {
            public string Name;
            public string Ip;
            public int Port;
            public int CurrentPlayers;
            public int MaxPlayers;
        }

        public class RemoteInputState
        {
            public Vector3 Position;
            public Vector3 Forward;
            public bool MouseDown;
        }

        public struct PendingAction
        {
            public int PlayerId;
            public byte ActionType;
            public byte WeaponIndex;
            public Vector3 Position;
            public Vector3 Forward;
        }

        public NetMode Mode { get; private set; } = NetMode.None;
        public bool IsConnected { get; private set; } = false;
        public bool IsScanning { get; private set; } = false;
        public List<DiscoveredServer> DiscoveredServers { get; } = new List<DiscoveredServer>();
        public Dictionary<int, RemoteInputState> RemoteInputs { get; } = new Dictionary<int, RemoteInputState>();

        private NetManager netManager;
        private NetPeer hostPeer; // На клиенте — единственный пир (хост)
        private string serverName = "Сервер";
        private int maxPlayers = 8;
        private static readonly Random rnd = new Random();

        private List<Player3D> players;
        private List<PhysicalObject3D> worldObjects;
        private List<Zombie3D> zombies;
        private List<ChatMessage> chatMessages;
        private Player3D localPlayer;

        private readonly Dictionary<int, PhysicalObject3D> objectsByNetId = new Dictionary<int, PhysicalObject3D>();
        private readonly Dictionary<int, Zombie3D> zombiesByNetId = new Dictionary<int, Zombie3D>();
        private readonly List<PendingAction> pendingActions = new List<PendingAction>();

        private float tickTimer = 0f;
        private const float TickInterval = 0.05f; // 20 Гц

        private float scanTimer = 0f;
        private const float ScanBroadcastInterval = 0.5f;
        private const float ScanDuration = 3.0f;
        private float scanElapsed = 0f;

        private Vector3 localInputPos;
        private Vector3 localInputFwd = new Vector3(0, 0, 1);
        private bool localInputMouseDown;

        internal void Configure(List<Player3D> playersRef, List<PhysicalObject3D> worldObjectsRef, List<Zombie3D> zombiesRef, List<ChatMessage> chatMessagesRef, Player3D localPlayerRef)
        {
            players = playersRef;
            worldObjects = worldObjectsRef;
            zombies = zombiesRef;
            chatMessages = chatMessagesRef;
            localPlayer = localPlayerRef;
        }

        // ---------------------------------------------------------------
        // Управление режимами
        // ---------------------------------------------------------------

        public bool StartHost(string name, int maxPlayersCount)
        {
            Stop();
            serverName = string.IsNullOrWhiteSpace(name) ? "Сервер" : name;
            maxPlayers = maxPlayersCount;

            try
            {
                netManager = new NetManager(this)
                {
                    BroadcastReceiveEnabled = true,
                    UnconnectedMessagesEnabled = true
                };
                if (!netManager.Start(GamePort))
                {
                    netManager = null;
                    Mode = NetMode.None;
                    return false;
                }
            }
            catch
            {
                netManager = null;
                Mode = NetMode.None;
                return false;
            }

            Mode = NetMode.Host;
            IsConnected = true;

            objectsByNetId.Clear();
            zombiesByNetId.Clear();
            RemoteInputs.Clear();
            pendingActions.Clear();
            tickTimer = 0f;
            return true;
        }

        public bool StartClient(string ip, int port)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            if (port < 1 || port > 65535) return false;

            Stop();
            try
            {
                netManager = new NetManager(this) { UnconnectedMessagesEnabled = true };
                if (!netManager.Start()) { netManager = null; return false; }
                hostPeer = netManager.Connect(ip, port, ConnectionKey);
                if (hostPeer == null) { Stop(); return false; }
            }
            catch
            {
                Stop();
                return false;
            }

            Mode = NetMode.Client;
            IsConnected = false;

            objectsByNetId.Clear();
            zombiesByNetId.Clear();
            tickTimer = 0f;
            return true;
        }

        public void StartScan()
        {
            if (Mode != NetMode.None) return;

            if (netManager == null)
            {
                netManager = new NetManager(this)
                {
                    BroadcastReceiveEnabled = true,
                    UnconnectedMessagesEnabled = true
                };
                netManager.Start();
            }

            DiscoveredServers.Clear();
            IsScanning = true;
            scanElapsed = 0f;
            scanTimer = ScanBroadcastInterval; // отправляем первый запрос сразу в этом же тике
        }

        public void Stop()
        {
            if (netManager != null)
            {
                netManager.DisconnectAll();
                netManager.Stop();
                netManager = null;
            }
            hostPeer = null;
            Mode = NetMode.None;
            IsConnected = false;
            IsScanning = false;
            RemoteInputs.Clear();
            pendingActions.Clear();
            objectsByNetId.Clear();
            zombiesByNetId.Clear();
        }

        // ---------------------------------------------------------------
        // Основной тик — вызывать каждый кадр из главного цикла игры
        // ---------------------------------------------------------------

        public void PollEvents(float dt)
        {
            if (netManager == null) return;

            netManager.PollEvents();

            if (IsScanning)
            {
                scanElapsed += dt;
                scanTimer += dt;
                if (scanTimer >= ScanBroadcastInterval)
                {
                    scanTimer = 0f;
                    SendDiscoveryRequest();
                }
                if (scanElapsed >= ScanDuration)
                {
                    IsScanning = false;
                }
            }

            if (Mode == NetMode.Host)
            {
                foreach (var pair in RemoteInputs)
                {
                    var remotePlayer = players?.Find(p => p.Id == pair.Key);
                    if (remotePlayer != null)
                        remotePlayer.Position = pair.Value.Position;
                }

                tickTimer += dt;
                if (tickTimer >= TickInterval)
                {
                    tickTimer = 0f;
                    BroadcastWorldState();
                }
            }
            else if (Mode == NetMode.Client && IsConnected)
            {
                tickTimer += dt;
                if (tickTimer >= TickInterval)
                {
                    tickTimer = 0f;
                    SendInputState();
                }
            }
        }

        private void SendDiscoveryRequest()
        {
            var writer = new NetDataWriter();
            writer.Put(DiscoveryRequestMagic);
            netManager.SendBroadcast(writer, GamePort);
        }

        // ---------------------------------------------------------------
        // Отправка данных клиент -> хост
        // ---------------------------------------------------------------

        public void SetLocalInputState(Vector3 pos, Vector3 fwd, bool mouseDown)
        {
            localInputPos = pos;
            localInputFwd = fwd;
            localInputMouseDown = mouseDown;
        }

        private void SendInputState()
        {
            if (hostPeer == null) return;
            var writer = new NetDataWriter();
            writer.Put(PT_InputState);
            PutVec3(writer, localInputPos);
            PutVec3(writer, localInputFwd);
            writer.Put(localInputMouseDown);
            hostPeer.Send(writer, DeliveryMethod.Sequenced);
        }

        public void SendAction(byte actionType, Vector3 pos, Vector3 fwd, byte weaponIndex = 0)
        {
            if (Mode != NetMode.Client || hostPeer == null) return;
            var writer = new NetDataWriter();
            writer.Put(PT_Action);
            writer.Put(actionType);
            writer.Put(weaponIndex);
            PutVec3(writer, pos);
            PutVec3(writer, fwd);
            hostPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendChat(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            if (Mode == NetMode.Client)
            {
                if (hostPeer == null) return;
                var writer = new NetDataWriter();
                writer.Put(PT_ChatSend);
                writer.Put(text);
                hostPeer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                // Хост или одиночная игра — добавляем сразу и (если хост) рассылаем
                AppendChat(localPlayer.Name, text, localPlayer.PlayerColor);
                if (Mode == NetMode.Host)
                {
                    BroadcastChat(localPlayer.Name, text, localPlayer.PlayerColor);
                }
            }
        }

        private void AppendChat(string sender, string text, Color color)
        {
            chatMessages.Add(new ChatMessage
            {
                Sender = sender,
                Text = text,
                SenderColor = color,
                TimeRemaining = 12.0f
            });
            if (chatMessages.Count > 8) chatMessages.RemoveAt(0);
        }

        private void BroadcastChat(string sender, string text, Color color)
        {
            var writer = new NetDataWriter();
            writer.Put(PT_ChatBroadcast);
            writer.Put(sender);
            writer.Put(text);
            writer.Put(color.R);
            writer.Put(color.G);
            writer.Put(color.B);
            netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
        }

        public List<PendingAction> DrainPendingActions()
        {
            var copy = new List<PendingAction>(pendingActions);
            pendingActions.Clear();
            return copy;
        }

        // ---------------------------------------------------------------
        // Рассылка состояния мира хост -> клиенты
        // ---------------------------------------------------------------

        private void BroadcastWorldState()
        {
            if (netManager == null || players == null || worldObjects == null || zombies == null) return;
            // Игроки
            var wp = new NetDataWriter();
            wp.Put(PT_PlayerState);
            wp.Put(players.Count);
            foreach (var p in players)
            {
                wp.Put(p.Id);
                PutVec3(wp, p.Position);
                wp.Put(p.PlayerColor.R);
                wp.Put(p.PlayerColor.G);
                wp.Put(p.PlayerColor.B);
                wp.Put(p.Name);
                wp.Put(p.Health);
                wp.Put(p.Kills);
                wp.Put(p.Deaths);
            }
            netManager.SendToAll(wp, DeliveryMethod.ReliableOrdered);

            // Объекты мира (блоки, гравипушки)
            var wo = new NetDataWriter();
            wo.Put(PT_WorldObjects);
            wo.Put(worldObjects.Count);
            foreach (var o in worldObjects)
            {
                wo.Put(o.NetId);
                wo.Put((byte)o.Type);
                PutVec3(wo, o.Position);
                PutVec3(wo, o.Size);
                wo.Put(o.Color.R);
                wo.Put(o.Color.G);
                wo.Put(o.Color.B);
                wo.Put(o.IsHeld);
                wo.Put(o.HeldByPlayerId);
            }
            netManager.SendToAll(wo, DeliveryMethod.ReliableOrdered);

            // Зомби
            var wz = new NetDataWriter();
            wz.Put(PT_Zombies);
            wz.Put(zombies.Count);
            foreach (var z in zombies)
            {
                wz.Put(z.NetId);
                PutVec3(wz, z.Position);
                wz.Put(z.Yaw);
                wz.Put(z.Health);
            }
            netManager.SendToAll(wz, DeliveryMethod.ReliableOrdered);
        }

        // ---------------------------------------------------------------
        // INetEventListener
        // ---------------------------------------------------------------

        public void OnPeerConnected(NetPeer peer)
        {
            if (Mode == NetMode.Host)
            {
                int id = peer.Id + 100;
                var newPlayer = new Player3D
                {
                    Id = id,
                    Name = "Игрок" + id,
                    Position = new Vector3(0, 0, 0),
                    PlayerColor = new Color((byte)rnd.Next(60, 255), (byte)rnd.Next(60, 255), (byte)rnd.Next(60, 255), (byte)255),
                    Health = 10,
                    MaxHealth = 10
                };
                players.Add(newPlayer);
                RemoteInputs[id] = new RemoteInputState { Position = newPlayer.Position, Forward = new Vector3(0, 0, 1), MouseDown = false };

                var writer = new NetDataWriter();
                writer.Put(PT_Welcome);
                writer.Put(id);
                peer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
            else if (Mode == NetMode.Client)
            {
                IsConnected = true;
                var writer = new NetDataWriter();
                writer.Put(PT_Join);
                writer.Put(localPlayer.Name);
                writer.Put(localPlayer.PlayerColor.R);
                writer.Put(localPlayer.PlayerColor.G);
                writer.Put(localPlayer.PlayerColor.B);
                peer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (Mode == NetMode.Host)
            {
                int id = peer.Id + 100;
                var existing = players.Find(pl => pl.Id == id);
                string name = existing != null ? existing.Name : "Игрок";
                players.RemoveAll(pl => pl.Id == id);
                RemoteInputs.Remove(id);

                var writer = new NetDataWriter();
                writer.Put(PT_PlayerLeft);
                writer.Put(id);
                netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);

                AppendChat("Сервер", $"{name} покинул игру", Color.Gray);
                BroadcastChat("Сервер", $"{name} покинул игру", Color.Gray);
            }
            else if (Mode == NetMode.Client)
            {
                IsConnected = false;
            }
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            if (Mode == NetMode.Client) IsConnected = false;
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            try
            {
                byte type = reader.GetByte();
                switch (type)
                {
                    case PT_Join:
                    {
                        if (Mode != NetMode.Host) break;
                        string name = reader.GetString();
                        byte r = reader.GetByte();
                        byte g = reader.GetByte();
                        byte b = reader.GetByte();
                        int id = peer.Id + 100;
                        var p = players.Find(pl => pl.Id == id);
                        if (p != null)
                        {
                            p.Name = string.IsNullOrWhiteSpace(name) ? p.Name : name;
                            p.PlayerColor = new Color(r, g, b, (byte)255);
                            AppendChat("Сервер", $"{p.Name} подключился", Color.Gray);
                            BroadcastChat("Сервер", $"{p.Name} подключился", Color.Gray);
                        }
                        break;
                    }
                    case PT_Welcome:
                    {
                        if (Mode != NetMode.Client) break;
                        int id = reader.GetInt();
                        localPlayer.Id = id;
                        break;
                    }
                    case PT_InputState:
                    {
                        if (Mode != NetMode.Host) break;
                        int id = peer.Id + 100;
                        Vector3 pos = GetVec3(reader);
                        Vector3 fwd = GetVec3(reader);
                        bool mouseDown = reader.GetBool();
                        if (!RemoteInputs.TryGetValue(id, out var state))
                        {
                            state = new RemoteInputState();
                            RemoteInputs[id] = state;
                        }
                        state.Position = pos;
                        state.Forward = fwd;
                        state.MouseDown = mouseDown;
                        break;
                    }
                    case PT_Action:
                    {
                        if (Mode != NetMode.Host) break;
                        int id = peer.Id + 100;
                        byte actionType = reader.GetByte();
                        byte weaponIndex = reader.GetByte();
                        Vector3 pos = GetVec3(reader);
                        Vector3 fwd = GetVec3(reader);
                        pendingActions.Add(new PendingAction { PlayerId = id, ActionType = actionType, WeaponIndex = weaponIndex, Position = pos, Forward = fwd });
                        break;
                    }
                    case PT_PlayerState:
                    {
                        if (Mode != NetMode.Client) break;
                        int count = reader.GetInt();
                        for (int i = 0; i < count; i++)
                        {
                            int id = reader.GetInt();
                            Vector3 pos = GetVec3(reader);
                            byte r = reader.GetByte();
                            byte g = reader.GetByte();
                            byte b = reader.GetByte();
                            string name = reader.GetString();
                            int health = reader.GetInt();
                            int kills = reader.GetInt();
                            int deaths = reader.GetInt();

                            if (id == localPlayer.Id)
                            {
                                // Свою позицию считаем локально — синхронизируем только счётчики
                                localPlayer.Kills = kills;
                                localPlayer.Deaths = deaths;
                                localPlayer.Health = health;
                                continue;
                            }

                            var p = players.Find(pl => pl.Id == id);
                            if (p == null)
                            {
                                p = new Player3D { Id = id };
                                players.Add(p);
                            }
                            p.Position = pos;
                            p.PlayerColor = new Color(r, g, b, (byte)255);
                            p.Name = name;
                            p.Health = health;
                            p.Kills = kills;
                            p.Deaths = deaths;
                        }
                        break;
                    }
                    case PT_WorldObjects:
                    {
                        if (Mode != NetMode.Client) break;
                        int count = reader.GetInt();
                        for (int i = 0; i < count; i++)
                        {
                            int netId = reader.GetInt();
                            byte objType = reader.GetByte();
                            Vector3 pos = GetVec3(reader);
                            Vector3 size = GetVec3(reader);
                            byte r = reader.GetByte();
                            byte g = reader.GetByte();
                            byte b = reader.GetByte();
                            bool isHeld = reader.GetBool();
                            int heldBy = reader.GetInt();

                            if (!objectsByNetId.TryGetValue(netId, out var obj))
                            {
                                obj = new PhysicalObject3D { NetId = netId };
                                objectsByNetId[netId] = obj;
                                worldObjects.Add(obj);
                            }
                            obj.Type = (EntityType)objType;
                            obj.Size = size;
                            obj.Color = new Color(r, g, b, (byte)255);
                            obj.IsHeld = isHeld;
                            obj.HeldByPlayerId = heldBy;

                            // Позицию предмета, который держим мы сами, обновляем локально для плавности,
                            // а не значением, полученным по сети (оно чуть запаздывает).
                            if (!(isHeld && heldBy == localPlayer.Id))
                            {
                                obj.Position = pos;
                            }
                        }
                        break;
                    }
                    case PT_Zombies:
                    {
                        if (Mode != NetMode.Client) break;
                        int count = reader.GetInt();
                        var seen = new HashSet<int>();
                        for (int i = 0; i < count; i++)
                        {
                            int netId = reader.GetInt();
                            Vector3 pos = GetVec3(reader);
                            float yaw = reader.GetFloat();
                            int health = reader.GetInt();

                            seen.Add(netId);
                            if (!zombiesByNetId.TryGetValue(netId, out var z))
                            {
                                z = new Zombie3D { NetId = netId };
                                zombiesByNetId[netId] = z;
                                zombies.Add(z);
                            }
                            z.Position = pos;
                            z.Yaw = yaw;
                            z.Health = health;
                        }

                        // Удаляем зомби, которых больше нет у хоста (погибли)
                        var toRemove = new List<int>();
                        foreach (var key in zombiesByNetId.Keys)
                        {
                            if (!seen.Contains(key)) toRemove.Add(key);
                        }
                        foreach (var key in toRemove)
                        {
                            zombies.Remove(zombiesByNetId[key]);
                            zombiesByNetId.Remove(key);
                        }
                        break;
                    }
                    case PT_ChatSend:
                    {
                        if (Mode != NetMode.Host) break;
                        string text = reader.GetString();
                        int id = peer.Id + 100;
                        var p = players.Find(pl => pl.Id == id);
                        string sender = p != null ? p.Name : "Игрок";
                        Color color = p != null ? p.PlayerColor : Color.White;
                        AppendChat(sender, text, color);
                        BroadcastChat(sender, text, color);
                        break;
                    }
                    case PT_ChatBroadcast:
                    {
                        if (Mode != NetMode.Client) break;
                        string sender = reader.GetString();
                        string text = reader.GetString();
                        byte r = reader.GetByte();
                        byte g = reader.GetByte();
                        byte b = reader.GetByte();
                        AppendChat(sender, text, new Color(r, g, b, (byte)255));
                        break;
                    }
                    case PT_PlayerLeft:
                    {
                        if (Mode != NetMode.Client) break;
                        int id = reader.GetInt();
                        players.RemoveAll(pl => pl.Id == id);
                        break;
                    }
                }
            }
            finally
            {
                reader.Recycle();
            }
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            try
            {
                string magic = reader.GetString();
                if (magic == DiscoveryRequestMagic && Mode == NetMode.Host)
                {
                    var writer = new NetDataWriter();
                    writer.Put(DiscoveryResponseMagic);
                    writer.Put(serverName);
                    writer.Put(players != null ? players.Count : 0);
                    writer.Put(maxPlayers);
                    netManager.SendUnconnectedMessage(writer, remoteEndPoint);
                }
                else if (magic == DiscoveryResponseMagic && IsScanning)
                {
                    string name = reader.GetString();
                    int cur = reader.GetInt();
                    int max = reader.GetInt();
                    string ip = remoteEndPoint.Address.ToString();

                    for (int i = 0; i < DiscoveredServers.Count; i++)
                    {
                        if (DiscoveredServers[i].Ip == ip)
                        {
                            var updated = DiscoveredServers[i];
                            updated.CurrentPlayers = cur;
                            updated.MaxPlayers = max;
                            DiscoveredServers[i] = updated;
                            return;
                        }
                    }

                    DiscoveredServers.Add(new DiscoveredServer
                    {
                        Name = name,
                        Ip = ip,
                        Port = GamePort,
                        CurrentPlayers = cur,
                        MaxPlayers = max
                    });
                }
            }
            catch
            {
                // Игнорируем чужие/повреждённые UDP-пакеты, прилетевшие в широковещательный порт
            }
            finally
            {
                reader.Recycle();
            }
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            if (Mode == NetMode.Host)
            {
                if (players != null && players.Count >= maxPlayers)
                {
                    request.Reject();
                    return;
                }
                request.AcceptIfKey(ConnectionKey);
            }
            else
            {
                request.Reject();
            }
        }

        private static void PutVec3(NetDataWriter writer, Vector3 v)
        {
            writer.Put(v.X);
            writer.Put(v.Y);
            writer.Put(v.Z);
        }

        private static Vector3 GetVec3(NetDataReader reader)
        {
            return new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        }
    }
}