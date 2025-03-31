// MessageHandlers.cs - Contains concrete handler implementations
using UnityEngine;
using System;

public class RegisterMessageHandler : IMessageHandler {
    public bool CanHandle(string messageType) => messageType == "register";

    public void HandleMessage(SignalingMessage message, string clientId, IMessageResponder responder) {
        Debug.Log($"Registering client {clientId} as {message.role}");
        responder.RegisterClient(clientId);

        // TODO: No client side usage
        responder.SendToClient(clientId, new {
            type = "registration_confirmed",
            clientId = clientId
        });
    }
}

public class ControlMessageHandler : IMessageHandler {
    public bool CanHandle(string messageType) => messageType == "control";

    public void HandleMessage(SignalingMessage message, string clientId, IMessageResponder responder) {
        Debug.Log($"Processing control command for ship {message.shipID}");
        
        // Since message itself now contains all the control data, pass the entire message
        // This lets us avoid using the nested command property
        ControlEventSystem.PublishControlCommand(message.shipID, message);

        // TODO: No client side usage
        responder.SendToClient(clientId, new {
            type = "control_acknowledged",
            shipId = message.shipID
        });
    }
}

public class HeartbeatResponseHandler : IMessageHandler {
    public bool CanHandle(string messageType) => messageType == "heartbeat_response";

    public void HandleMessage(SignalingMessage message, string clientId, IMessageResponder responder) {
        // Simply log at verbose level and update client activity
        // No need to send a response back to the client
        if (Debug.isDebugBuild) {
            Debug.Log($"Received heartbeat response from client {clientId}");
        }
        
        // The BaseSocketServer/ClientManager will automatically update the client's
        // activity timestamp when processing the message
    }
}

public class PingMessageHandler : IMessageHandler {
    public bool CanHandle(string messageType) => messageType == "ping";

    public void HandleMessage(SignalingMessage message, string clientId, IMessageResponder responder) {
        // Respond with a pong message that includes the original timestamp
        long receivedTimestamp = message.timestamp;
        long currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        if (Debug.isDebugBuild) {
            Debug.Log($"Received ping from client {clientId}. Sending pong response");
        }
        
        // Send a pong response
        responder.SendToClient(clientId, new {
            type = "pong",
            originalTimestamp = receivedTimestamp,
            timestamp = currentTimestamp
        });
    }
}

public class CameraRequestMessageHandler : IMessageHandler {
    public bool CanHandle(string messageType) => messageType == "camera_request";

    public void HandleMessage(SignalingMessage message, string clientId, IMessageResponder responder) {
        Debug.Log($"Processing camera request for ship {message.shipID}");
        
        // Create a camera request message and publish it to the video server
        CameraRequestMessage cameraRequest = new CameraRequestMessage {
            shipId = int.Parse(message.shipID),
            enable = message.enable
        };
        
        // Find the VideoServer in the scene and call its handler
        VideoServer videoServer = GameObject.FindObjectOfType<VideoServer>();
        if (videoServer != null) {
            videoServer.HandleCameraRequest(cameraRequest, clientId);
            
            // Send acknowledgment to client
            responder.SendToClient(clientId, new {
                type = "camera_request_acknowledged",
                shipId = message.shipID,
                enabled = message.enable
            });
        } else {
            Debug.LogError("Could not find VideoServer in scene for camera request");
            
            // Send error to client
            responder.SendToClient(clientId, new {
                type = "camera_request_error",
                shipId = message.shipID,
                error = "VideoServer not available"
            });
        }
    }
}