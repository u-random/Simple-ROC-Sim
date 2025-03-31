using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class TelemetryData
{
    public string type = "telemetry";
    public int id;
    public string name;
    public bool isShip = true; // Flag to indicate if this is a ship (controllable) or other object
    public string objectType = "ship"; // Type of object (ship, buoy, structure, etc.)
    public bool hasCamera = false; // Flag to indicate if this object has a camera for streaming

    [Serializable]
    public class PositionData
    {
        public double longitude;
        public double latitude;
    }

    [Serializable]
    public class MotionData
    {
        public float heading;
        public float speed;
        public float course;
    }

    [Serializable]
    public class ConnectionData
    {
        public bool connected = true;
        public long lastUpdated;
        public float signalStrength = 100;
    }

    [Serializable]
    public class TelemetryValues
    {
        public float rpm;
        public float fuelLevel;
    }

    public PositionData position;
    public MotionData motion;
    public string status = "active";
    public ConnectionData connection;
    public TelemetryValues telemetry;
    public string cameraFeed;
}

// Class representing an object that can generate telemetry
public class TelemetryObject
{
    public GameObject gameObject;
    public Transform transform;
    public Rigidbody rigidbody;
    public int id;
    public string name;          // Original object name
    public string displayName;   // Parsed display name (without prefixes)
    public bool isShip;
    public string objectType;
    public Transform trueForwardTransform;
    
    public TelemetryObject(GameObject obj, bool isShip = true, string objectType = "ship")
    {
        this.gameObject = obj;
        this.transform = obj.transform;
        this.rigidbody = obj.GetComponent<Rigidbody>();
        this.id = obj.GetInstanceID();
        this.name = obj.name;
        this.displayName = obj.name; // Default to original name, will be updated by AddTelemetryObject
        this.isShip = isShip;
        this.objectType = objectType;
        this.trueForwardTransform = obj.transform.Find("TrueForward");
    }
}

public class TelemetryProvider
{
    private List<TelemetryObject> telemetryObjects = new List<TelemetryObject>();
    
    // Default constructor - no specific objects added yet
    public TelemetryProvider() { }
    
    // Add a single telemetry object
    public void AddTelemetryObject(GameObject obj, bool isShip = true, string objectType = "ship")
    {
        // Skip adding the server game object itself
        if (obj.GetComponent<TelemetryServer>() != null || obj.GetComponent<VideoServer>() != null)
        {
            Debug.Log($"TelemetryProvider: Skipping server GameObject: {obj.name}");
            return;
        }
        
        // Parse the name to strip prefixes like "Ship_" or "Object_"
        string displayName = ParseObjectName(obj.name);
        
        TelemetryObject telObj = new TelemetryObject(obj, isShip, objectType);
        telObj.displayName = displayName; // Set the parsed display name
        telemetryObjects.Add(telObj);
        Debug.Log($"TelemetryProvider: Added telemetry object: {obj.name}, display name: {displayName}, isShip: {isShip}, type: {objectType}");
    }
    
    // Parse object name to remove prefixes like "Ship_" or "Object_"
    private string ParseObjectName(string originalName)
    {
        // Define common prefixes to remove
        string[] prefixes = { "Ship_", "Object_", "Structure_", "Buoy_" };
        
        foreach (var prefix in prefixes)
        {
            if (originalName.StartsWith(prefix))
            {
                return originalName.Substring(prefix.Length);
            }
        }
        
        // If no prefix matches, return the original name
        return originalName;
    }
    
    // Remove a telemetry object by ID
    public void RemoveTelemetryObject(int id)
    {
        telemetryObjects.RemoveAll(obj => obj.id == id);
    }
    
    // Clear all tracked objects
    public void ClearAllObjects()
    {
        telemetryObjects.Clear();
    }
    
    // Find objects by naming convention and add them
    public void AddObjectsByNamingConvention(string pattern)
    {
        GameObject[] allObjects = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        
        foreach (var obj in allObjects)
        {
            // Skip any objects with server components
            if (obj.GetComponent<TelemetryServer>() != null || 
                obj.GetComponent<VideoServer>() != null)
            {
                continue;
            }
            
            if (obj.name.StartsWith(pattern))
            {
                // Determine object type based on name
                bool isShip = false;
                string objType = "structure";
                
                if (obj.name.Contains("Ship"))
                {
                    isShip = true;
                    objType = "ship";
                }
                else if (obj.name.Contains("Buoy"))
                {
                    objType = "buoy";
                }
                else if (obj.name.Contains("Dock"))
                {
                    objType = "dock";
                }
                
                AddTelemetryObject(obj, isShip, objType);
            }
        }
    }
    
    // Add all children of a parent as telemetry objects
    public void AddChildrenOfParent(Transform parent)
    {
        foreach (Transform child in parent)
        {
            // Skip any objects with server components
            if (child.GetComponent<TelemetryServer>() != null || 
                child.GetComponent<VideoServer>() != null)
            {
                continue;
            }
            
            bool isShip = child.name.Contains("Ship");
            string objType = isShip ? "ship" : "structure";
            
            if (child.name.Contains("Buoy"))
            {
                objType = "buoy";
            }
            else if (child.name.Contains("Dock"))
            {
                objType = "dock";
            }
            
            AddTelemetryObject(child.gameObject, isShip, objType);
        }
    }

