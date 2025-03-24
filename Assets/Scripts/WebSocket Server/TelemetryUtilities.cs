using System;
using UnityEngine;


public class TelemetryUtilities
{
    // Returns heading in degrees (0-360), where 0 is North, 90 is East, etc.
    public static float GetShipHeading(Transform shipTransform)
    {
        // Get the forward vector of the ship in world space
        Vector3 forwardVector = shipTransform.forward;

        // Project the vector onto the XZ plane (ignore Y component)
        Vector3 flatForward = new Vector3(forwardVector.x, 0, forwardVector.z).normalized;

        // Convert to angle where Z+ is North (Unity's Z+ is forward by default)
        float heading = Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;

        // Convert to 0-360 range with 0 as North
        heading = (heading + 360) % 360;

        return heading;
    }

    // More precise use of forward as a vector between parent and child gameobjects
    public static float GetShipHeadingWithMarker(Transform shipTransform, Transform trueForwardTransform)
    {
        if (trueForwardTransform == null)
            return GetShipHeading(shipTransform); // Fallback to original method

        // Use direction from ship to forward marker
        Vector3 directionVector = (trueForwardTransform.position - shipTransform.position).normalized;
        Vector3 flatDirection = new Vector3(directionVector.x, 0, directionVector.z).normalized;

        float heading = Mathf.Atan2(flatDirection.x, flatDirection.z) * Mathf.Rad2Deg;
        heading = (heading + 360) % 360;

        return heading;
    }


    /// <summary>
    /// Converts Unity world position to geographic coordinates (longitude, latitude)
    /// </summary>
    /// <param name="unityPosition">Position in Unity world space</param>
    /// <returns>Array containing [longitude, latitude]</returns>
    public static double[] UnityToGeo(Vector3 unityPosition)
    {
        // TODO: FIX COORDINATE CALCULATION
        double kmPerDegreeLat = 111.0;
        double kmPerDegreeLon = kmPerDegreeLat * Math.Cos(SimulatorConfig.MapCenterCoordinates[1] * Math.PI / 180);

        double xOffsetKm = unityPosition.x * SimulatorConfig.UnityUnitsToKm;
        double zOffsetKm = unityPosition.z * SimulatorConfig.UnityUnitsToKm;

        double longitude = SimulatorConfig.MapCenterCoordinates[0] + (xOffsetKm / kmPerDegreeLon);
        double latitude = SimulatorConfig.MapCenterCoordinates[1] + (zOffsetKm / kmPerDegreeLat);

        return new double[] { longitude, latitude };
    }


    /// <summary>
    /// Converts speed in Unity units per second to nautical knots
    /// </summary>
    /// <param name="unitySpeed">Speed in Unity units per second</param>
    /// <returns>Speed in knots</returns>
    public static float UnitySpeedToKnots(float unitySpeed)
    {
        // Convert Unity units/s to km/h, then to knots
        // 1 knot = 1.852 km/h, so km/h / 1.852 = knots
        float kmPerHour = unitySpeed * SimulatorConfig.UnityUnitsToKm * 3600f;
        return kmPerHour / 1.852f;
    }
    
    public static float KnotsToUnitySpeed(float knots)
    {
        float kmPerHour = knots * 1.852f;
        return kmPerHour / (0.001f * 3600f);
    }


    /// <summary>
    /// Converts geographic coordinates to Unity world position
    /// </summary>
    /// <param name="longitude">Longitude in degrees</param>
    /// <param name="latitude">Latitude in degrees</param>
    /// <returns>Vector3 position in Unity world space (y=0)</returns>
    public static Vector3 GeoToUnity(double longitude, double latitude)
    {
        double kmPerDegreeLat = 111.0;
        double kmPerDegreeLon = kmPerDegreeLat * Math.Cos(SimulatorConfig.MapCenterCoordinates[1] * Math.PI / 180);

        double lonDiff = longitude - SimulatorConfig.MapCenterCoordinates[0];
        double latDiff = latitude - SimulatorConfig.MapCenterCoordinates[1];

        float xPosUnity = (float)(lonDiff * kmPerDegreeLon / SimulatorConfig.UnityUnitsToKm);
        float zPosUnity = (float)(latDiff * kmPerDegreeLat / SimulatorConfig.UnityUnitsToKm);

        return new Vector3(xPosUnity, 0, zPosUnity);
    }
}