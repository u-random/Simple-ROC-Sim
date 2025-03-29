using UnityEngine;


// This is just a simple speed applier for debug purposes
public class ShipSpeedController : MonoBehaviour
{
    [Tooltip("Desired speed in knots")]
    public float targetSpeedKnots = 0f;

    [Tooltip("How quickly the ship accelerates (force multiplier)")]
    public float accelerationFactor = 100f; // Increased default value

    [Tooltip("Use direct velocity control instead of forces")]
    public bool useDirectVelocityControl = false;

    [Tooltip("How quickly to reach target speed with direct control (0-1)")]
    [Range(0.01f, 1f)]
    public float velocityLerpFactor = 0.1f;

    private Rigidbody shipRigidbody;
    private Transform trueForwardTransform;

    void Start()
    {
        shipRigidbody = GetComponent<Rigidbody>();
        if (shipRigidbody == null)
        {
            Debug.LogError("ShipSpeedController requires a Rigidbody component!");
            enabled = false;
            return;
        }

        trueForwardTransform = transform.Find("TrueForward");

        // Print mass for debugging
        //Debug.Log($"Ship mass: {shipRigidbody.mass}");
    }

    void FixedUpdate()
    {
        if (targetSpeedKnots <= 0f) return;

        // Convert target speed from knots to Unity units/second
        float targetSpeedUnity = KnotsToUnitySpeed(targetSpeedKnots);

        // Get forward direction (horizontal only)
        Vector3 forwardDirection;
        if (trueForwardTransform != null)
        {
            forwardDirection = (trueForwardTransform.position - transform.position).normalized;
        }
        else
        {
            forwardDirection = transform.forward;
        }
        forwardDirection.y = 0;
        forwardDirection.Normalize();

        // Get current horizontal velocity
        Vector3 horizontalVelocity = new Vector3(shipRigidbody.linearVelocity.x, 0, shipRigidbody.linearVelocity.z);
        float currentSpeed = horizontalVelocity.magnitude;

        //Debug.Log($"Current speed: {currentSpeed} units/s ({TelemetryUtilities.UnitySpeedToKnots(currentSpeed)} knots), Target: {targetSpeedUnity} units/s ({targetSpeedKnots} knots)");

        if (useDirectVelocityControl)
        {
            // Direct velocity control approach
            Vector3 targetVelocity = forwardDirection * targetSpeedUnity;
            Vector3 newVelocity = Vector3.Lerp(shipRigidbody.linearVelocity, new Vector3(targetVelocity.x, shipRigidbody.linearVelocity.y, targetVelocity.z), velocityLerpFactor);
            shipRigidbody.linearVelocity = newVelocity;
        }
        else
        {
            // Force-based approach
            float speedDifference = targetSpeedUnity - currentSpeed;

            // Calculate force - made independent of mass since AddForce already accounts for mass
            float forceMagnitude = speedDifference * accelerationFactor;

            // Apply force in the forward direction
            Vector3 force = forwardDirection * forceMagnitude;

            // Debug the force being applied
            //Debug.Log($"Applying force: {force.magnitude} in direction {forwardDirection}");

            shipRigidbody.AddForce(force, ForceMode.Acceleration); // ForceMode.Acceleration ignores mass
        }
    }

    // Convert knots to Unity units per second
    private float KnotsToUnitySpeed(float knots)
    {
        // Reverse of UnitySpeedToKnots
        float kmPerHour = knots * 1.852f;
        return kmPerHour / (0.001f * 3600f);
    }

    // Public method to set speed from other scripts
    public void SetSpeed(float knots)
    {
        targetSpeedKnots = knots;
    }
}