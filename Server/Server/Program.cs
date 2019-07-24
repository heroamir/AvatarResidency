using System;
using System.Collections.Generic;
using System.IO;
using ENet;

namespace Server
{

    class Program
    {
        struct Position
        {
            public float x;
            public float y;
            public float z;
        }

        static Host _server = new Host();
        private static Dictionary<uint, Position> _players = new Dictionary<uint, Position>();

        static void Main(string[] args)
        {
            const ushort port = 1234;
            const int maxClients = 100;
            Library.Initialize();

            _server = new Host();
            Address address = new Address();
            
            address.Port = port;
            _server.Create(address, maxClients);

            Console.WriteLine($"Server started on {port}");

            Event netEvent;
            while (!Console.KeyAvailable)
            {
                bool polled = false;

                while (!polled)
                {
                    if (_server.CheckEvents(out netEvent) <= 0)
                    {
                        if (_server.Service(15, out netEvent) <= 0)
                            break;

                        polled = true;
                    }

                    switch (netEvent.Type)
                    {
                        case EventType.None:
                            break;

                        case EventType.Connect:
                            Console.WriteLine("Client connected - ID: " + netEvent.Peer.ID + ", IP: " + netEvent.Peer.IP);
                            netEvent.Peer.Timeout(32, 1000, 4000);
                            break;

                        case EventType.Disconnect:
                            Console.WriteLine("Client disconnected - ID: " + netEvent.Peer.ID + ", IP: " + netEvent.Peer.IP);
                            HandleLogout(netEvent.Peer.ID);
                            break;

                        case EventType.Timeout:
                            Console.WriteLine("Client timeout - ID: " + netEvent.Peer.ID + ", IP: " + netEvent.Peer.IP);
                            HandleLogout(netEvent.Peer.ID);
                            break;

                        case EventType.Receive:
                            //Console.WriteLine("Packet received from - ID: " + netEvent.Peer.ID + ", IP: " + netEvent.Peer.IP + ", Channel ID: " + netEvent.ChannelID + ", Data length: " + netEvent.Packet.Length);
                            HandlePacket(ref netEvent);
                            netEvent.Packet.Dispose();
                            break;
                    }
                }

                _server.Flush();
            }
            Library.Deinitialize();
        }

        enum PacketId : byte
        {
            LoginRequest = 1,
            LoginResponse = 2,
            LoginEvent = 3,
            PositionUpdateRequest = 4,
            PositionUpdateEvent = 5,
            LogoutEvent = 6,
            ChangeStatus = 7,
            ChangeStatusEvent = 8,
            ChangeColorRequest = 9,
            ChangeColorEvent = 10,
            ResetRequest = 11,
            AudioRequest = 12,
            AudioEvent = 13,
            UploadObjRequest = 14,
            UploadObjEvent = 15,
            PlayerPositionUpdateRequest = 16,
            PlayerPositionUpdateEvent = 17,
        }

        static void HandlePacket(ref Event netEvent)
        {
            var readBuffer = new byte[8000000];//4681737
            var readStream = new MemoryStream(readBuffer);
            var reader = new BinaryReader(readStream);

            readStream.Position = 0;
            netEvent.Packet.CopyTo(readBuffer);
            var packetId = (PacketId)reader.ReadByte();

            if (packetId != PacketId.PositionUpdateRequest && packetId != PacketId.PlayerPositionUpdateRequest)
                Console.WriteLine($"HandlePacket received: {packetId}");

            if (packetId == PacketId.LoginRequest)
            {
                var playerId = netEvent.Peer.ID;
                SendLoginResponse(ref netEvent, playerId);
                BroadcastLoginEvent(playerId);
                foreach (var p in _players)
                {
                    SendLoginEvent(ref netEvent, p.Key);
                }
                _players.Add(playerId, new Position { x = 10.0f, y = 10.0f, z = 10.0f });
            }
            else if (packetId == PacketId.PositionUpdateRequest)
            {
                var playerId = reader.ReadUInt32();
                var x = reader.ReadSingle();
                var y = reader.ReadSingle();
                var z = reader.ReadSingle();

                Console.WriteLine($"ID: {playerId}, Pos: {x}, {y}, {z}");
                BroadcastPositionUpdateEvent(playerId, x, y, z);
            }
            else if (packetId == PacketId.PlayerPositionUpdateRequest)
            {
                var playerId = reader.ReadUInt32();
                var x = reader.ReadSingle();
                var y = reader.ReadSingle();
                var z = reader.ReadSingle();
                // Quaternion
                var xq = reader.ReadSingle();
                var yq = reader.ReadSingle();
                var zq = reader.ReadSingle();
                var wq = reader.ReadSingle();

                BroadcastPlayerPositionUpdateEvent(playerId, x, y, z, xq, yq, zq, wq);
            }
            else if(packetId == PacketId.ChangeColorRequest)
            {
                var playerId = reader.ReadUInt32();
                var red = reader.ReadSingle();
                var green = reader.ReadSingle();
                var blue = reader.ReadSingle();
                Console.WriteLine($"ID: {playerId}, status change request, Color: {red}, {green}, {blue}");
                BroadcastChangeColorEvent(playerId, red, green, blue);
            }
            else if (packetId == PacketId.ChangeStatus)
            {
                var playerId = reader.ReadUInt32();
                Console.WriteLine($"ID: {playerId}, change status request");
                BroadcastChangeStatusEvent(playerId);
            }
            else if (packetId == PacketId.AudioRequest)
            {
                var playerId = reader.ReadUInt32();
                var size = reader.ReadInt32();
                byte[] audio = reader.ReadBytes(size);

                Console.WriteLine($"ID: {playerId}, Audio Received");
                BroadcastAudioEvent(playerId, size, audio);
            }
            else if (packetId == PacketId.UploadObjRequest)
            {
                var playerId = reader.ReadUInt32();
                var size = reader.ReadInt32();
                byte[] obj = reader.ReadBytes(size);

                Console.WriteLine($"ID: {playerId}, Object Received");
                BroadcastUploadObjEvent(playerId, size, obj);
            }
        }

