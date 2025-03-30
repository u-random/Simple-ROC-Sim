using System.Collections.Generic;
using UnityEngine;
using System;

public class CameraManager
{
    private Camera targetCamera;
    private RenderTexture renderTexture;
    private Texture2D frameTexture;
    private bool isStreaming = false;
    private float lastCaptureTime = 0;
    private int currentShipId = 0;
    private Dictionary<int, string> frameCache = new Dictionary<int, string>();

    // Configuration
    private int streamWidth = 1280;
    private int streamHeight = 540;
    private float captureInterval; // seconds between frames
    private int jpegQuality = 75;

    // Event to notify when a new frame is ready
    public event Action<int, byte[]> OnCompressedFrameReady;

    public CameraManager(Camera camera = null, int width = 1280, int height = 540, int fps = 15, int quality = 75)
    {
        targetCamera = camera ?? Camera.main;
        streamWidth = width;
        streamHeight = height;
        captureInterval = 1f / fps;
        jpegQuality = quality;

        Initialize();
    }
    
    // Accessors for adaptive streaming
    public int GetJpegQuality() { return jpegQuality; }
    public void SetJpegQuality(int quality) { jpegQuality = Mathf.Clamp(quality, 1, 100); }
    
    public float GetCaptureInterval() { return captureInterval; }
    public void SetCaptureInterval(float interval) { captureInterval = Mathf.Max(0.033f, interval); } // Minimum 30fps

    private void Initialize()
    {
        Debug.Log("Initializing camera manager...");

        if (targetCamera == null)
        {
            Debug.LogError("No camera found for streaming!");
            return;
        }

        // Create render texture
        renderTexture = new RenderTexture(streamWidth, streamHeight, 24);
        renderTexture.Create();

        // Create texture for frame capture
        frameTexture = new Texture2D(streamWidth, streamHeight, TextureFormat.RGB24, false);

        Debug.Log($"Camera manager initialized with camera {targetCamera.name}");
    }

    public void StartStreaming(int shipId)
    {
        if (isStreaming) return;

        Debug.Log($"Starting camera streaming for ship {shipId}");
        currentShipId = shipId;
        targetCamera.targetTexture = renderTexture;
        isStreaming = true;
    }

    public void StopStreaming()
    {
        if (!isStreaming) return;

        Debug.Log("Stopping camera streaming");
        isStreaming = false;

        if (targetCamera != null)
        {
            targetCamera.targetTexture = null;
        }
    }

    // Call this from WebSocketManager's Update method
    public void Update()
    {
        if (!isStreaming) return;

        if (Time.time - lastCaptureTime >= captureInterval)
        {
            CaptureFrame();
            lastCaptureTime = Time.time;
        }
    }

    // Migrated to binary data
    private void CaptureFrame()
    {
        //Debug.Log($"CaptureFrame called: isStreaming={isStreaming}, camera={targetCamera != null}, RT={renderTexture != null}");

        if (!isStreaming || targetCamera == null || renderTexture == null || frameTexture == null)
            return;

        try
        {
            targetCamera.targetTexture = renderTexture;
            targetCamera.Render();
            RenderTexture.active = renderTexture;
            frameTexture.ReadPixels(new Rect(0, 0, streamWidth, streamHeight), 0, 0);
            frameTexture.Apply();
            RenderTexture.active = null;

            // Convert to JPEG binary data only
            byte[] jpegBytes = frameTexture.EncodeToJPG(jpegQuality);
            
            // For caching/debug purposes, still maintain base64 version
            //string base64Frame = Convert.ToBase64String(jpegBytes);
            //frameCache[currentShipId] = base64Frame;

            //Debug.Log($"Frame captured: shipId={currentShipId}, binary length={jpegBytes.Length}bytes");
            Debug.Log($"Frame captured for shipId={currentShipId}");
            // Notify listeners with binary data instead of base64
            OnCompressedFrameReady?.Invoke(currentShipId, jpegBytes);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error capturing camera frame: {ex.Message}");
        }
    }

    // Get the latest frame for a ship
    public string GetLatestFrame(int shipId)
    {
        if (frameCache.TryGetValue(shipId, out string frame))
        {
            return frame;
        }
        return null;
    }

    public void Cleanup()
    {
        StopStreaming();

        if (renderTexture != null)
        {
            renderTexture.Release();
            renderTexture = null;
        }

        frameTexture = null;
        frameCache.Clear();
    }
}