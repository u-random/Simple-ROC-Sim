using System.Collections.Generic;
using WebSocketSharp.Server;
using WebSocketSharp;
using UnityEngine;
using System;


/// <summary>
/// Manages WebSocket communication between Unity simulator and ROC web application
/// </summary>
public class WebSocketManager : MonoBehaviour, IMessageResponder
{
    [Header("Components")]
    [SerializeField] private float telemetryUpdateRate = 0.1f;
    [SerializeField] private Camera streamCamera;
    [SerializeField] private int cameraQuality = 75;
    [SerializeField] private int cameraFps = 15;
    [SerializeField] private int port = 3000;

    // Component references
    private TelemetryProvider telemetryProvider;
    private MessageProcessor messageProcessor;
    private WebSocketServiceHost serviceHost;
    private WebSocketServer webSocketServer;
    private ClientManager clientManager;
    private CameraManager cameraManager;

    // Timing
    private float lastTelemetryTime = 0;

    // Threading
    private Queue<KeyValuePair<string, string>> messageQueue = new Queue<KeyValuePair<string, string>>();
    private object queueLock = new object();

    #region Unity Lifecycle Methods

    void Start()
    {
        Debug.Log("WebSocketManager: Initializing...");

        // Initialize components
        InitializeWebSocketServer();
        InitializeComponents();

        // Subscribe to events
        cameraManager.OnCompressedFrameReady += HandleCameraFrame;

        Debug.Log("WebSocketManager: Initialization complete");
    }

    void Update()
    {
        // Process message queue
        ProcessMessageQueue();

        // Update camera manager
        cameraManager.Update();

        // TODO: This is the limiting factor for the frame rate
        // Send telemetry at specified rate
        if (Time.time - lastTelemetryTime > telemetryUpdateRate)
        {
            SendTelemetryData();
            lastTelemetryTime = Time.time;
        }

        // Update client connection states
        clientManager.UpdateClientStates();
    }

    void OnDestroy()
    {
        Debug.Log("WebSocketManager: Cleaning up resources");

        // Unsubscribe from events
        if (cameraManager != null)
        {
            cameraManager.OnCompressedFrameReady -= HandleCameraFrame;
            cameraManager.Cleanup();
        }

        // Stop WebSocket server
        if (webSocketServer != null && webSocketServer.IsListening)
        {
            webSocketServer.Stop();
        }
    }

    #endregion

    #region Initialization

    private void InitializeWebSocketServer()
    {
        Debug.Log($"WebSocketManager: Starting WebSocket server on port {port}");

        try
        {
            webSocketServer = new WebSocketServer(port);
            webSocketServer.AddWebSocketService<ROCHandler>("/", () => {
                var handler = new ROCHandler();
                handler.SetManager(this);
                return handler;
            });

            webSocketServer.Start();
            serviceHost = webSocketServer.WebSocketServices["/"];

            Debug.Log("WebSocketManager: WebSocket server started successfully");
        }
        catch (Exception ex)
        {
            Debug.LogError($"WebSocketManager: Failed to start WebSocket server: {ex.Message}");
        }
    }

    private void InitializeComponents()
    {
        // Initialize client manager
        clientManager = new ClientManager(serviceHost);

        // Initialize telemetry provider
        telemetryProvider = new TelemetryProvider(transform, GetComponent<Rigidbody>());

        // Initialize camera manager
        cameraManager = new CameraManager(
            streamCamera,
            width: 1280,
            height: 540,
            fps: cameraFps,
            quality: cameraQuality
        );

        // Initialize message processor
        messageProcessor = new MessageProcessor(this);

        // Start camera streaming with this ship's ID
        cameraManager.StartStreaming(gameObject.GetInstanceID());
    }

    #endregion

    #region Message Handling

    // Called by ROCHandler when client sends a message
    public void HandleClientMessage(string message, string clientId)
    {
        // Queue message for processing on main thread
        lock (queueLock)
        {
            messageQueue.Enqueue(new KeyValuePair<string, string>(clientId, message));
        }
    }

    private void ProcessMessageQueue()
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

            // Process message on main thread
            messageProcessor.ProcessMessage(item.Value, item.Key);
            processedCount++;
        }
    }

    #endregion

    #region Telemetry

    private void SendTelemetryData()
    {
        if (!clientManager.HasActiveClients()) return;

        // Generate telemetry data
        var telemetryData = telemetryProvider.GenerateTelemetry();

        // TODO: Remove camera feed from telemetry message
        // Add camera feed if available
        int shipId = telemetryData.id;
        string cameraFrame = cameraManager.GetLatestFrame(shipId);
        if (!string.IsNullOrEmpty(cameraFrame))
        {
            telemetryData.cameraFeed = cameraFrame;
        }

        // Broadcast to all clients
        clientManager.BroadcastToAll(telemetryData);
    }

    private void HandleCameraFrame(int shipId, string base64Frame)
    {
        // This method is called when a new camera frame is ready
        // We don't need to do anything here since GetLatestFrame will retrieve
        // the latest frame when sending telemetry
    }

    #endregion

    #region IMessageResponder Implementation

    public void SendToClient(string clientId, object data)
    {
        clientManager.SendToClient(clientId, data);
    }

    public void BroadcastToAllClients(object data)
    {
        clientManager.BroadcastToAll(data);
    }

    public void RegisterClient(string clientId)
    {
        clientManager.RegisterClient(clientId);

        // Send initial connected message
        var connectMessage = new {
            type = "simulator_connected",
            id = gameObject.GetInstanceID(),
            name = gameObject.name
        };

        SendToClient(clientId, connectMessage);
    }

    public void UnregisterClient(string clientId)
    {
        clientManager.UnregisterClient(clientId);
    }

    #endregion
}

public class ROCHandler : WebSocketBehavior
{
    private WebSocketManager manager;

    public void SetManager(WebSocketManager manager)
    {
        this.manager = manager;
    }

    protected override void OnOpen()
    {
        Debug.Log($"ROCHandler: Client {ID} connected");
        manager.RegisterClient(ID);
    }

    protected override void OnClose(CloseEventArgs e)
    {
        Debug.Log($"ROCHandler: Client {ID} disconnected");
        manager.UnregisterClient(ID);
    }

    protected override void OnMessage(MessageEventArgs e)
    {
        Debug.Log($"ROCHandler: Received message from client {ID}");
        manager.HandleClientMessage(e.Data, ID);
    }
}