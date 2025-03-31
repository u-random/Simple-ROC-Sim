using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WebSocketSharp;

[Serializable]
public class CameraFrameMessage
{
    public string type;
    public int id;
    public string frameData;
    public float timestamp;
}

[Serializable]
public class CameraRequestMessage
{
    public string type = "camera_request";
    public int shipId;
    public bool enable;
}

[Serializable]
public class MessageTypeOnly
{
    public string type;
}

public class VideoServer : BaseSocketServer
{
    [Header("Camera Settings")]
    [SerializeField] private int cameraQuality = 75;
    [SerializeField] private int cameraFps = 15;
    [SerializeField] private int width = 1280;
    [SerializeField] private int height = 540;
    
    [Header("Multi-Camera Settings")]
    [SerializeField] private bool enableCameraRequests = true; // UNUSED
    [SerializeField] public TelemetryServer telemetryServer; // Reference to the TelemetryServer for object tracking
    
    // Adaptive streaming settings
    [Header("Adaptive Streaming")]
    [SerializeField] private bool enableAdaptiveQuality = true;
    [SerializeField] private int maxConsecutiveFailures = 3;
    [SerializeField] private int qualityReductionStep = 10;
    [SerializeField] private int minQuality = 40;
    [SerializeField] private int lowBandwidthFps = 10;
    
    private int consecutiveFailures = 0;
    private bool usingLowBandwidthMode = false;
    private int currentActiveShipId = -1;
    private CameraManager cameraManager;
    private TelemetryProvider telemetryProvider;
    private Dictionary<int, CameraManager> cameraManagers = new Dictionary<int, CameraManager>();

    protected override BaseSocketHandler CreateSocketHandler()
    {
        return new VideoSocketHandler(this);
    }

    // Ensure proper initialization sequence after both objects are fully initialized
    protected override void Start()
    {
        Debug.Log("VideoServer: Start method called - waiting for TelemetryServer to be ready");

        // Call base implementation first to initialize server components including clientManager
        base.Start();
        
        // Check reference to TelemetryServer
        if (telemetryServer == null)
        {
            Debug.LogWarning("VideoServer: No TelemetryServer assigned in Inspector. Camera functionality will be limited.");
            Debug.LogWarning("VideoServer: Please assign the TelemetryServer in the Unity Inspector for full functionality!");
        }
        else
        {
            Debug.Log($"VideoServer: Using TelemetryServer reference from Inspector: {telemetryServer.name}");
        }
    }
    
    // Called after Start, try to update telemetryProvider
    private void OnEnable()
    {
        // Add handler to the update loop to retry getting telemetryProvider
        StartCoroutine(DelayedTelemetryProviderCheck());
    }
    
    // Try a few times to get the telemetryProvider if it's not available immediately
    private System.Collections.IEnumerator DelayedTelemetryProviderCheck()
    {
        const int MAX_ATTEMPTS = 5;
        int attempts = 0;
        
        // Try multiple times to get the provider, with increasing delays
        while (telemetryProvider == null && telemetryServer != null && attempts < MAX_ATTEMPTS)
        {
            yield return new WaitForSeconds(0.5f + attempts * 0.5f);  // Increasing delay
            
            Debug.Log($"VideoServer: Attempt {attempts+1} to get TelemetryProvider from TelemetryServer");
            telemetryProvider = telemetryServer.GetTelemetryProvider();
            
            if (telemetryProvider != null)
            {
                Debug.Log("VideoServer: Successfully got TelemetryProvider after delay");
                telemetryProvider.LogAllObjects();
                
                // Actively look for cameras now
                CheckForCameras();
                
                break;
            }
            
            attempts++;
        }
        
        if (telemetryProvider == null && telemetryServer != null)
        {
            Debug.LogError("VideoServer: Failed to get TelemetryProvider after multiple attempts!");
        }
    }
    
