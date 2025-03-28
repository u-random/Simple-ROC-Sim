// MessageHandler.cs - Contains interfaces and base functionality
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SignalingMessage
{
    public string type;
    public string shipId;
    public string role;
    public string clientId; // UNUSED
    public object command;  // UNUSED
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