using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;
using TMPro;
using Unity.VisualScripting;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Quaternion = UnityEngine.Quaternion;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

// TODO: More realistic propeller rotation
// TODO: Add a swirly water effect behind the propeller + white lines rotating around the propeller

// TODO: Edit control mode to engine separate
// Have the speed be increased/decreased in 10% or 5% increments
// Multiply by a force
// Separate engine rotation, increments of 15 degrees.

[System.Serializable]
public class ControlState
{
    // Primary controls (used in unified mode)
    public float throttle = 0f;          // -1.0 to 1.0 range (matches netForce)
    public float rudder = 0f;            // -1.0 to 1.0 range (matches leftStickValue)
    public bool engineOn = false;        // Engine power state

    // Engine group controls
    public float mainThrottle = 0f;      // Controls main/aft engines
    public float mainRudder = 0f;        // Controls main/aft engines steering
    public float bowThrottle = 0f;       // Controls bow thrusters
    public float bowRudder = 0f;         // Controls bow thrusters steering
    
    // Camera control
    public float cameraRotation = 0f;    // Camera rotation angle

    // Optional advanced controls
    public float targetAngle = 0f;       // For direct angle control instead of rudder input
    public string engineMode = "unified"; // "unified", "group", "individual"

    // Ship identifier
    public string shipId = "";           // To identify which ship should receive commands
}

public enum AxisType 
{
    X,
    Y,
    Z
}

public class ShipController2 : MonoBehaviour
{
    [System.Serializable]
    public struct UIText
    {
        public TextMeshProUGUI forceText;
        public TextMeshProUGUI rotationText;
        public TextMeshProUGUI currentSpinText;
        public TextMeshProUGUI currentAngleText;
        public TextMeshProUGUI currentThrustText;
        public TextMeshProUGUI currentSpeedKnots;
    }


    public float maxSpeedKnots = 12f;
    private float maxForce = 0f;
    
    //public Vector2 forceClamp = new Vector2(-500f, 2750f);
    //public float forceMultiplier = 100f;
    public float rotationMultiplier = 10f;
    public bool holdingRudder = false;
    public bool holdingThrust = false;
    public bool debug = false;
    public int EngineRotationLimit = 45;

    [Header("Axis Configuration")]
    [Tooltip("This is the local axis around which the rudder should rotate")]
    public AxisType rudderRotationAxis = AxisType.Y; 
    [Tooltip("This is the local axis around which the propeller should rotate")]
    public AxisType propellerRotationAxis = AxisType.Z;    
    
    public UIText UI;

    private List<EnginePropellerPair> EnginePropellerPairs = new List<EnginePropellerPair>();
    private Transform SavedPropulsionRoot;
    private InputActions InputActions;
    private float ReturnSpeed = 10f;
    private Vector2 rotateValue;
    private Rigidbody ParentRigidbody;
    //private float propellerRotationCoefficient = 1.0f;


    private Vector3 SavedInitialPosition;
    private Quaternion SavedInitialRotation;



    
    private Vector3 StartDirection;

    private readonly string RotatingPropulsion = "RotatingPropulsion";
    private readonly string StaticPropulsion = "StaticPropulsion";

    private ControlState InternalControlState = new ControlState();

    private readonly float AccelerationRate = 0.025f; // How quickly force builds up
    private readonly float DecelerationRate = 0.08f; // How quickly force decreases when no input

    private readonly float MaxThrottle = 1.0f;

    // State variables
    private float CurrentSpin = 0f;
    private float CurrentAngle = 0f;
    private float CurrentThrust = 0f; // Cancel?
    private float CurrentThrottle = 0f; // CurrentThrottle is a scalar of the maximum engine force 0-1.0

    

    struct EnginePropellerPair
    {
        public GameObject EngineJoint;
        public GameObject PropellerJoint;

        public EnginePropellerPair(Transform engine, Transform propeller)
        {
            EngineJoint = engine.GameObject();
            PropellerJoint = propeller.GameObject();
        }
    }


    // TODO List
    // Generate "Gameplay" - Control structs from input or control message from ROC
    // Either control all engines the same, or assign different controlstructs to different engines
    // Support with two keybinds in Unity, WASD and arrows, for different engine controls
    // Should maybe be able to assign engines to sets, that shares control input
    // Either set automatically here by searching game objects childs, or assign in list in editor



