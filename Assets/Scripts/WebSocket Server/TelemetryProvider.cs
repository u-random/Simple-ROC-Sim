using System;
using UnityEngine;

[Serializable]
public class TelemetryData
{
    public string type = "telemetry";
    public int id;
    public string name;

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

public class TelemetryProvider
{
    private readonly Transform shipTransform;
    private readonly Rigidbody shipRigidbody;
    private readonly int shipId;
    private readonly string shipName;

    // Constructor with dependencies
    public TelemetryProvider(Transform transform, Rigidbody rigidbody, string name = null)
    {
        this.shipTransform = transform;
        this.shipRigidbody = rigidbody;
        this.shipId = transform.gameObject.GetInstanceID();
        this.shipName = string.IsNullOrEmpty(name) ? transform.gameObject.name : name;
    }

    // Generate telemetry data based on the current state of the ship
    public TelemetryData GenerateTelemetry()
    {
        // Convert Unity position to geographic coordinates
        Vector3 position = shipTransform.position;
        double[] geoCoords = CoordinateConverter.UnityToGeo(position);
        double longitude = geoCoords[0];
        double latitude = geoCoords[1];

        float heading = shipTransform.eulerAngles.y;
        float speedKnots = shipRigidbody != null ? CoordinateConverter.UnitySpeedToKnots(shipRigidbody.linearVelocity.magnitude) : 0;

        return new TelemetryData
        {
            id = shipId,
            name = shipName,
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
            telemetry = new TelemetryData.TelemetryValues { rpm = 1200, fuelLevel = 85 }
        };
    }
}