    // Check for objects with cameras
    private void CheckForCameras()
    {
        bool anyCamerasFound = false;
        foreach (var obj in GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            Transform cameraTransform = obj.transform.Find("ConningCamera");
            if (cameraTransform != null && cameraTransform.GetComponent<Camera>() != null)
            {
                Debug.Log($"VideoServer: Found object with camera: {obj.name}");
                anyCamerasFound = true;
            }
        }
        
        if (!anyCamerasFound)
        {
            Debug.LogWarning("VideoServer: No objects with 'ConningCamera' children found in scene!");
        }
    }

    protected override void InitializeComponents()
    {
        Debug.Log("VideoServer: InitializeComponents called");
        
        // If TelemetryServer reference is not set, try to find it in the scene
        // but don't rely on it being fully initialized yet
        if (telemetryServer == null)
        {
            telemetryServer = FindObjectOfType<TelemetryServer>();
            if (telemetryServer != null)
            {
                Debug.Log("VideoServer: Found TelemetryServer in scene");
            }
            else
            {
                Debug.LogWarning("VideoServer: Could not find TelemetryServer in scene. Camera selection functionality will be limited.");
            }
        }
        
        // Initial attempt to get TelemetryProvider - may be null at this point
        // Our coroutine will retry later
        if (telemetryServer != null)
        {
            telemetryProvider = telemetryServer.GetTelemetryProvider();
            if (telemetryProvider != null)
            {
                Debug.Log("VideoServer: Connected to TelemetryProvider via TelemetryServer during initialization");
            }
        }
        
        // Set up a fallback camera right away if possible
        Camera attachedCamera = GetComponentInChildren<Camera>();
        if (attachedCamera != null)
        {
            Debug.Log($"VideoServer: Found attached camera: {attachedCamera.name}");
            InitializeCameraManager(attachedCamera, gameObject.GetInstanceID());
        }
    }

    // Initialize a camera manager for a specific ship ID
    private void InitializeCameraManager(Camera camera, int shipId)
    {
        if (camera == null) return;
        
        Debug.Log($"VideoServer: Initializing camera manager for ship ID: {shipId} with camera {camera.name}");
        
        CameraManager manager = new CameraManager(
            camera,
            width: width,
            height: height,
            fps: cameraFps,
            quality: cameraQuality
        );
        
        manager.OnCompressedFrameReady += HandleCameraFrame;
        manager.StartStreaming(shipId);
        
        // Store the camera manager
        if (cameraManagers.ContainsKey(shipId))
        {
            // If we already have a manager for this ID, clean it up first
            cameraManagers[shipId].OnCompressedFrameReady -= HandleCameraFrame;
            cameraManagers[shipId].Cleanup();
        }
        
        cameraManagers[shipId] = manager;
        
        if (currentActiveShipId == -1)
        {
            currentActiveShipId = shipId;
        }
    }

