// MessageHandler.cs - Contains interfaces and base functionality
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SignalingMessage
{
    // Message metadata
    public string type;
    public string shipID;
    public string role;
    public string clientId;

    // Control properties (flattened from the command object)
    public float throttle;
    public float rudder;
    public bool engineOn;

    // Engine group controls
    public float mainThrottle;
    public float mainRudder;
    public float bowThrottle;
    public float bowRudder;
    
    // Camera control
    public float cameraRotation;

    // Control mode
    public string engineMode = "unified";
    
    // ROC control mode toggle (true = ROC controls ship, false = Unity controls ship)
    public bool controlModeActive;
    
    // Legacy field - no longer used with flattened structure
    public object command;
}

public interface IMessageHandler
{
    bool CanHandle(string messageType);
    void HandleMessage(SignalingMessage message, string clientId, IMessageResponder responder);
}

public interface IMessageResponder
{
    void SendToClient(string clientId, object data);
    void BroadcastToAllClients(object data);
    void RegisterClient(string clientId);
    void UnregisterClient(string clientId);
}

public class MessageProcessor
{
    private readonly List<IMessageHandler> handlers = new List<IMessageHandler>();
    private readonly IMessageResponder responder;

    public MessageProcessor(IMessageResponder responder)
    {
        this.responder = responder;
        RegisterDefaultHandlers();
    }

    private void RegisterDefaultHandlers()
    {
        // Classes from MessageHandlerSpecific.cs
        RegisterHandler(new RegisterMessageHandler());
        RegisterHandler(new ControlMessageHandler());
    }

    public void RegisterHandler(IMessageHandler handler)
    {
        handlers.Add(handler);
    }

    public bool ProcessMessage(string message, string clientId)
    {
        try
        {
            var signalMessage = JsonUtility.FromJson<SignalingMessage>(message);

            foreach (var handler in handlers)
            {
                if (handler.CanHandle(signalMessage.type))
                {
                    handler.HandleMessage(signalMessage, clientId, responder);
                    return true;
                }
            }

            Debug.LogWarning($"No handler for message type: {signalMessage.type}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error processing message: {ex.Message}");
            return false;
        }
    }
}