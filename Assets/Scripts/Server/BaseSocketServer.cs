using System.Collections.Generic;
using WebSocketSharp.Server;
using WebSocketSharp;
using UnityEngine;
using System;


// TODO: Consider taking in an array of gameObjects that can be exposed to ROC


/// <summary>
/// WebSocket base class for communication between Unity simulator and ROC web application
/// </summary>
public abstract class BaseSocketServer : MonoBehaviour, IMessageResponder
{
    [Header("Base Server Parameters")]
    [SerializeField] protected int port = 3000;

    protected MessageProcessor messageProcessor;
    protected WebSocketServiceHost serviceHost;
    protected WebSocketServer webSocketServer;
    protected ClientManager clientManager;

    protected Queue<KeyValuePair<string, string>> messageQueue = new Queue<KeyValuePair<string, string>>();
    protected object queueLock = new object();
    protected string endpoint = "/";


    #region Unity Lifecycle Methods

    protected virtual void Start()
    {
        InitializeWebSocketServer();
        InitializeComponents();

        clientManager = new ClientManager(serviceHost);
        messageProcessor = new MessageProcessor(this);

        Debug.Log($"{GetType().Name}: Initialized on port {port}");
    }

    protected virtual void Update()
    {
        ProcessMessageQueue();
        SendData();
        
        // Safety check for null clientManager
        if (clientManager == null)
        {
            Debug.LogError($"{GetType().Name}: clientManager is null in Update! This indicates a initialization problem.");
            
            // Try to create a new clientManager if serviceHost is valid
            if (serviceHost != null)
            {
                Debug.Log($"{GetType().Name}: Attempting to recreate clientManager...");
                clientManager = new ClientManager(serviceHost);
            }
            else
            {
                Debug.LogError($"{GetType().Name}: Cannot recreate clientManager - serviceHost is also null!");
            }
            
            return;
        }
        
        // Update client states if clientManager is valid
        clientManager.UpdateClientStates();
    }

    protected virtual void OnDestroy()
    {
        CleanupResources();

        if (webSocketServer != null && webSocketServer.IsListening)
        {
            webSocketServer.Stop();
        }
    }

    #endregion

    #region Initialization

    protected virtual void InitializeWebSocketServer()
    {
        Debug.Log($"{GetType().Name}: Starting WebSocket server on port {port}");

        try
        {
            webSocketServer = new WebSocketServer(port);
            webSocketServer.AddWebSocketService<BaseSocketHandler>(endpoint, () => {
                var handler = CreateSocketHandler();
                handler.SetServer(this);
                return handler;
            });

            webSocketServer.Start();
            serviceHost = webSocketServer.WebSocketServices[endpoint];
        }
        catch (Exception ex)
        {
            Debug.LogError($"{GetType().Name}: Failed to start WebSocket server: {ex.Message}");
        }
    }

    // Factory method for creating handlers
    protected abstract BaseSocketHandler CreateSocketHandler();

    // Abstract methods
    protected abstract void InitializeComponents();
    protected abstract void SendData();
    protected abstract void CleanupResources();

    #endregion

    #region Message Handling
    // Message handling
    public void HandleClientMessage(string message, string clientId)
    {
        lock (queueLock)
        {
            messageQueue.Enqueue(new KeyValuePair<string, string>(clientId, message));
        }
    }

    protected virtual void ProcessMessageQueue()
    {
        const int maxMessagesPerFrame = 10;
        int processedCount = 0;

        while (processedCount < maxMessagesPerFrame)
        {
            KeyValuePair<string, string> item;
            bool hasMessage;

            lock (queueLock)
            {
                hasMessage = messageQueue.Count > 0;
                item = hasMessage ? messageQueue.Dequeue() : default;
            }

            if (!hasMessage) break;

            messageProcessor.ProcessMessage(item.Value, item.Key);
            processedCount++;
        }
    }

    #endregion

    #region IMessageResponder Implementation

    // IMessageResponder implementation
    public virtual void SendToClient(string clientId, object data)
    {
        clientManager.SendToClient(clientId, data);
    }

    public virtual void BroadcastToAllClients(object data)
    {
        clientManager.BroadcastToAll(data);
    }

    public virtual void RegisterClient(string clientId)
    {
        Debug.Log($"Registering client {clientId} with {GetType().Name}");
        clientManager.RegisterClient(clientId);

        var connectMessage = new {
            type = "server_connected",
            id = gameObject.GetInstanceID(),
            name = gameObject.name,
            serverType = GetType().Name
        };

        SendToClient(clientId, connectMessage);
    }

    public virtual void UnregisterClient(string clientId)
    {
        clientManager.UnregisterClient(clientId);
    }

    #endregion
}


public abstract class BaseSocketHandler : WebSocketBehavior
{
    protected BaseSocketServer server;

    public void SetServer(BaseSocketServer server)
    {
        this.server = server;
    }

    protected override void OnOpen()
    {
        Debug.Log($"Client {ID} connected to {server.GetType().Name}");
        server.RegisterClient(ID);
    }

    protected override void OnClose(CloseEventArgs e)
    {
        Debug.Log($"Client {ID} disconnected from {server.GetType().Name}");
        server.UnregisterClient(ID);
    }

    protected override void OnMessage(MessageEventArgs e)
    {
        server.HandleClientMessage(e.Data, ID);
    }
}