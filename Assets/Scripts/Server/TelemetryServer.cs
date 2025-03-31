using System.Collections.Generic;
using UnityEngine;

public class TelemetryServer : BaseSocketServer
{
    [Header("Telemetry Settings")]
    [SerializeField] private float telemetryUpdateRate = 0.1f;

    [Header("Object Discovery Settings")]
    [SerializeField] private bool autoDiscoverObjects = true;
    [SerializeField] private string objectNamePattern = "Ship_"; // Pattern to search for objects

    private TelemetryProvider telemetryProvider;
    private float lastTelemetryTime = 0;

    protected override BaseSocketHandler CreateSocketHandler()
    {
        return new TelemetrySocketHandler();
    }

    // Override Start method to ensure initialization happens in the right order
    protected override void Start()
    {
        Debug.Log("TelemetryServer: Start method called");
        
        // Call base implementation first to initialize server components
        base.Start();
        
        // Double-check that telemetryProvider was properly initialized
        if (telemetryProvider == null)
        {
            Debug.LogWarning("TelemetryServer: telemetryProvider is still null after Start, re-initializing components");
            InitializeComponents();
            
            // Log error if still null after re-initialization
            if (telemetryProvider == null)
            {
                Debug.LogError("TelemetryServer: Failed to initialize telemetryProvider in Start method!");
            }
            else
            {
                Debug.Log($"TelemetryServer: Successfully initialized telemetryProvider with {telemetryProvider.GetObjectCount()} objects");
            }
        }
    }
    
    protected override void InitializeComponents()
    {
        Debug.Log("TelemetryServer: InitializeComponents called");
        
        try
        {
            // Create a new telemetry provider
            telemetryProvider = new TelemetryProvider();
            
            // Discover objects based on settings
            if (autoDiscoverObjects)
            {
                if (!string.IsNullOrEmpty(objectNamePattern))
                {
                    Debug.Log($"TelemetryServer: Adding objects by naming convention: {objectNamePattern}");
                    telemetryProvider.AddObjectsByNamingConvention(objectNamePattern);
                }
                
                // Add self as a fallback if using naming pattern (will be skipped if it has server components)
                bool isShip = gameObject.name.Contains("Ship");
                string objType = isShip ? "ship" : "structure";
                Debug.Log($"TelemetryServer: Attempting to add self as telemetry object: {gameObject.name}");
                telemetryProvider.AddTelemetryObject(gameObject, isShip, objType);
            }
            
            // Log discovered objects
            int objectCount = telemetryProvider.GetObjectCount();
            Debug.Log($"TelemetryServer: Initialized with {objectCount} objects");
            telemetryProvider.LogAllObjects();
            
            // Warning if no objects were found
            if (objectCount == 0)
            {
                Debug.LogWarning("TelemetryServer: No telemetry objects were discovered! Check naming pattern or scene hierarchy.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"TelemetryServer: Exception during initialization: {ex.Message}\n{ex.StackTrace}");
            telemetryProvider = null;
        }
        
        // Double-check the initialization result
        if (telemetryProvider == null)
        {
            Debug.LogError("TelemetryServer: Failed to initialize telemetryProvider!");
        }
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
        // Clear telemetry objects on cleanup
        if (telemetryProvider != null)
        {
            telemetryProvider.ClearAllObjects();
        }
    }

    private void SendTelemetryData()
    {
        if (!clientManager.HasActiveClients()) return;

        // Check if telemetryProvider is null
        if (telemetryProvider == null)
        {
            Debug.LogError("TelemetryServer.SendTelemetryData: telemetryProvider is null! Reinitializing components...");
            InitializeComponents();
            
            // Double-check after initialization attempt
            if (telemetryProvider == null)
            {
                Debug.LogError("TelemetryServer.SendTelemetryData: Failed to initialize telemetryProvider! Skipping telemetry broadcast.");
                return;
            }
        }

        List<TelemetryData> allTelemetryData = telemetryProvider.GenerateAllTelemetry();
        
        foreach (var telemetryData in allTelemetryData)
        {
            Debug.Log($"Broadcasting telemetry for {telemetryData.name} (ID: {telemetryData.id})");
            clientManager.BroadcastToAll(telemetryData);
        }
    }
    
    // Method to handle incoming control messages from clients
    public void HandleControlMessage(string shipId, string command, object[] parameters)
    {
        if (int.TryParse(shipId, out int id))
        {
            // Forward control commands to the appropriate ship if it exists and is controllable
            var obj = telemetryProvider.GetTelemetryObjectById(id);
            if (obj != null && obj.isShip)
            {
                // TODO: Implement control command handling
                Debug.Log($"Control command {command} received for ship {obj.name} (ID: {id})");
                
                // Forward command to the ship controller if available
                //var controller = obj.gameObject.GetComponent<ShipController2>();
                //if (controller != null)
                {
                    // Call appropriate controller methods based on command
                    // This will need to be implemented based on what commands are supported
                }
            }
            else
            {
                Debug.LogWarning($"Received control command for unknown or non-controllable object ID: {id}");
            }
        }
    }
    
    // Expose the TelemetryProvider to other components
    public TelemetryProvider GetTelemetryProvider()
    {
        if (telemetryProvider == null)
        {
            Debug.LogError("TelemetryServer.GetTelemetryProvider: telemetryProvider is null! Attempting to initialize...");
            
            // Try to initialize if it's null
            InitializeComponents();
            
            // Check if initialization was successful
            if (telemetryProvider == null)
            {
                Debug.LogError("TelemetryServer.GetTelemetryProvider: Failed to initialize telemetryProvider!");
                return null;
            }
        }
        
        int objectCount = telemetryProvider.GetObjectCount();
        if (objectCount == 0)
        {
            Debug.LogWarning("TelemetryServer.GetTelemetryProvider: Returning provider with 0 objects - no telemetry data will be sent");
        }
        else
        {
            Debug.Log($"TelemetryServer.GetTelemetryProvider: Returning provider with {objectCount} objects");
        }
        
        return telemetryProvider;
    }
}

public class TelemetrySocketHandler : BaseSocketHandler
{
    // Implement message handling for telemetry-specific commands
    protected override void OnMessage(WebSocketSharp.MessageEventArgs e)
    {
        base.OnMessage(e);
        
        // Handle any telemetry-specific messages here
        // For example, you might receive a request to focus on a specific ship
    }
}