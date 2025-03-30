public static class ControlEventSystem
{
    // Event for control commands
    public delegate void ControlCommandHandler(string shipId, SignalingMessage controlData);
    public static event ControlCommandHandler OnControlCommand;

    public static void PublishControlCommand(string shipId, SignalingMessage controlData)
    {
        OnControlCommand?.Invoke(shipId, controlData);
    }
}