using System;
using System.Collections.Generic;
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


public class VideoServer : BaseSocketServer
{
    [Header("Camera Settings")]
    [SerializeField] private Camera streamCamera;
    [SerializeField] private int cameraQuality = 75;
    [SerializeField] private int cameraFps = 15;
    [SerializeField] private int width = 1280;
    [SerializeField] private int height = 540;
    
    // Adaptive streaming settings
    [Header("Adaptive Streaming")]
    [SerializeField] private bool enableAdaptiveQuality = true;
    [SerializeField] private int maxConsecutiveFailures = 3;
    [SerializeField] private int qualityReductionStep = 10;
    [SerializeField] private int minQuality = 40;
    [SerializeField] private int lowBandwidthFps = 10;
    
    private int consecutiveFailures = 0;
    private bool usingLowBandwidthMode = false;

    private CameraManager cameraManager;

    protected override BaseSocketHandler CreateSocketHandler()
    {
        return new VideoSocketHandler();
    }

    protected override void InitializeComponents()
    {
        cameraManager = new CameraManager(
            streamCamera,
            width: width,
            height: height,
            fps: cameraFps,
            quality: cameraQuality
        );

        cameraManager.OnCompressedFrameReady += HandleCameraFrame;
        cameraManager.StartStreaming(gameObject.GetInstanceID());
    }

    protected override void Update()
    {
        base.Update();
        cameraManager.Update();
    }

    protected override void SendData()
    {
        // Camera frames are sent via event callback
    }

    protected override void CleanupResources()
    {
        if (cameraManager != null)
        {
            cameraManager.OnCompressedFrameReady -= HandleCameraFrame;
            cameraManager.Cleanup();
        }
    }
    
    /// <summary>
    /// Handle failures in frame transmission by adapting quality and frame rate
    /// </summary>
    private void HandleTransmissionFailure()
    {
        consecutiveFailures++;
        Debug.LogWarning($"Binary transmission failure #{consecutiveFailures}");
        
        if (consecutiveFailures >= maxConsecutiveFailures)
        {
            // Reset counter
            consecutiveFailures = 0;
            
            // If not already in low bandwidth mode, reduce quality
            if (!usingLowBandwidthMode)
            {
                // Reduce quality first
                int newQuality = Mathf.Max(cameraManager.GetJpegQuality() - qualityReductionStep, minQuality);
                
                if (newQuality != cameraManager.GetJpegQuality())
                {
                    Debug.Log($"Reducing JPEG quality to {newQuality} due to transmission failures");
                    cameraManager.SetJpegQuality(newQuality);
                }
                else if (cameraManager.GetCaptureInterval() < 1.0f / lowBandwidthFps)
                {
                    // If we can't reduce quality further, reduce frame rate
                    float newInterval = 1.0f / lowBandwidthFps;
                    Debug.Log($"Switching to low bandwidth mode: {lowBandwidthFps} FPS");
                    cameraManager.SetCaptureInterval(newInterval);
                    usingLowBandwidthMode = true;
                }
            }
        }
    }

    // Implemented binary frame transmission
    private void HandleCameraFrame(int shipId, byte[] jpegData)
    {
        if (!clientManager.HasActiveClients()) {
            Debug.Log("No active clients, skipping broadcast");
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
            // This is crucial to prevent the server from thinking clients are inactive
            DateTime now = DateTime.UtcNow;
            int activeClientCount = 0;
            
            // Instead of BroadcastAsync, send to each client individually and update their activity
            foreach (var session in serviceHost.Sessions.Sessions)
            {
                string clientId = session.ID;
                
                // Check if client exists and is active
                if (clientManager.GetClientState(clientId) == ClientManager.ConnectionState.Connected)
                {
                    try {
                        // Send binary data and update activity
                        session.Context.WebSocket.SendAsync(message, null);
                        clientManager.UpdateClientActivity(clientId);
                        activeClientCount++;
                    }
                    catch (Exception e) {
                        Debug.LogWarning($"Failed to send binary frame to client {clientId}: {e.Message}");
                    }
                }
            }
            
            //Debug.Log($"Binary frame sent to {activeClientCount} clients: shipId={shipId}, length={jpegData.Length} bytes");
            Debug.Log($"Binary frame sent");

            // If no active clients receiving binary data, something might be wrong
            if (activeClientCount == 0 && clientManager.HasActiveClients()) {
                Debug.LogWarning("No clients received binary data despite having active clients!");
                
                // Count as a failure for adaptive streaming
                if (enableAdaptiveQuality) {
                    HandleTransmissionFailure();
                }
            } else {
                // Reset failure counter on success
                consecutiveFailures = 0;
            }
        } catch (Exception ex) {
            Debug.LogError($"Error sending binary frame: {ex.Message}");
            
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
            
            bool success = clientManager.BroadcastToAll(frameMessage) > 0;
            Debug.Log($"Fallback to JSON frame: success={success}");
        }
    }
}

public class VideoSocketHandler : BaseSocketHandler
{
    protected override void OnMessage(MessageEventArgs e)
    {
        server.HandleClientMessage(e.Data, ID);
    }
    // Video-specific message handling if needed
}