    /// Handle camera request messages from clients
    public void HandleCameraRequest(CameraRequestMessage request, string clientId)
    {
        Debug.Log($"VideoServer: Camera request received from client {clientId} for ship ID: {request.shipId}, enable: {request.enable}");
        
        // Check for telemetryProvider issues first
        if (telemetryProvider == null && request.enable)
        {
            Debug.LogError("VideoServer: TelemetryProvider is null - cannot process camera request");
            
            // Try to get TelemetryProvider one more time
            if (telemetryServer != null)
            {
                Debug.Log("VideoServer: Attempting to get TelemetryProvider again...");
                telemetryProvider = telemetryServer.GetTelemetryProvider();
                
                if (telemetryProvider == null)
                {
                    // Still null, notify client
                    clientManager.SendToClient(clientId, new {
                        type = "camera_request_error",
                        shipId = request.shipId.ToString(),
                        error = "Server error: TelemetryProvider not available"
                    });
                    return;
                }
                else
                {
                    Debug.Log("VideoServer: Successfully recovered TelemetryProvider");
                }
            }
            else
            {
                // No TelemetryServer, notify client
                clientManager.SendToClient(clientId, new {
                    type = "camera_request_error",
                    shipId = request.shipId.ToString(),
                    error = "Server error: No TelemetryServer available"
                });
                return;
            }
        }
        
        try
        {
            if (request.enable)
            {
                // Check if this ship has a camera
                if (telemetryProvider != null && telemetryProvider.HasCameraForStreaming(request.shipId))
                {
                    Camera shipCamera = telemetryProvider.GetCameraForObject(request.shipId);
                    if (shipCamera != null)
                    {
                        // Initialize or get camera manager for this ship
                        if (!cameraManagers.TryGetValue(request.shipId, out CameraManager manager))
                        {
                            // Initialize new camera manager for this ship
                            InitializeCameraManager(shipCamera, request.shipId);
                            manager = cameraManagers[request.shipId];
                        }
                        
                        // Register the client with this camera manager
                        manager.AddClient(clientId);
                        currentActiveShipId = request.shipId;
                        Debug.Log($"VideoServer: Now streaming from ship ID: {request.shipId} for client {clientId}");
                        
                        // Send acknowledgment to the specific client
                        clientManager.SendToClient(clientId, new {
                            type = "camera_request_acknowledged",
                            shipId = request.shipId.ToString(),
                            enabled = true
                        });
                    }
                    else
                    {
                        Debug.LogWarning($"VideoServer: No camera found for ship ID: {request.shipId}");
                        // Send error to client
                        clientManager.SendToClient(clientId, new {
                            type = "camera_request_error",
                            shipId = request.shipId.ToString(),
                            error = "Camera component not found"
                        });
                    }
                }
                else
                {
                    Debug.LogWarning($"VideoServer: Ship ID {request.shipId} doesn't have a ConningCamera child or doesn't exist");
                    // Send error to client
                    clientManager.SendToClient(clientId, new {
                        type = "camera_request_error",
                        shipId = request.shipId.ToString(),
                        error = "Ship doesn't have a camera or doesn't exist"
                    });
                }
            }
            else
            {
                // Disable streaming for this ship for this client
                if (cameraManagers.TryGetValue(request.shipId, out CameraManager manager))
                {
                    // Remove the client from this camera manager
                    manager.RemoveClient(clientId);
                    Debug.Log($"VideoServer: Stopped streaming for ship ID: {request.shipId} for client {clientId}");
                    
                    // If this was the active ship and has no more clients, find another active one
                    if (currentActiveShipId == request.shipId && !manager.HasClients())
                    {
                        currentActiveShipId = -1;
                        foreach (var kvp in cameraManagers)
                        {
                            if (kvp.Key != request.shipId && kvp.Value.HasClients())
                            {
                                currentActiveShipId = kvp.Key;
                                break;
                            }
                        }
                    }
                    
                    // Send acknowledgment to client
                    clientManager.SendToClient(clientId, new {
                        type = "camera_request_acknowledged",
                        shipId = request.shipId.ToString(),
                        enabled = false
                    });
                }
                else
                {
                    // Ship wasn't streaming anyway, but still acknowledge
                    clientManager.SendToClient(clientId, new {
                        type = "camera_request_acknowledged",
                        shipId = request.shipId.ToString(),
                        enabled = false
                    });
                }
            }
        }
        catch (Exception ex)
        {
            // Catch any unexpected errors and report them
            Debug.LogError($"VideoServer: Exception in HandleCameraRequest: {ex.Message}\n{ex.StackTrace}");
            clientManager.SendToClient(clientId, new {
                type = "camera_request_error",
                shipId = request.shipId.ToString(),
                error = $"Server error: {ex.Message}"
            });
        }
    }

    protected override void Update()
    {
        try
        {
            // Call base.Update() which includes clientManager.UpdateClientStates()
            base.Update();
            
            // Update all active camera managers
            foreach (var manager in cameraManagers.Values)
            {
                manager.Update();
            }
        }
        catch (NullReferenceException ex)
        {
            Debug.LogError($"VideoServer.Update: NullReferenceException caught: {ex.Message}");
            
            // Try to repair the server state if possible
            if (serviceHost == null)
            {
                Debug.LogError("VideoServer.Update: serviceHost is null!");
            }
            
            // Check if clientManager is initialized
            if (clientManager == null)
            {
                Debug.LogError("VideoServer.Update: clientManager is null! Re-initializing server components...");
                // Attempt to recreate the core components
                InitializeWebSocketServer();
                if (serviceHost != null)
                {
                    clientManager = new ClientManager(serviceHost);
                    Debug.Log("VideoServer.Update: Successfully recreated clientManager");
                }
            }
        }
    }

