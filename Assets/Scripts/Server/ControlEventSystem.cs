public static class ControlEventSystem
{
    // Event for control commands
    public delegate void ControlCommandHandler(string shipId, object controlData);
    public static event ControlCommandHandler OnControlCommand;

    public static void PublishControlCommand(string shipId, object controlData)
    {
        OnControlCommand?.Invoke(shipId, controlData);
    }
}