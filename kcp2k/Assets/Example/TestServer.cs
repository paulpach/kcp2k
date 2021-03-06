﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace kcp2k.Examples
{
    public class TestServer : MonoBehaviour
    {
        // configuration
        public ushort Port = 7777;
        [Tooltip("NoDelay is recommended to reduce latency. This also scales better without buffers getting full.")]
        public bool NoDelay = true;
        [Tooltip("KCP internal update interval. 100ms is KCP default, but a lower interval is recommended to minimize latency.")]
        public uint Interval = 40;

        // server
        readonly byte[] buffer = new byte[Kcp.MTU_DEF];
        Socket serverSocket;
        EndPoint serverNewClientEP = new IPEndPoint(IPAddress.IPv6Any, 0);
        // connections <connectionId, connection> where connectionId is EndPoint.GetHashCode
        public Dictionary<int, KcpServerConnection> connections = new Dictionary<int, KcpServerConnection>();

        public bool Active() => serverSocket != null;

        public void StartServer()
        {
            // only start once
            if (serverSocket != null)
            {
                Debug.LogWarning("KCP: server already started!");
            }

            // listen
            Debug.Log("KCP: starting server");
            serverSocket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            serverSocket.DualMode = true;
            serverSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, Port));
            Debug.Log("KCP: server started");
        }

        public void Send(int connectionId, ArraySegment<byte> segment)
        {
            if (connections.TryGetValue(connectionId, out KcpServerConnection connection))
            {
                connection.Send(segment);
            }
        }

        public bool Disconnect(int connectionId)
        {
            if (connections.TryGetValue(connectionId, out KcpServerConnection connection))
            {
                connection.Disconnect();
                return true;
            }
            return false;
        }

        public string GetAddress(int connectionId)
        {
            if (connections.TryGetValue(connectionId, out KcpServerConnection connection))
            {
                return (connection.GetRemoteEndPoint() as IPEndPoint).Address.ToString();
            }
            return "";
        }

        public void StopServer()
        {
            serverSocket?.Close();
            serverSocket = null;
            Debug.Log("KCP: server stopped");
        }

        // MonoBehaviour ///////////////////////////////////////////////////////
        HashSet<int> connectionsToRemove = new HashSet<int>();
        void UpdateServer()
        {
            while (serverSocket != null && serverSocket.Poll(0, SelectMode.SelectRead))
            {
                int msgLength = serverSocket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref serverNewClientEP);
                //Debug.Log($"KCP: server raw recv {msgLength} bytes = {BitConverter.ToString(buffer, 0, msgLength)}");

                // calculate connectionId from endpoint
                int connectionId = serverNewClientEP.GetHashCode();

                // is this a new connection?
                if (!connections.TryGetValue(connectionId, out KcpServerConnection connection))
                {
                    // add it to a queue
                    connection = new KcpServerConnection(serverSocket, serverNewClientEP, NoDelay, Interval);

                    //acceptedConnections.Writer.TryWrite(connection);
                    connections.Add(connectionId, connection);
                    Debug.Log($"KCP: server added connection {serverNewClientEP}");

                    // setup connected event
                    connection.OnConnected += () =>
                    {
                        // call mirror event
                        Debug.Log($"KCP: OnServerConnected({connectionId})");
                    };

                    // setup data event
                    connection.OnData += (message) =>
                    {
                        // call mirror event
                        Debug.Log($"KCP: OnServerDataReceived({connectionId}, {BitConverter.ToString(message.Array, message.Offset, message.Count)})");
                    };

                    // setup disconnected event
                    connection.OnDisconnected += () =>
                    {
                        // flag for removal
                        // (can't remove directly because connection is updated
                        //  and event is called while iterating all connections)
                        connectionsToRemove.Add(connectionId);

                        // call mirror event
                        Debug.Log($"KCP: OnServerDisconnected({connectionId})");
                    };

                    // send handshake
                    connection.Handshake();
                }

                connection.RawInput(buffer, msgLength);
            }

            // tick all server connections
            foreach (KcpServerConnection connection in connections.Values)
            {
                connection.Tick();
                connection.Receive();
            }

            // remove disconnected connections
            // (can't do it in connection.OnDisconnected because Tick is called
            //  while iterating connections)
            foreach (int connectionId in connectionsToRemove)
            {
                connections.Remove(connectionId);
            }
            connectionsToRemove.Clear();
        }

        public void LateUpdate()
        {
            UpdateServer();
        }

        void OnGUI()
        {
            int firstclient = connections.Count > 0 ? connections.First().Key : -1;

            GUILayout.BeginArea(new Rect(160, 5, 250, 400));
            GUILayout.Label("Server:");
            if (GUILayout.Button("Start"))
            {
                StartServer();
            }
            if (GUILayout.Button("Send 0x01, 0x02 to " + firstclient))
            {
                Send(firstclient, new ArraySegment<byte>(new byte[]{0x01, 0x02}));
            }
            if (GUILayout.Button("Disconnect connection " + firstclient))
            {
                Disconnect(firstclient);
            }
            if (GUILayout.Button("Stop"))
            {
                StopServer();
            }
            GUILayout.EndArea();
        }
    }
}
