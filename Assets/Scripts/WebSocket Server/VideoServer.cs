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

    // TODO: Migrate this system to binary
    private void HandleCameraFrame(int shipId, string base64Frame)
    {
        if (!clientManager.HasActiveClients()) {
            Debug.Log("No active clients, skipping broadcast");
            return;
        }

        var frameMessage = new CameraFrameMessage {
            type = "camera_frame",
            id = shipId,
            frameData = base64Frame,
            timestamp = Time.time
        };

        bool success = clientManager.BroadcastToAll(frameMessage) > 0;
        Debug.Log($"Frame broadcast success: {success}, length: {base64Frame?.Length ?? 0}");
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