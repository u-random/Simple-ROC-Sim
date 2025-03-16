// MessageHandlers.cs - Contains concrete handler implementations
using UnityEngine;

public class RegisterMessageHandler : IMessageHandler {
    public bool CanHandle(string messageType) => messageType == "register";

    public void HandleMessage(SignalingMessage message, string clientId, IMessageResponder responder) {
        Debug.Log($"Registering client {clientId} as {message.role}");
        responder.RegisterClient(clientId);

        responder.SendToClient(clientId, new {
            type = "registration_confirmed",
            clientId = clientId
        });
    }
}

public class ControlMessageHandler : IMessageHandler {
    public bool CanHandle(string messageType) => messageType == "control";

    public void HandleMessage(SignalingMessage message, string clientId, IMessageResponder responder) {
        Debug.Log($"Processing control command for ship {message.shipId}");

        // Process control command

        responder.SendToClient(clientId, new {
            type = "control_acknowledged",
            shipId = message.shipId
        });
    }
}