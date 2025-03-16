using UnityEngine;
using System;

/// <summary>
/// Utility class for converting between Unity world coordinates and geographic coordinates
/// </summary>
public static class CoordinateConverter
{
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