    // Generate telemetry data for all tracked objects
    public List<TelemetryData> GenerateAllTelemetry()
    {
        List<TelemetryData> allTelemetry = new List<TelemetryData>();
        
        foreach (var obj in telemetryObjects)
        {
            allTelemetry.Add(GenerateTelemetryForObject(obj));
        }
        
        return allTelemetry;
    }

    // Generate telemetry data for a single object
    private TelemetryData GenerateTelemetryForObject(TelemetryObject obj)
    {
        // Convert Unity position to geographic coordinates
        Vector3 position = obj.transform.position;
        double[] geoCoords = TelemetryUtilities.UnityToGeo(position);
        double longitude = geoCoords[0];
        double latitude = geoCoords[1];

        float heading = TelemetryUtilities.GetShipHeadingWithMarker(obj.transform, obj.trueForwardTransform);
        Vector3 horizontalVelocity = obj.rigidbody != null ?
            new Vector3(obj.rigidbody.linearVelocity.x, 0, obj.rigidbody.linearVelocity.z) : Vector3.zero;
        float speedKnots = obj.rigidbody != null ?
            TelemetryUtilities.UnitySpeedToKnots(horizontalVelocity.magnitude) : 0;

        // Check if object has a camera for streaming
        bool hasCamera = obj.transform.Find("ConningCamera") != null;

        return new TelemetryData
        {
            id = obj.id,
            name = obj.displayName, // Use the parsed display name instead of the original name
            isShip = obj.isShip,
            objectType = obj.objectType,
            hasCamera = hasCamera, // Set whether this object has a camera
            position = new TelemetryData.PositionData { longitude = longitude, latitude = latitude },
            motion = new TelemetryData.MotionData
            {
                heading = heading,
                speed = speedKnots,
                course = heading
            },
            status = "active",
            connection = new TelemetryData.ConnectionData
            {
                connected = true,
                lastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                signalStrength = 100
            },
            telemetry = new TelemetryData.TelemetryValues { 
                rpm = obj.isShip ? 1200 : 0, 
                fuelLevel = obj.isShip ? 85 : 0 
            }
        };
    }
    
    // Get a telemetry object by ID
    public TelemetryObject GetTelemetryObjectById(int id)
    {
        return telemetryObjects.Find(obj => obj.id == id);
    }
    
    // Check if an object has a camera for streaming
    public bool HasCameraForStreaming(int id)
    {
        var obj = GetTelemetryObjectById(id);
        if (obj == null) 
        {
            Debug.LogWarning($"TelemetryProvider.HasCameraForStreaming: No object found with ID {id}");
            return false;
        }
        
        // Check if it has a ConningCamera child
        Transform cameraTransform = obj.transform.Find("ConningCamera");
        if (cameraTransform == null)
        {
            Debug.Log($"TelemetryProvider.HasCameraForStreaming: Object '{obj.name}' (ID: {id}) does not have a ConningCamera child");
            // Try to find any camera on this object
            Camera anyCamera = obj.gameObject.GetComponentInChildren<Camera>();
            if (anyCamera != null)
            {
                Debug.Log($"TelemetryProvider.HasCameraForStreaming: Object '{obj.name}' has a camera '{anyCamera.name}', but it's not named 'ConningCamera'");
            }
            return false;
        }
        
        // Check if it has a Camera component
        Camera camera = cameraTransform.GetComponent<Camera>();
        if (camera == null)
        {
            Debug.LogWarning($"TelemetryProvider.HasCameraForStreaming: Object '{obj.name}' has ConningCamera child but it has no Camera component");
            return false;
        }
        
        Debug.Log($"TelemetryProvider.HasCameraForStreaming: Object '{obj.name}' (ID: {id}) has valid camera for streaming");
        return true;
    }
    
    // Get the camera for a specific object if it exists
    public Camera GetCameraForObject(int id)
    {
        var obj = GetTelemetryObjectById(id);
        if (obj == null) 
        {
            Debug.LogWarning($"TelemetryProvider.GetCameraForObject: No object found with ID {id}");
            return null;
        }
        
        Transform cameraTransform = obj.transform.Find("ConningCamera");
        if (cameraTransform == null)
        {
            Debug.LogWarning($"TelemetryProvider.GetCameraForObject: Object {obj.name} has no ConningCamera child");
            return null;
        }
        
        Camera camera = cameraTransform.GetComponent<Camera>();
        if (camera == null)
        {
            Debug.LogWarning($"TelemetryProvider.GetCameraForObject: ConningCamera on {obj.name} has no Camera component");
        }
        else
        {
            Debug.Log($"TelemetryProvider.GetCameraForObject: Found camera on {obj.name}");
        }
        
        return camera;
    }
    
    // Get the number of objects tracked by this provider
    public int GetObjectCount()
    {
        return telemetryObjects.Count;
    }
    
    // Debug method to log all tracked objects
    public void LogAllObjects()
    {
        Debug.Log($"TelemetryProvider: has {telemetryObjects.Count} objects:");
        foreach (var obj in telemetryObjects)
        {
            Debug.Log($"TelemetryProvider: - {obj.name} (ID: {obj.id}, Type: {obj.objectType}, IsShip: {obj.isShip})");
            Debug.Log($"TelemetryProvider: Has Camera: {obj.transform.Find("ConningCamera") != null}");
        }
    }
}