    #region Unity Lifetime Functions

    private void Awake() // OK
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        InputActions = new InputActions();
        InputActions.Ship.Rudder.performed += ctx => rotateValue = ctx.ReadValue<Vector2>();
        InputActions.Ship.Rudder.canceled += ctx => rotateValue = Vector2.zero;
        InputActions.Debug.ResetButton.performed += ctx => ResetPosition();
    }


    private void Start() // OK
    {
        SavedInitialPosition = transform.position;
        SavedInitialRotation = transform.rotation;
        SavedPropulsionRoot = transform.Find(RotatingPropulsion) != null
            ? transform.Find(RotatingPropulsion)
            : transform.Find(StaticPropulsion);
        ParentRigidbody = GetComponent<Rigidbody>();

        maxForce = CalculateRequiredForceForSpeed(maxSpeedKnots, ParentRigidbody.mass);
        SearchAndAssignChild(SavedPropulsionRoot.GameObject());

        //print("Number of EnginePropeller pairs" + enginePropellerPairs.Count); // Debugging

        // Start direction for debug vectors
        if (EnginePropellerPairs.Count > 0)
        {
            Transform engineJoint = EnginePropellerPairs[0].EngineJoint.transform;
            StartDirection = -engineJoint.forward; //Quaternion.Euler(0, engineRotationLimit, 0)*
        }
    }


    private void FixedUpdate() // WiP
    {
        GenerateControl(); // Currently overwrites ROC input

        if (SavedPropulsionRoot) WorkOnJoints();

        /* UPDATE Debug texts */
        // TODO: Find and assign based on scene root interface prefab
        if (UI.forceText) UI.forceText.text = "Trigger Force: " + InternalControlState.throttle.ToString("F2");
        if (UI.rotationText) UI.rotationText.text = "Target Rotation: " + InternalControlState.rudder.ToString("F2");
        if (UI.currentAngleText) UI.currentAngleText.text = "Current Angle: " + CurrentAngle.ToString("F0");
        if (UI.currentThrustText) UI.currentThrustText.text = "Current Force: " + CurrentThrust.ToString("F0");
        if (UI.currentSpeedKnots) UI.currentSpeedKnots.text = "Speed: " + CalculateSpeed().ToString("F1") + " knots";
        if (UI.currentSpinText) UI.currentSpinText.text = "Current Spin: " + (CurrentSpin / Time.deltaTime).ToString("F0") + " degrees/frame";
    }

    #endregion

    #region Ship Control Functions

    /// Find all engine joints as children of the propulsion object and populate the list
    private void SearchAndAssignChild(GameObject firstTargetChild) //OK
    {
        foreach (Transform engineJoint in firstTargetChild.transform)
        {
            if (firstTargetChild.name == RotatingPropulsion)
            {
                Transform propellerJoint = engineJoint.Find("PropellerJoint");
                if (propellerJoint != null)
                    EnginePropellerPairs.Add(new EnginePropellerPair(engineJoint, propellerJoint));
            }
            // The idea behind splitting these are that if rotating propulsion, the propeller is a child of the engine joint,
            // and thus rotates with it. But if static propulsion, the propeller does not rotate in relation to the
            // rudder
            else if (firstTargetChild.name == StaticPropulsion)
            {
                Debug.Log(firstTargetChild.name + " is a Static Propeller");
                Transform propellerJoint = firstTargetChild.transform.Find("PropellerJoint");
                if (propellerJoint != null)
                    EnginePropellerPairs.Add(new EnginePropellerPair(engineJoint, propellerJoint));
            }
            else print("No propulsion object found.");
        }
    }


    /// Iterate over each pair in the enginePropellerPairs list, and apply transformations and force on the joints
    private void WorkOnJoints() //WiPfloat leftStickValue, float netForce
    {
        foreach (EnginePropellerPair pair in EnginePropellerPairs)
        {
            Transform engineJoint = pair.EngineJoint.transform;
            Transform propellerJoint = pair.PropellerJoint.transform;
            // Check if this is a main/aft engine or a bow thruster
            bool isMainEngine = engineJoint.name.Contains("Engine") && !engineJoint.name.Contains("Bow");
            bool isBowThruster = engineJoint.name.Contains("Bow");


            // Using main engine as default
            float throttleValue = InternalControlState.mainThrottle; //netForce;
            float rudderValue   = InternalControlState.mainRudder; //leftStickValue;
            print("Throttle Main: "+ InternalControlState.mainThrottle);
            print("Rudder Main: "+ InternalControlState.mainRudder);

            // In group mode, use separate controls for main and bow engines
            if (InternalControlState.engineMode == "group" && isBowThruster)
            {
                throttleValue = InternalControlState.bowThrottle;
                rudderValue = InternalControlState.bowRudder;
            }

            // Apply controls based on engine type
            if (isMainEngine)
            {
                print("Working on Main Engine");
                RotateRudder(GetDesiredRotation(rudderValue, engineJoint));
                ApplyForce(throttleValue, pair);
                RotatePropeller(propellerJoint);
            }
            else if (isBowThruster)
            {
                // For bow thrusters in unified mode, traditionally we reverse the rudder control
                float effectiveRudderValue = (InternalControlState.engineMode == "unified") ? -rudderValue : rudderValue;
                RotateRudder(GetDesiredRotation(effectiveRudderValue, engineJoint));
                ApplyForce(throttleValue, pair);
                RotatePropeller(propellerJoint);
            }
            else
            {
                // Handle any other engine types
                RotateRudder(GetDesiredRotation(rudderValue, engineJoint));
                ApplyForce(throttleValue, pair);
                RotatePropeller(propellerJoint);
            }
        }
    }


    /// Apply force to the global parent rigidbody at the propeller joint position 
    private void ApplyForce(float force, EnginePropellerPair pair)
    {
        if (Mathf.Abs(force) <= 0.05f) return; // Only apply force if there's significant input
        // Apply the throttle with current maxForce scaling
        float finalForce = force * maxForce; 
        CurrentThrust = finalForce;

        Vector3 direction = GetDirectionFromAxis(propellerRotationAxis, pair.EngineJoint.transform);

        // Normalize in XZ plane only
        Vector3 directionXZ = new Vector3(direction.x, 0, direction.z);
        directionXZ = directionXZ.normalized;
        //direction = new Vector3(directionXZ.x, direction.y, directionXZ.z);
        
        Vector3 position = pair.PropellerJoint.transform.position;

        // Apply the force to the rigidbody
        ParentRigidbody.AddForceAtPosition(-directionXZ * CurrentThrust, position);

        // Draw force vector for debugging
        if (debug)
        {
            Debug.DrawRay(position, 50f * directionXZ, Color.yellow);
        }
    }


    /// Rotate the rudder based on the GetDesiredRotation function return values 
    private void RotateRudder((Quaternion targetRotation, Transform joint, float desiredRotation) data)
    {
        if (holdingRudder)
            data.joint.localRotation = data.targetRotation;
        else if (data.desiredRotation < -0.001 || data.desiredRotation > 0.001)
            data.joint.localRotation = Quaternion.Lerp(data.joint.localRotation, data.targetRotation, ReturnSpeed);
        else
            ResetRotation(data.joint.transform);
    }

    /// Get the desired rotation based on the input value and the joint's current rotation
    private (Quaternion, Transform, float) GetDesiredRotationOLD(float rotationValue, Transform joint)
    {
        float desiredRotation = -rotationValue * rotationMultiplier;
        float newRotation = joint.localEulerAngles.y + desiredRotation;
        newRotation = NormalizeAngle(newRotation);
        if (newRotation > 180) newRotation -= 360; // To avoid snapping at 0/360 degrees
        if (EngineRotationLimit == 0) CurrentAngle = newRotation;
        else CurrentAngle = Mathf.Clamp(newRotation, -EngineRotationLimit, EngineRotationLimit); 
        Quaternion targetRotation = Quaternion.Euler(0, CurrentAngle, 0);
        return (targetRotation, joint, desiredRotation);
    }
    
    /// Updated to use a variable axis
    private (Quaternion, Transform, float) GetDesiredRotation(float rotationValue, Transform joint)
    {
        float desiredRotation = -rotationValue * rotationMultiplier;
        Vector3 currentEuler = joint.localEulerAngles;
    
        // Create target rotation while preserving other axes
        Vector3 targetEuler = currentEuler;
        
        int axisIndex = (int)rudderRotationAxis; // Map axis to index
        float rotation = NormalizeAngle(currentEuler[axisIndex] + desiredRotation);
        if (rotation > 180) rotation -= 360;

        // full 360 degree rotation if no engine limit
        if (EngineRotationLimit == 0) targetEuler[axisIndex] = rotation;
        else targetEuler[axisIndex] = Mathf.Clamp(rotation, -EngineRotationLimit, EngineRotationLimit);
    
        CurrentAngle = targetEuler[(int)rudderRotationAxis]; // X=0, Y=1, Z=2
        Quaternion targetRotation = Quaternion.Euler(targetEuler);
        return (targetRotation, joint, desiredRotation);
    }


    /// Rotate the propeller based on the current thrust and time
    private void RotatePropellerOLD(Transform joint)
    {
        float rotationThisFrame = CurrentThrust * rotationMultiplier * Time.deltaTime;
        rotationThisFrame = Mathf.Clamp(rotationThisFrame, -1000f, 3000f);

        Quaternion currentRotation = joint.localRotation;
        Quaternion newRotation = Quaternion.Euler(currentRotation.eulerAngles.x, currentRotation.eulerAngles.y,
            currentRotation.eulerAngles.z + rotationThisFrame);
        joint.localRotation = newRotation;
        CurrentSpin = rotationThisFrame;
    }
    
    /// Updated to use a variable axis
    private void RotatePropeller(Transform joint)
    {
        float rotationThisFrame = CurrentThrust * rotationMultiplier * Time.deltaTime;
        rotationThisFrame = Mathf.Clamp(rotationThisFrame, -100f, 200f);

        Vector3 currentEuler = joint.localRotation.eulerAngles;
        Vector3 newEuler = currentEuler;
    
        // Modify only the selected axis, preserving others
        switch(propellerRotationAxis)
        {
            case AxisType.X: newEuler.x += rotationThisFrame; break;
            case AxisType.Y: newEuler.y += rotationThisFrame; break;
            case AxisType.Z: newEuler.z += rotationThisFrame; break;
        }
    
        joint.localRotation = Quaternion.Euler(newEuler);
        CurrentSpin = rotationThisFrame;
    }


    /// Smoothly reset the rotation of the joint to 0
    private void ResetRotation(Transform joint)
    {
        float currentYRotation = NormalizeAngle(joint.localEulerAngles.y);
        float newAngle = Mathf.MoveTowardsAngle(currentYRotation, 0, ReturnSpeed); //Time.deltaTime * 
        joint.localRotation = Quaternion.Euler(0, newAngle, 0);
    }
    
    /// Callback function for the reset button
    private void ResetPosition()
    {
        float positionThreshold = 2.0f;
        float rotationThreshold = 10.0f;
        float positionDifference = Vector3.Distance(transform.position, SavedInitialPosition);
        float rotationDifference = Quaternion.Angle(transform.rotation, SavedInitialRotation);
        if (positionDifference > positionThreshold || rotationDifference > rotationThreshold)
        {
            transform.position = SavedInitialPosition;
            transform.rotation = SavedInitialRotation;
            if (ParentRigidbody != null)
            {
                ParentRigidbody.linearVelocity = Vector3.zero;
                ParentRigidbody.angularVelocity = Vector3.zero;
                ParentRigidbody
                    .ResetInertiaTensor(); // Optional, use if we need to reset rotational velocities due to inertia changes
            }

            print("Position and rotation reset. Forces removed.");
        }
        else
            print("Reset hit, but position and rotation are within thresholds.");
    }

    #endregion

    #region Control Data Generation

    private void GenerateControl()
    {
        float rightTriggerValue = InputActions.Ship.PositivePropulsion.ReadValue<float>();
        float leftTriggerValue = InputActions.Ship.NegativePropulsion.ReadValue<float>();
        float inputForce = rightTriggerValue - leftTriggerValue; // Input force direction
        float leftStickValue = InputActions.Ship.Rudder.ReadValue<Vector2>().x;

        // Accumulate throttle based on input
        if (inputForce > 0) CurrentThrottle += AccelerationRate * inputForce * Time.fixedDeltaTime * 50f;
        else if (inputForce < 0) CurrentThrottle += AccelerationRate * inputForce * Time.fixedDeltaTime * 50f;
        else if (!holdingThrust) AutoDecelerate();
        
        // Clamp to maximum values
        CurrentThrottle = Mathf.Clamp(CurrentThrottle, -MaxThrottle, MaxThrottle);
        // Snap to zero if very close
        if (Mathf.Abs(CurrentThrottle) < 0.01f) CurrentThrottle = 0f;
        
        // Update control state
        InternalControlState.mainThrottle = CurrentThrottle;
        InternalControlState.mainRudder= leftStickValue;
        //ControlState.throttle = CurrentThrottle;
        //ControlState.rudder = leftStickValue;
        InternalControlState.engineOn = (CurrentThrottle != 0f);
    }

    private void AutoDecelerate()
    {
        // Gradually return to zero when no input
        if (CurrentThrottle > 0)
            CurrentThrottle -= DecelerationRate * Time.fixedDeltaTime * 50f;
        else if (CurrentThrottle < 0)
            CurrentThrottle += DecelerationRate * Time.fixedDeltaTime * 50f;
    }