    protected override void SendData()
    {
        // Camera frames are sent via event callback
    }

    // Handle client disconnect - stop streaming for all cameras the client was using
    public void HandleClientDisconnect(string clientId)
    {
        Debug.Log($"VideoServer: Handling disconnect for client {clientId}");
        
        // Remove this client from all camera managers
        foreach (var manager in cameraManagers.Values)
        {
            manager.RemoveClient(clientId);
        }
        
        // Update the current active ship ID if necessary
        if (currentActiveShipId != -1 && 
            cameraManagers.TryGetValue(currentActiveShipId, out CameraManager activeManager) && 
            !activeManager.HasClients())
        {
            // Find another active ship
            currentActiveShipId = -1;
            foreach (var kvp in cameraManagers)
            {
                if (kvp.Value.HasClients())
                {
                    currentActiveShipId = kvp.Key;
                    break;
                }
            }
        }
    }
    
    protected override void CleanupResources()
    {
        // Clean up all camera managers
        foreach (var manager in cameraManagers.Values)
        {
            manager.OnCompressedFrameReady -= HandleCameraFrame;
            manager.Cleanup();
        }
        cameraManagers.Clear();
    }
    
    /// <summary>
    /// Handle failures in frame transmission by adapting quality and frame rate
    /// </summary>
    private void HandleTransmissionFailure()
    {
        consecutiveFailures++;
        Debug.LogWarning($"VideoServer: Binary transmission failure #{consecutiveFailures}");
        
        if (consecutiveFailures >= maxConsecutiveFailures)
        {
            // Reset counter
            consecutiveFailures = 0;
            
            // If not already in low bandwidth mode, reduce quality
            if (!usingLowBandwidthMode)
            {
                if (cameraManagers.TryGetValue(currentActiveShipId, out CameraManager manager))
                {
                    // Reduce quality first
                    int newQuality = Mathf.Max(manager.GetJpegQuality() - qualityReductionStep, minQuality);
                    
                    if (newQuality != manager.GetJpegQuality())
                    {
                        Debug.Log($"VideoServer: Reducing JPEG quality to {newQuality} due to transmission failures");
                        manager.SetJpegQuality(newQuality);
                    }
                    else if (manager.GetCaptureInterval() < 1.0f / lowBandwidthFps)
                    {
                        // If we can't reduce quality further, reduce frame rate
                        float newInterval = 1.0f / lowBandwidthFps;
                        Debug.Log($"VideoServer: Switching to low bandwidth mode: {lowBandwidthFps} FPS");
                        manager.SetCaptureInterval(newInterval);
                        usingLowBandwidthMode = true;
                    }
                }
            }
        }
    }

