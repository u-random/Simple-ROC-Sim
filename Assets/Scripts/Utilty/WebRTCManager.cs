using UnityEngine;
using Unity.WebRTC;
using System.Collections;
using System.Collections.Generic;
using WebSocketSharp;

// This is the Unity-side Publisher of the WebRTC pipeline for the camera feed

public class WebRTCStreamManager : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private int streamWidth = 2560;
    [SerializeField] private int streamHeight = 1080;
    [SerializeField] private string signalingServerUrl = "ws://localhost:3000";

    private RTCPeerConnection peerConnection;
    private WebSocket websocket;
    private MediaStream videoStream;
    private RenderTexture renderTexture;
    private VideoStreamTrack videoTrack;

    // Define serializable classes for signaling messages
    [System.Serializable]
    private class SignalingMessage
    {
        public string type;
        public string sdp;
        public string role;
        public IceCandidate candidate;
    }

    [System.Serializable]
    private class IceCandidate
    {
        public string candidate;
        public string sdpMid;
        public int sdpMLineIndex;
    }

    private void Start()
    {
        if (!targetCamera)
        {
            Debug.LogError("Target camera not assigned!");
            return;
        }

        // Initialize WebRTC when the component starts
        StartCoroutine(InitializeWebRTC());
    }

    private IEnumerator InitializeWebRTC()
    {
        // Properly initialize WebRTC in Unity 6
        // No need to call WebRTC.Initialize() explicitly in newer versions
        yield return new WaitForEndOfFrame();

        // Create render texture for the camera
        renderTexture = new RenderTexture(streamWidth, streamHeight, 24);
        renderTexture.Create();
        targetCamera.targetTexture = renderTexture;

        // Configure RTCConfiguration with STUN server
        var config = new RTCConfiguration
        {
            iceServers = new RTCIceServer[]
            {
                new RTCIceServer { urls = new string[] { "stun:stun.l.google.com:19302" } }
            }
        };

        // Sets up the WebRTC peer connection and handles ICE candidates
        peerConnection = new RTCPeerConnection(ref config);
        SetupPeerConnectionCallbacks();

        // Connect to signaling server for WebRTC handshake
        ConnectToSignalingServer();

        // Create video track from camera
        // Converts the texture into a streamable format
        videoTrack = new VideoStreamTrack(renderTexture);

        // Add track to peer connection
        peerConnection.AddTrack(videoTrack);

        Debug.Log("WebRTC initialized successfully");
    }

    private void SetupPeerConnectionCallbacks()
    {
        peerConnection.OnIceCandidate = candidate =>
        {
            if (candidate == null) return;

            Debug.Log($"Generated ICE candidate: {candidate.Candidate}");

            var candidateMsg = new SignalingMessage
            {
                type = "candidate",
                candidate = new IceCandidate
                {
                    candidate = candidate.Candidate,
                    sdpMid = candidate.SdpMid,
                    // Fix for type conversion - explicitly cast to int
                    sdpMLineIndex = candidate.SdpMLineIndex.HasValue ? candidate.SdpMLineIndex.Value : 0
                }
            };

            SendSignalingMessage(candidateMsg);
        };

        peerConnection.OnNegotiationNeeded = () =>
        {
            Debug.Log("Negotiation needed");
            StartCoroutine(CreateAndSendOffer());
        };

        peerConnection.OnConnectionStateChange = state =>
        {
            Debug.Log($"Connection state changed to: {state}");
        };

        peerConnection.OnIceConnectionChange = state =>
        {
            Debug.Log($"ICE connection state changed to: {state}");
        };
    }

    private void ConnectToSignalingServer()
    {
        websocket = new WebSocket(signalingServerUrl);

        websocket.OnOpen += (sender, e) =>
        {
            Debug.Log("Connected to signaling server");
            SendSignalingMessage(new SignalingMessage { type = "register", role = "publisher" });
        };

        websocket.OnMessage += (sender, e) =>
        {
            try
            {
                Debug.Log($"Received message: {e.Data}");
                var message = JsonUtility.FromJson<SignalingMessage>(e.Data);

                // Process message on the main thread
                UnityMainThreadDispatcher.Instance().Enqueue(() => {
                    HandleSignalingMessage(message);
                });
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to parse signaling message: {ex.Message}");
            }
        };

        websocket.OnError += (sender, e) =>
        {
            Debug.LogError($"WebSocket error: {e.Message}");
        };

        websocket.OnClose += (sender, e) =>
        {
            Debug.Log($"WebSocket closed: {e.Code} - {e.Reason}");
        };

        websocket.Connect();
    }

    private IEnumerator CreateAndSendOffer()
    {
        Debug.Log("Creating offer...");

        // Create the offer
        RTCSessionDescriptionAsyncOperation op = peerConnection.CreateOffer();
        yield return op;

        if (op.IsError)
        {
            Debug.LogError($"Offer creation error: {op.Error.message}");
            yield break;
        }

        RTCSessionDescription desc = op.Desc;
        Debug.Log($"Offer created: {desc.sdp}");

        // Set local description (our own offer)
        RTCSetSessionDescriptionAsyncOperation setLocalOp = peerConnection.SetLocalDescription(ref desc);
        yield return setLocalOp;

        if (setLocalOp.IsError)
        {
            Debug.LogError($"Set local description error: {setLocalOp.Error.message}");
            yield break;
        }

        // Send offer to signaling server
        Debug.Log("Sending offer to remote peer");
        SendSignalingMessage(new SignalingMessage { type = "offer", sdp = desc.sdp });
    }

    private void HandleSignalingMessage(SignalingMessage message)
    {
        if (message == null || string.IsNullOrEmpty(message.type)) return;

        Debug.Log($"Handling message type: {message.type}");

        switch (message.type)
        {
            case "answer":
                if (!string.IsNullOrEmpty(message.sdp))
                {
                    Debug.Log("Received answer, setting remote description");
                    StartCoroutine(HandleAnswer(message.sdp));
                }
                break;

            case "candidate":
                if (message.candidate != null)
                {
                    Debug.Log($"Received ICE candidate: {message.candidate.candidate}");
                    var candidateInit = new RTCIceCandidateInit
                    {
                        candidate = message.candidate.candidate,
                        sdpMid = message.candidate.sdpMid,
                        sdpMLineIndex = message.candidate.sdpMLineIndex
                    };

                    RTCIceCandidate candidate = new RTCIceCandidate(candidateInit);
                    peerConnection.AddIceCandidate(candidate);
                }
                break;

            case "request-offer":
                Debug.Log("Received request for offer, creating new offer");
                StartCoroutine(CreateAndSendOffer());
                break;
        }
    }

    private IEnumerator HandleAnswer(string sdp)
    {
        var desc = new RTCSessionDescription
        {
            type = RTCSdpType.Answer,
            sdp = sdp
        };

        Debug.Log("Setting remote description from answer");
        var op = peerConnection.SetRemoteDescription(ref desc);
        yield return op;

        if (op.IsError)
        {
            Debug.LogError($"Set remote description error: {op.Error.message}");
        }
        else
        {
            Debug.Log("Remote description set successfully");
        }
    }

    private void SendSignalingMessage(SignalingMessage message)
    {
        if (websocket?.ReadyState == WebSocketState.Open)
        {
            string json = JsonUtility.ToJson(message);
            Debug.Log($"Sending signaling message: {json}");
            websocket.Send(json);
        }
        else
        {
            Debug.LogWarning("WebSocket not connected, message not sent");
        }
    }

    private void OnDestroy()
    {
        Debug.Log("Cleaning up WebRTC resources");

        if (peerConnection != null)
        {
            peerConnection.Close();
            peerConnection.Dispose();
            peerConnection = null;
        }

        if (videoTrack != null)
        {
            videoTrack.Dispose();
            videoTrack = null;
        }

        if (renderTexture != null)
        {
            renderTexture.Release();
            renderTexture = null;
        }

        if (websocket != null && websocket.ReadyState == WebSocketState.Open)
        {
            websocket.Close();
            websocket = null;
        }

        // WebRTC.Dispose() is not needed in newer versions
    }
}

// Helper class to run code on the main thread
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance;
    private readonly Queue<System.Action> _executionQueue = new Queue<System.Action>();
    private readonly object _lock = new object();

    public static UnityMainThreadDispatcher Instance()
    {
        if (_instance == null)
        {
            var go = new GameObject("UnityMainThreadDispatcher");
            _instance = go.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(go);
        }
        return _instance;
    }

    public void Enqueue(System.Action action)
    {
        lock (_lock)
        {
            _executionQueue.Enqueue(action);
        }
    }

    void Update()
    {
        lock (_lock)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }
}