private void HandleControlCommand(string shipId, object controlData)
    {
        // Add debug log to see what we're receiving
        Debug.Log($"HandleControlCommand for ship {shipId} (this={transform.gameObject.GetInstanceID()}), controlData type: {controlData.GetType()}");
        
        // Check if this command is for this ship
        if (shipId != transform.gameObject.GetInstanceID().ToString()) {
            Debug.Log($"Ignoring command: shipId {shipId} doesn't match {transform.gameObject.GetInstanceID()}");
            return;
        }

        // The controlData is now the SignalingMessage itself, which directly contains our control properties
        // Cast it to access the properties directly
        if (controlData is SignalingMessage message)
        {
            Debug.Log($"Processing as SignalingMessage with properties: Main throttle={message.mainThrottle}, Main rudder={message.mainRudder}, engineMode={message.engineMode}");

            // Create a ControlState from the message properties
            ControlState controlState = new ControlState {
                mainThrottle = message.mainThrottle,
                mainRudder = message.mainRudder,
                bowThrottle = message.bowThrottle,
                bowRudder = message.bowRudder,
                cameraRotation = message.cameraRotation,
                engineMode = message.engineMode,
                engineOn = message.engineOn
            };

            // Apply the control state
            //ApplyControlState(controlState);
            InternalControlState = controlState;
        }
        else
        {
            Debug.LogError($"Received controlData is not a SignalingMessage. Type: {controlData.GetType()}");
            
            // Try to convert using JSON as fallback
            try {
                string jsonData = JsonUtility.ToJson(controlData);
                Debug.Log($"Fallback: Converting JSON data: {jsonData}");
                ControlState controlState = JsonUtility.FromJson<ControlState>(jsonData);
                ApplyControlState(controlState);
            }
            catch (System.Exception ex) {
                Debug.LogError($"Failed to parse control data: {ex.Message}");
            }
        }
    }


    private void ApplyControlState(ControlState controlState)
    {


        // Apply control state based on engine mode
        switch (controlState.engineMode)
        {
            case "group":
                // Apply group-based controls
                InternalControlState.mainThrottle = controlState.mainThrottle;
                InternalControlState.mainRudder = controlState.mainRudder;
                InternalControlState.bowThrottle = controlState.bowThrottle;
                InternalControlState.bowRudder = controlState.bowRudder;

                // Calculate effective throttle and rudder as average for compatibility
                //ControlState.throttle = (controlState.mainThrottle + controlState.bowThrottle);
                //ControlState.rudder = (controlState.mainRudder + controlState.bowRudder);
                break;

            case "unified":
            default:
                // Apply unified controls
                InternalControlState.throttle = controlState.throttle;
                InternalControlState.rudder = controlState.rudder;

                // Mirror to group controls for compatibility
                InternalControlState.mainThrottle = controlState.throttle;
                InternalControlState.mainRudder = controlState.rudder;
                InternalControlState.bowThrottle = controlState.throttle;
                InternalControlState.bowRudder = controlState.rudder;
                break;
        }

        // Apply camera rotation
        InternalControlState.cameraRotation = controlState.cameraRotation;

        // Set engine on state if any throttle is non-zero
        InternalControlState.engineOn = controlState.engineOn ||
                                Mathf.Abs(InternalControlState.mainThrottle) > 0.01f ||
                                Mathf.Abs(InternalControlState.bowThrottle) > 0.01f;

        Debug.Log($"Applied control state: Mode={controlState.engineMode}, " +
                  $"Main={InternalControlState.mainThrottle}/{InternalControlState.mainRudder}, " +
                  $"Bow={InternalControlState.bowThrottle}/{InternalControlState.bowRudder}, " +
                  $"Camera={InternalControlState.cameraRotation}");
    }

    #endregion
    
    #region Helper Functions
    /// Normalize an angle to [0, 360) degrees
    private float NormalizeAngle(float angle)
    {
        while (angle < 0.0f) angle += 360.0f;
        while (angle >= 360.0f) angle -= 360.0f;
        return angle;
    }
    
    private Vector3 GetDirectionFromAxis(AxisType axisType, Transform reference)
    {
        switch(axisType)
        {
            case AxisType.Z: return reference.forward;
            case AxisType.Y: return -reference.up;
            case AxisType.X: return reference.right;
            default: return reference.forward;
        }
    }
    
    
    // Calculate the force required to achieve a target speed
    public float CalculateRequiredForceForSpeed(float targetSpeedKnots, float shipMass)
    {
        // Convert knots to Unity units per second
        float targetSpeedUnity = TelemetryUtilities.KnotsToUnitySpeed(targetSpeedKnots);
    
        // Basic water resistance formula: resistance increases with the square of velocity
        // F = k * v^2, where k is a resistance coefficient
        float waterResistanceCoefficient = 0.5f; // This can be adjusted based on ship shape/size
    
        // Calculate base force needed to overcome water resistance at target speed
        float resistanceForce = waterResistanceCoefficient * targetSpeedUnity * targetSpeedUnity;
    
        // Scale by mass to ensure proper acceleration
        // More massive ships need more force to reach the same speed
        float requiredForce = resistanceForce * shipMass;
    
        // Add a bit of extra force to ensure the ship can reach and maintain target speed
        float extraForceFactor = 1.2f;
        requiredForce *= extraForceFactor;
    
        return requiredForce;
    }

    private float CalculateSpeed()
    {
        // Calculate current speed in knots
        Vector3 horizontalVelocity = ParentRigidbody != null ?
            new Vector3(ParentRigidbody.linearVelocity.x, 0, ParentRigidbody.linearVelocity.z) : Vector3.zero;
        float currentSpeedKnots = ParentRigidbody != null ?
            TelemetryUtilities.UnitySpeedToKnots(horizontalVelocity.magnitude) : 0;
        return currentSpeedKnots;
    }
    
    #endregion
    
    
    
    private void OnEnable()
    {
        if (InputActions != null) InputActions.Enable();
        ControlEventSystem.OnControlCommand += HandleControlCommand; // Trigger HandleControlCommand by event
    }
    
    
    private void OnDisable()
    {
        if (InputActions != null) InputActions.Disable();
        ControlEventSystem.OnControlCommand -= HandleControlCommand;
    }
    
    
    private void OnDrawGizmos()
    {
        if (!debug) return;
        if (EnginePropellerPairs == null) return;

        foreach (EnginePropellerPair pair in EnginePropellerPairs)
        {
            Transform engineJoint = pair.EngineJoint.transform;

            // Draw semicircle
            for (int i = 0; i <= 2*EngineRotationLimit; i++)
            {
                Vector3 rotatedStartDirection = Quaternion.Euler(0, -89, 0) *transform.rotation * StartDirection; // Apply parent rotation to startDirection
                Vector3 lineStart = engineJoint.position + Quaternion.Euler(0, i -EngineRotationLimit, 0) * rotatedStartDirection;
                Vector3 lineEnd = engineJoint.position + Quaternion.Euler(0, i -EngineRotationLimit-1, 0) * rotatedStartDirection;
                Color lineColor = i < EngineRotationLimit ? Color.red : Color.green; // Change color based on angle
                //Debug.DrawLine(lineStart, lineEnd, lineColor);
            }

            float currentAngle = engineJoint.localRotation.y;
            
            // Draw line for current rotation
            Vector3 currentDirection = Quaternion.Euler(0, currentAngle, 0) * -engineJoint.forward;
            //Debug.DrawLine(engineJoint.position, engineJoint.position + currentDirection, Color.magenta);
        }
    }
}
