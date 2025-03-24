using System.Collections.Generic;
using WebSocketSharp.Server;
using UnityEngine;
using System;

/// <summary>
/// Manages WebSocket client connections and message distribution
/// </summary>
public class ClientManager
{
    #region Enums and Events

    /// <summary>
    /// Connection states for clients
    /// </summary>
    public enum ConnectionState
    {
        Connected,
        Reconnecting,
        Disconnected
    }

    #endregion

    #region Configuration

    /// <summary>
    /// Maximum number of reconnection attempts
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 5;

    /// <summary>
    /// Base delay between reconnection attempts in milliseconds
    /// </summary>
    public int ReconnectBaseDelayMs { get; set; } = 1000;

    /// <summary>
    /// Time in milliseconds after which a client is considered inactive
    /// </summary>
    public int StaleTimeoutMs { get; set; } = 5000;

    #endregion

    private Dictionary<string, ClientInfo> clients = new Dictionary<string, ClientInfo>();
    private WebSocketServiceHost serviceHost;

    /// <summary>
    /// Information about a connected client
    /// </summary>
    private class ClientInfo
    {
        public string Id { get; }
        public DateTime LastActivity { get; set; }
        public bool IsActive { get; set; }
        public ConnectionState State { get; set; }
        public int ReconnectAttempts { get; set; }
        public DateTime NextReconnectTime { get; set; }

        public ClientInfo(string id)
        {
            Id = id;
            LastActivity = DateTime.UtcNow;
            IsActive = true;
            State = ConnectionState.Connected;
            ReconnectAttempts = 0;
        }

        /// <summary>
        /// Calculate reconnect delay using exponential backoff
        /// </summary>
        public int CalculateReconnectDelay(int baseDelay, int maxDelay)
        {
            // Exponential backoff: baseDelay * 2^attempts with jitter
            double backoff = baseDelay * Math.Pow(2, ReconnectAttempts);
            double jitter = new System.Random().NextDouble() * 0.3 * backoff; // 30% jitter
            return (int)Math.Min(backoff + jitter, 30000); // Cap at 30 seconds
        }
    }