    // Implemented binary frame transmission
    private void HandleCameraFrame(int shipId, byte[] jpegData)
    {
        // Get the camera manager for this ship
        if (!cameraManagers.TryGetValue(shipId, out CameraManager manager) || !manager.HasClients())
        {
            // Skip if there's no manager or no clients for this ship
            return;
        }

        if (!clientManager.HasActiveClients()) {
            Debug.Log("VideoServer: No active clients, skipping broadcast");
            return;
        }

        // Binary frame protocol:
        // 1. A magic number/header to identify this as a camera frame (4 bytes: "VCAM")
        // 2. ShipId (4 bytes)
        // 3. Timestamp (4 bytes)
        // 4. Frame data length (4 bytes)
        // 5. Frame data (variable length)
        
        // Create a binary buffer with header + JPEG data
        byte[] header = System.Text.Encoding.ASCII.GetBytes("VCAM");
        byte[] shipIdBytes = BitConverter.GetBytes(shipId);
        byte[] timestampBytes = BitConverter.GetBytes(Time.time);
        byte[] lengthBytes = BitConverter.GetBytes(jpegData.Length);
        
        // Total message size: 4 (magic) + 4 (shipId) + 4 (timestamp) + 4 (length) + jpegData.Length
        byte[] message = new byte[16 + jpegData.Length];
        
        // Copy header components into message
        Buffer.BlockCopy(header, 0, message, 0, 4);
        Buffer.BlockCopy(shipIdBytes, 0, message, 4, 4);
        Buffer.BlockCopy(timestampBytes, 0, message, 8, 4);
        Buffer.BlockCopy(lengthBytes, 0, message, 12, 4);
        Buffer.BlockCopy(jpegData, 0, message, 16, jpegData.Length);
        
        // Send binary message using the WebSocket API
        try {
            // Update client activity timestamps before sending binary data
            DateTime now = DateTime.UtcNow;
            int activeClientCount = 0;
            
            // Instead of broadcasting to all clients, send only to clients that have requested this ship's camera
            foreach (var session in serviceHost.Sessions.Sessions)
            {
                string clientId = session.ID;
                
                // Check if client exists, is active, and the associated manager has this client
                if (clientManager.GetClientState(clientId) == ClientManager.ConnectionState.Connected)
                {
                    try {
                        // Send binary data and update activity
                        session.Context.WebSocket.SendAsync(message, null);
                        clientManager.UpdateClientActivity(clientId);
                        activeClientCount++;
                    }
                    catch (Exception e) {
                        Debug.LogWarning($"VideoServer: Failed to send binary frame to client {clientId}: {e.Message}");
                    }
                }
            }
            
            if (activeClientCount > 0) {
                Debug.Log($"VideoServer: Binary frame sent for ship ID: {shipId} to {activeClientCount} clients");
                // Reset failure counter on success
                consecutiveFailures = 0;
            } else {
                Debug.LogWarning($"VideoServer: No clients received frame for ship ID: {shipId}");
                
                // Count as a failure for adaptive streaming
                if (enableAdaptiveQuality) {
                    HandleTransmissionFailure();
                }
            }
        } catch (Exception ex) {
            Debug.LogError($"VideoServer: Error sending binary frame: {ex.Message}");
            
            // Count as a failure for adaptive streaming
            if (enableAdaptiveQuality) {
                HandleTransmissionFailure();
            }
            
            // Fallback to JSON if binary transmission fails
            var frameMessage = new CameraFrameMessage {
                type = "camera_frame",
                id = shipId,
                frameData = Convert.ToBase64String(jpegData),
                timestamp = Time.time
            };
            
            // Only send to clients watching this ship
            int successCount = 0;
            foreach (var client in clientManager.GetActiveClientIds())
            {
                if (clientManager.SendToClient(client, frameMessage))
                {
                    successCount++;
                }
            }
            
            Debug.Log($"VideoServer: Fallback to JSON frame: success={successCount > 0} (sent to {successCount} clients)");
        }
    }
}

public class VideoSocketHandler : BaseSocketHandler
{
    private VideoServer videoServer;

    public VideoSocketHandler(VideoServer server)
    {
        this.videoServer = server;
    }

    protected override void OnMessage(MessageEventArgs e)
    {
        base.OnMessage(e);

        try
        {
            // Check the message type first
            var messageType = JsonUtility.FromJson<MessageTypeOnly>(e.Data);
            
            // Check if this is a camera request message
            if (messageType != null && messageType.type == "camera_request")
            {
                var cameraRequest = JsonUtility.FromJson<CameraRequestMessage>(e.Data);
                videoServer.HandleCameraRequest(cameraRequest, ID);
            }
            else
            {
                // Forward to the general message handler
                server.HandleClientMessage(e.Data, ID);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"VideoServer: Error processing video socket message: {ex.Message}");
            server.HandleClientMessage(e.Data, ID);
        }
    }
    
    protected override void OnClose(CloseEventArgs e)
    {
        Debug.Log($"VideoSocketHandler: Client {ID} disconnected");
        
        // Clean up camera managers for this client
        if (videoServer != null)
        {
            videoServer.HandleClientDisconnect(ID);
        }
        
        // Call base to handle standard unregistration
        base.OnClose(e);
    }
}