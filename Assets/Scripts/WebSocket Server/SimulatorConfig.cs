// Static configuration class
public static class SimulatorConfig
{
    // Map Configuration
    public static readonly double[] MapCenterCoordinates = new double[] { 10.570455, 59.425565 }; // {Longitude, Latitude}
    public static readonly float UnityUnitsToKm = 0.001f; // One unity unit = 1 meter
    public static readonly float MapSizeKm = 12f;
    // Networking Configuration
    public static readonly float TelemetryUpdateRate = 0.1f;
    public static readonly int WebSocketPort = 3000;
}