    /// <summary>
    /// Initialize the client manager with a service host
    /// </summary>
    public ClientManager(WebSocketServiceHost host)
    {
        serviceHost = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// Register a new client
    /// </summary>
    public void RegisterClient(string clientId)
    {
        if (string.IsNullOrEmpty(clientId))
            throw new ArgumentException("Client ID cannot be null or empty", nameof(clientId));

        if (clients.TryGetValue(clientId, out var client))
        {
            client.IsActive = true;
            client.LastActivity = DateTime.UtcNow;
            client.State = ConnectionState.Connected;
            client.ReconnectAttempts = 0;
            Debug.Log($"Client {clientId} reconnected");
        }
        else
        {
            clients.Add(clientId, new ClientInfo(clientId));
            Debug.Log($"Client {clientId} registered");
        }
    }

    /// <summary>
    /// Unregister a client
    /// </summary>
    public void UnregisterClient(string clientId)
    {
        if (clients.TryGetValue(clientId, out var client))
        {
            client.IsActive = false;
            client.State = ConnectionState.Disconnected;
            Debug.Log($"Client {clientId} unregistered");
        }
    }

    /// <summary>
    /// Send a message to a specific client
    /// </summary>
    public bool SendToClient(string clientId, object data)
    {
        if (!clients.TryGetValue(clientId, out var client) || !client.IsActive)
        {
            Debug.LogWarning($"Attempted to send message to inactive/unknown client: {clientId}");
            return false;
        }

        try
        {
            string json = JsonUtility.ToJson(data);
            serviceHost.Sessions.SendTo(json, clientId);
            client.LastActivity = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error sending message to client {clientId}: {ex.Message}");

            if (client.State == ConnectionState.Connected)
            {
                BeginReconnect(clientId);
            }
            return false;
        }
    }

    /// <summary>
    /// Start reconnection process for client
    /// </summary>
    private void BeginReconnect(string clientId)
    {
        if (!clients.TryGetValue(clientId, out var client))
            return;

        if (client.State == ConnectionState.Reconnecting || client.ReconnectAttempts >= MaxReconnectAttempts)
            return;

        client.ReconnectAttempts++;
        int delay = client.CalculateReconnectDelay(ReconnectBaseDelayMs, 30000);
        client.NextReconnectTime = DateTime.UtcNow.AddMilliseconds(delay);
        client.State = ConnectionState.Reconnecting;

        Debug.Log($"Beginning reconnect for client {clientId} (attempt {client.ReconnectAttempts}/{MaxReconnectAttempts}, delay {delay}ms)");
    }

    /// <summary>
    /// Broadcast a message to all active clients
    /// </summary>
    public int BroadcastToAll(object data)
    {
        if (clients.Count == 0)
            return 0;

        int successCount = 0;
        try
        {
            string json = JsonUtility.ToJson(data);
            //Debug.Log($"Broadcasting JSON, length: {json?.Length ?? 0}");
            serviceHost.Sessions.Broadcast(json);

            // Update last activity for all clients
            DateTime now = DateTime.UtcNow;
            foreach (var client in clients.Values)
            {
                if (client.IsActive && client.State == ConnectionState.Connected)
                {
                    client.LastActivity = now;
                    successCount++;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error broadcasting message: {ex.Message}");
        }

        return successCount;
    }

    /// <summary>
    /// Check client timeouts and manage reconnections
    /// </summary>
    public void UpdateClientStates()
    {
        DateTime now = DateTime.UtcNow;

        foreach (var client in clients.Values)
        {
            // Skip inactive clients
            if (!client.IsActive)
                continue;

            // Handle reconnecting clients
            if (client.State == ConnectionState.Reconnecting)
            {
                if (now >= client.NextReconnectTime)
                {
                    if (client.ReconnectAttempts >= MaxReconnectAttempts)
                    {
                        // Max reconnect attempts exceeded
                        client.State = ConnectionState.Disconnected;
                        client.IsActive = false;
                        Debug.LogWarning($"Client {client.Id} reconnection failed after {MaxReconnectAttempts} attempts");
                    }
                    else
                    {
                        // Try reconnect by sending a ping
                        try
                        {
                            var pingMessage = new { type = "ping", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                            string json = JsonUtility.ToJson(pingMessage);
                            serviceHost.Sessions.SendTo(json, client.Id);

                            // Update for next attempt
                            client.ReconnectAttempts++;
                            int delay = client.CalculateReconnectDelay(ReconnectBaseDelayMs, 30000);
                            client.NextReconnectTime = now.AddMilliseconds(delay);

                            Debug.Log($"Sent reconnect ping to client {client.Id} (attempt {client.ReconnectAttempts})");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"Failed to send reconnect ping to {client.Id}: {ex.Message}");
                        }
                    }
                }
            }

            // Check for stale connections
            else if (client.State == ConnectionState.Connected)
            {
                TimeSpan inactiveTime = now - client.LastActivity;
                if (inactiveTime.TotalMilliseconds > StaleTimeoutMs)
                {
                    Debug.LogWarning($"Client {client.Id} timed out after {StaleTimeoutMs}ms of inactivity");
                    BeginReconnect(client.Id);
                }
            }
        }
    }

    /// <summary>
    /// Get connection state for a client
    /// </summary>
    public ConnectionState GetClientState(string clientId)
    {
        if (clients.TryGetValue(clientId, out var client))
            return client.State;

        return ConnectionState.Disconnected;
    }

    /// <summary>
    /// Get a list of active client IDs
    /// </summary>
    public List<string> GetActiveClientIds()
    {
        List<string> activeClients = new List<string>();
        foreach (var pair in clients)
        {
            if (pair.Value.IsActive)
                activeClients.Add(pair.Key);
        }
        return activeClients;
    }

    /// <summary>
    /// Check if there are any active clients
    /// </summary>
    public bool HasActiveClients()
    {
        foreach (var client in clients.Values)
        {
            if (client.IsActive)
                return true;
        }
        return false;
    }
}