        static void SendLoginResponse(ref Event netEvent, uint playerId)
        {
            var protocol = new Protocol();
            var buffer = protocol.Serialize((byte)PacketId.LoginResponse, playerId);
            var packet = default(Packet);
            packet.Create(buffer);
            netEvent.Peer.Send(0, ref packet);
        }

        static void SendLoginEvent(ref Event netEvent, uint playerId)
        {
            var protocol = new Protocol();
            var buffer = protocol.Serialize((byte)PacketId.LoginEvent, playerId);
            var packet = default(Packet);
            packet.Create(buffer);
            netEvent.Peer.Send(0, ref packet);
        }

        static void BroadcastLoginEvent(uint playerId)
        {
            var protocol = new Protocol();
            var buffer = protocol.Serialize((byte)PacketId.LoginEvent, playerId);
            var packet = default(Packet);
            packet.Create(buffer);
            _server.Broadcast(0, ref packet);
        }

        static void BroadcastLogoutEvent(uint playerId)
        {
            var protocol = new Protocol();
            var buffer = protocol.Serialize((byte)PacketId.LogoutEvent, playerId);
            var packet = default(Packet);
            packet.Create(buffer);
            _server.Broadcast(0, ref packet);
        }

        static void BroadcastPositionUpdateEvent(uint playerId, float x, float y, float z)
        {
            var protocol = new Protocol();
            var buffer = protocol.Serialize((byte)PacketId.PositionUpdateEvent, playerId, x, y, z);
            var packet = default(Packet);
            packet.Create(buffer);
            _server.Broadcast(0, ref packet);
        }

        static void BroadcastPlayerPositionUpdateEvent(uint playerId, float x, float y, float z, float xq, float yq, float zq, float wq)
        {
            var protocol = new Protocol();
            var buffer = protocol.Serialize((byte)PacketId.PlayerPositionUpdateEvent, playerId, x, y, z, xq, yq, zq, wq);
            var packet = default(Packet);
            packet.Create(buffer);
            _server.Broadcast(0, ref packet);
        }

        static void BroadcastChangeStatusEvent(uint playerId)
        {
            var protocol = new Protocol();
            var buffer = protocol.Serialize((byte)PacketId.ChangeStatusEvent, playerId);
            var packet = default(Packet);
            packet.Create(buffer);
            _server.Broadcast(0, ref packet);
        }
        static void BroadcastChangeColorEvent(uint playerId, float red, float green, float blue)
        {
            var protocol = new Protocol();
            var buffer = protocol.Serialize((byte)PacketId.ChangeColorEvent, playerId, red, green, blue);
            var packet = default(Packet);
            packet.Create(buffer);
            _server.Broadcast(0, ref packet);
        }
        static void BroadcastAudioEvent(uint playerId, int size, byte[] source)
        {
            var protocol = new Protocol();
            var buffer = protocol.Serialize((byte)PacketId.AudioEvent, playerId, size, source);
            var packet = default(Packet);
            packet.Create(buffer);
            _server.Broadcast(0, ref packet);
        }
        static void BroadcastUploadObjEvent(uint playerId, int size, byte[] source)
        {
            var protocol = new Protocol();
            var buffer = protocol.Serialize((byte)PacketId.UploadObjEvent, playerId, size, source);
            var packet = default(Packet);
            packet.Create(buffer);
            _server.Broadcast(0, ref packet);
        }
        static void HandleLogout(uint playerId)
        {
            if (!_players.ContainsKey(playerId))
                return;

            _players.Remove(playerId);
            BroadcastLogoutEvent(playerId);
        }
    }
}
