using UnityEngine;


// TODO: Abstract gameobject to handle several candidates


public class TelemetryServer : BaseSocketServer
{
    [Header("Telemetry Settings")]
    [SerializeField] protected string shipType = "Demo Ship"; // TODO: Use enum with a selector
    [SerializeField] private float telemetryUpdateRate = 0.1f;

    private TelemetryProvider telemetryProvider;
    private float lastTelemetryTime = 0;

    //private ControlState currentControlState = new ControlState();
    private Rigidbody shipRigidbody;

    protected override BaseSocketHandler CreateSocketHandler()
    {
        return new TelemetrySocketHandler();
    }

    protected override void InitializeComponents()
    {
        telemetryProvider = new TelemetryProvider(transform, GetComponent<Rigidbody>());
        shipRigidbody = GetComponent<Rigidbody>();
    }

    protected override void SendData()
    {
        if (Time.time - lastTelemetryTime > telemetryUpdateRate)
        {
            SendTelemetryData();
            lastTelemetryTime = Time.time;
        }
    }

    protected override void CleanupResources()
    {
        // No specific resources to clean up
    }

    private void SendTelemetryData()
    {
        if (!clientManager.HasActiveClients()) return;

        var telemetryData = telemetryProvider.GenerateTelemetry();
        //Debug.Log("Broadcasting telemetry:" + telemetryData.id);
        clientManager.BroadcastToAll(telemetryData);
    }
}

public class TelemetrySocketHandler : BaseSocketHandler
{
    // Telemetry-specific message handling if needed
}