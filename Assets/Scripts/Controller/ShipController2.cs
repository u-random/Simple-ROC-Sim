using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;
using Vector2 = UnityEngine.Vector2;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using System;
using TMPro;


// TODO: More realistic propeller rotation
// TODO: Add a swirly water effect behind the propeller + white lines rotating around the propeller
// TODO: Edit control mode to engine separate
// Have the speed be increased/decreased in 10% or 5% increments
// Multiply by a force
// Separate engine rotation, increments of 15 degrees.
// TODO: Abstract out the control State generation, making this controller input agnostic, supporting either signal messages or direct unity input
// TODO: Add UI elements to view the rotations


[System.Serializable]
public class ControlState
{
    // -1.0 to 1.0 range (matches netForce, leftStickValue)
    public float mainThrottle;  // Controls main/aft engines
    public float mainRudder;    // Controls main/aft engines steering
    public float bowThrottle;   // Controls bow thrusters
    public float bowRudder;     // Controls bow thrusters steering

    public float cameraRotation;          // UNIMPLEMENTED Camera rotation angle
    public float targetAngle;             // UNIMPLEMENTED For direct angle control instead of rudder input
    public string engineMode = "unified"; // Swap between "unified" and "group"
    public bool engineOn;                 // Engine power state (Set from throttle input if active)
    public string shipId;                 // To identify which ship should receive commands
}

[System.Serializable]
public struct UIText
{
    public TextMeshProUGUI currentMode;
    public TextMeshProUGUI currentThrottle;     // Inputs
    public TextMeshProUGUI currentThrust;       //Generated Force
    public TextMeshProUGUI currentThrottle2;    // Inputs
    public TextMeshProUGUI currentThrust2;      //Generated Force

    public TextMeshProUGUI currentRudder;   // Inputs
    public TextMeshProUGUI currentRudderAngle;
    public TextMeshProUGUI currentRudder2;  // Inputs
    public TextMeshProUGUI currentRudderAngle2;
    // Propeller Rotation values
    public TextMeshProUGUI currentSpin;
    public TextMeshProUGUI currentSpin2;

    public TextMeshProUGUI currentSpeedKnots;
}

public enum AxisType 
{
    X,
    Y,
    Z
}


/// <summary>
/// Custom ship controller script.
/// Now with support for two engine groups, Aft (default) and Bow, decided based on naming.
/// </summary>
public class ShipController2 : MonoBehaviour
{
    public float maxSpeedKnots = 12f;
    private float maxForce;
    
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

    [SerializeField] private GameObject interfacePrefab;
    private UIText UI;

    private List<EnginePropellerPair> enginePropellerPairs = new List<EnginePropellerPair>();
    private Transform savedPropulsionRoot;
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
    private SignalingMessage latestROCMessage = new SignalingMessage(); // Store the latest message from ROC

    private readonly float AccelerationRate = 0.025f; // How quickly force builds up
    private readonly float DecelerationRate = 0.08f; // How quickly force decreases when no input
    private readonly float MaxThrottle = 1.0f;

    // -1.0 to 1.0 Input scalars
    private float CurrentThrottle, CurrentThrottle2;
    private float CurrentRudder, CurrentRudder2;
    // Force value
    private float CurrentThrust, CurrentThrust2;
    // Propeller spin
    private float CurrentSpin, CurrentSpin2;

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
    // OK - Generate "Gameplay" - Control structs from input or control message from ROC
    // OK - Either control all engines the same, or assign different controlstructs to different engines
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
        savedPropulsionRoot = transform.Find(RotatingPropulsion) != null
            ? transform.Find(RotatingPropulsion)
            : transform.Find(StaticPropulsion);
        ParentRigidbody = GetComponent<Rigidbody>();

        maxForce = CalculateRequiredForceForSpeed(maxSpeedKnots, ParentRigidbody.mass);
        SearchAndAssignChild(savedPropulsionRoot.GameObject());
        FindAndAssignUI();

        //print("Number of EnginePropeller pairs" + enginePropellerPairs.Count); // Debugging

        // Start direction for debug vectors
        if (enginePropellerPairs.Count > 0)
        {
            Transform engineJoint = enginePropellerPairs[0].EngineJoint.transform;
            StartDirection = -engineJoint.forward; //Quaternion.Euler(0, engineRotationLimit, 0)*
        }
    }


    private void FixedUpdate() // WiP
    {
        // Always call GenerateControl - it will handle both Unity and ROC inputs
        GenerateControl();

        if (savedPropulsionRoot) WorkOnJoints();

        UpdateUI();
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
                    enginePropellerPairs.Add(new EnginePropellerPair(engineJoint, propellerJoint));
            }
            // The idea behind splitting these are that if rotating propulsion, the propeller is a child of the engine joint,
            // and thus rotates with it. But if static propulsion, the propeller does not rotate in relation to the
            // rudder
            else if (firstTargetChild.name == StaticPropulsion)
            {
                Debug.Log(firstTargetChild.name + " is a Static Propeller");
                Transform propellerJoint = firstTargetChild.transform.Find("PropellerJoint");
                if (propellerJoint != null)
                    enginePropellerPairs.Add(new EnginePropellerPair(engineJoint, propellerJoint));
            }
            else print("No propulsion object found.");
        }
    }


    // TODO: Apply different throttle, rotation if in split/group mode
    /// Iterate over each pair in the enginePropellerPairs list, and apply transformations and force on the joints
    private void WorkOnJoints() //WiP float leftStickValue, float netForce
    {
        foreach (EnginePropellerPair pair in enginePropellerPairs)
        {
            Transform engineJoint = pair.EngineJoint.transform;
            Transform propellerJoint = pair.PropellerJoint.transform;

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

            // Have Bow thruster rotate as mirror of aft
            if (InternalControlState.engineMode == "unified" && isBowThruster) rudderValue = -rudderValue ;
            RotateRudder(GetDesiredRotation(rudderValue, engineJoint));
            ApplyForce(throttleValue, pair);
            RotatePropeller(propellerJoint);
        }
    }


    /// Apply force to the global parent rigidbody at the propeller joint position 
    private void ApplyForce(float throttleMultiplier, EnginePropellerPair pair)
    {
        bool isBowThruster = pair.EngineJoint.name.Contains("Bow");
        //print("bow thrust: " + isBowThruster);
        // Apply the throttle with current maxForce scaling
        // Calculate before eventual quick return to update thrust references to return to 0 if no input
        float finalForce = throttleMultiplier * maxForce;
        if (!isBowThruster) CurrentThrust = finalForce;
        else CurrentThrust2 = finalForce;

        if (Mathf.Abs(throttleMultiplier) <= 0.01f) return; // Only apply force if there's significant input

        Vector3 direction = GetDirectionFromAxis(propellerRotationAxis, pair.EngineJoint.transform);

        // Normalize in XZ plane only
        Vector3 directionXZ = new Vector3(direction.x, 0, direction.z);
        directionXZ = directionXZ.normalized;
        //direction = new Vector3(directionXZ.x, direction.y, directionXZ.z);
        
        Vector3 position = pair.PropellerJoint.transform.position;

        // Apply the force to the rigidbody
        ParentRigidbody.AddForceAtPosition(-directionXZ * finalForce, position);

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


    private float _lastRotationUpdateTime = 0f;
    private const float ROTATION_UPDATE_THRESHOLD = 0.03f;
    /// Updated to use a variable axis
    private (Quaternion, Transform, float) GetDesiredRotation(float rotationValue, Transform joint)
    {
        bool isBowThruster = joint.name.Contains("Bow");
        if (holdingRudder)
        {
            // Added some latency to input sampling
            float currentTime = Time.time;
            if (currentTime - _lastRotationUpdateTime < ROTATION_UPDATE_THRESHOLD) return (joint.localRotation, joint, 0f);
            _lastRotationUpdateTime = currentTime;
        }

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

        if (!holdingRudder) targetEuler[axisIndex] = Mathf.Clamp(rotation, -45, 45);
        // Update global variable based on engine group
        if (!isBowThruster) CurrentRudder = targetEuler[(int)rudderRotationAxis]; // X=0, Y=1, Z=2
        else CurrentRudder2 = targetEuler[(int)rudderRotationAxis];
        Quaternion targetRotation = Quaternion.Euler(targetEuler);
        return (targetRotation, joint, desiredRotation);
    }


    // TODO: Fix this, make it more realistic
    /// Updated to use a variable axis
    private void RotatePropeller(Transform joint)
    {
        if (Math.Abs(CurrentThrottle + CurrentThrottle2) <= 0.03f) return; // Quick return if insignificant input

        bool isBowThruster = joint.parent.name.Contains("Bow");
        bool separateThrust = InternalControlState.engineMode == "group";

        float throttleMultiplier;
        if (isBowThruster && separateThrust) throttleMultiplier = CurrentThrottle2;
        else throttleMultiplier = CurrentThrottle; // Using main thrust if not bow thruster or if shared engine power

        float rotationThisFrame = throttleMultiplier * rotationMultiplier * 20f;
        // Time.deltaTime;
        rotationThisFrame = Mathf.Clamp(rotationThisFrame, -40f, 40f);

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

        // Calculate and store RPM
        // Convert degrees per frame to rotations per minute:
        // (degrees/frame) ÷ 360° = rotations/frame
        // (rotations/frame) ÷ Time.deltaTime = rotations/second
        // (rotations/second) × 60 = rotations/minute
        float rpm = (rotationThisFrame / 360f) * (1f / Time.deltaTime) * 60f;

        if (isBowThruster) CurrentSpin2 = rpm;
        else CurrentSpin = rpm;
    }


    /// Smoothly reset the rotation of the joint to 0
    private void ResetRotation(Transform joint)
    {
        float currentYRotation = NormalizeAngle(joint.localEulerAngles.y);
        float newAngle = Mathf.MoveTowardsAngle(currentYRotation, 0, ReturnSpeed); //Time.deltaTime * 
        joint.localRotation = Quaternion.Euler(0, newAngle, 0);
    }


    // TODO: Maybe reset control state or CurrentThrottle etc
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

    /// I know there's a lot of duplication here, but abstracting would be unnecessary
    private void GenerateControl()
    {
        //print("Control Mode?: " + latestROCMessage.controlModeActive);
        //print("latestROCMessage: " + JsonUtility.ToJson(latestROCMessage));

        float inputForce;
        float inputForce2;
        float leftStickValue;
        float leftStickValue2;

        if (latestROCMessage.controlModeActive) // Use Roc input
        {
            inputForce = latestROCMessage.mainThrottle;
            inputForce2 = latestROCMessage.bowThrottle;
            leftStickValue =latestROCMessage.mainRudder;
            leftStickValue2 = latestROCMessage.bowRudder;
        }
        else // Use Unity input
        {
            float rightTriggerValue = InputActions.Ship.PositivePropulsion.ReadValue<float>();
            float rightTriggerValueAlt = InputActions.Ship.PositivePropulsionAlt.ReadValue<float>();
            float leftTriggerValue = InputActions.Ship.NegativePropulsion.ReadValue<float>();
            float leftTriggerValueAlt = InputActions.Ship.NegativePropulsionAlt.ReadValue<float>();
            inputForce = rightTriggerValue - leftTriggerValue; // Input force direction
            inputForce2 = rightTriggerValueAlt - leftTriggerValueAlt;
            leftStickValue = InputActions.Ship.Rudder.ReadValue<Vector2>().x;
            leftStickValue2 = InputActions.Ship.RudderAlt.ReadValue<Vector2>().x;
        }
        
        // Accumulate throttle based on input
        if (inputForce > 0) CurrentThrottle += AccelerationRate * inputForce * Time.fixedDeltaTime * 50f;
        else if (inputForce < 0) CurrentThrottle += AccelerationRate * inputForce * Time.fixedDeltaTime * 50f;
        if (inputForce2 > 0) CurrentThrottle2 += AccelerationRate * inputForce2 * Time.fixedDeltaTime * 50f;
        else if (inputForce2 < 0) CurrentThrottle2 += AccelerationRate * inputForce2 * Time.fixedDeltaTime * 50f;
        else if (!holdingThrust) AutoDecelerate();
        print("Throttle now: " + CurrentThrottle.ToString("F2")); // This equals 0.02

        // Clamp to maximum values
        CurrentThrottle = Mathf.Clamp(CurrentThrottle, -MaxThrottle, MaxThrottle);
        //print("Throttle: " + CurrentThrottle.ToString("F2"));
        CurrentThrottle2 = Mathf.Clamp(CurrentThrottle2, -MaxThrottle, MaxThrottle);

        // Snap to zero if very close
        if (Mathf.Abs(CurrentThrottle) < 0.01f) CurrentThrottle = 0f;
        if (Mathf.Abs(CurrentThrottle2) < 0.01f) CurrentThrottle2 = 0f;

        // Update control state
        InternalControlState.mainThrottle = CurrentThrottle;
        InternalControlState.bowThrottle = CurrentThrottle2;
        InternalControlState.mainRudder = leftStickValue;
        InternalControlState.bowRudder = leftStickValue2;
        InternalControlState.engineOn = (CurrentThrottle != 0f);
    }


    private void AutoDecelerate()
    {
        // Gradually return to zero when no input
        if (CurrentThrottle > 0) CurrentThrottle -= DecelerationRate * Time.fixedDeltaTime * 50f;
        else if (CurrentThrottle < 0) CurrentThrottle += DecelerationRate * Time.fixedDeltaTime * 50f;
        if (CurrentThrottle2 > 0) CurrentThrottle2 -= DecelerationRate * Time.fixedDeltaTime * 50f;
        else if (CurrentThrottle2 < 0) CurrentThrottle2 += DecelerationRate * Time.fixedDeltaTime * 50f;
    }


    private void HandleControlCommand(string shipId, SignalingMessage message)
    {
        if (shipId != transform.gameObject.GetInstanceID().ToString()) return;
        print("message" + message);
        latestROCMessage = message; // Store the message for use in GenerateControl
    }


    #endregion
    
    #region Helper Functions


    // TODO: Find and assign based on scene root interface prefab
    private void FindAndAssignUI()
    {
        Canvas canvas = interfacePrefab.GetComponentInChildren<Canvas>();
        GameObject canvasObj = canvas != null ? canvas.gameObject : interfacePrefab;

        // Find all TextMeshProUGUI child components
        foreach (Transform child in canvasObj.transform)
        {
            TextMeshProUGUI textComponent = child.GetComponent<TextMeshProUGUI>();
            if (textComponent == null) continue;

            // Simple switch using hardcoded GameObject names for the interface prefab children
            switch (child.name)
            {
                case "CurrentMode"           : UI.currentMode         = textComponent; break;
                case "CurrentThrottle"       : UI.currentThrottle     = textComponent; break;
                case "CurrentThrottleForce"  : UI.currentThrust       = textComponent; break;
                case "CurrentThrottle2"      : UI.currentThrottle2    = textComponent; break;
                case "CurrentThrottleForce2" : UI.currentThrust2      = textComponent; break;
                case "CurrentRudder"         : UI.currentRudder       = textComponent; break;
                case "CurrentRudderAngle"    : UI.currentRudderAngle  = textComponent; break;
                case "CurrentRudder2"        : UI.currentRudder2      = textComponent; break;
                case "CurrentRudderAngle2"   : UI.currentRudderAngle2 = textComponent; break;
                case "CurrentSpin"           : UI.currentSpin         = textComponent; break;
                case "CurrentSpin2"          : UI.currentSpin2        = textComponent; break;
                case "CurrentSpeed"          : UI.currentSpeedKnots   = textComponent; break;
            }
        }

    }

    // TODO: Update rest of script to use all variables for both engines
    private void UpdateUI()
    {
        if (UI.currentMode)         UI.currentMode.text         = "Current Mode\t: "     + InternalControlState.engineMode;
        if (UI.currentThrottle)     UI.currentThrottle.text     = "Throttle1 Input\t: "  + InternalControlState.mainThrottle.ToString("F2");
        if (UI.currentThrust)       UI.currentThrust.text       = "Throttle1 Force\t: "  + CurrentThrust.ToString("F0");
        if (UI.currentThrottle2)    UI.currentThrottle2.text    = "Throttle2 Input\t: "  + InternalControlState.bowThrottle.ToString("F2");
        if (UI.currentThrust2)      UI.currentThrust2.text      = "Throttle2 Force\t: "  + CurrentThrust2.ToString("F0");
        if (UI.currentRudder)       UI.currentRudder.text       = "Rudder1 Input\t: "    + InternalControlState.mainRudder.ToString("F2");
        if (UI.currentRudderAngle)  UI.currentRudderAngle.text  = "Rudder1 Rotation\t: " + CurrentRudder.ToString("F0");
        if (UI.currentRudder2)      UI.currentRudder2.text      = "Rudder2 Input\t: "    + InternalControlState.bowRudder.ToString("F2");
        if (UI.currentRudderAngle2) UI.currentRudderAngle2.text = "Rudder2 Rotation\t: " + CurrentRudder2.ToString("F0");

        if (UI.currentSpin)  UI.currentSpin.text  = "Propeller1 Spin\t: " + CurrentSpin.ToString("F0") + " rpm";
        if (UI.currentSpin2) UI.currentSpin2.text = "Propeller2 Spin\t: " + CurrentSpin2.ToString("F0") + " rpm";

        if (UI.currentSpeedKnots) UI.currentSpeedKnots.text = "Speed\t\t: " + CalculateSpeed().ToString("F1") + " knots";

        // Add ROC control indicator with throttle/rudder values
        if (UI.currentThrottle && latestROCMessage.controlModeActive) UI.currentThrottle.text += " [CONTROL MODE]";
    }


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
        if (enginePropellerPairs == null) return;

        foreach (EnginePropellerPair pair in enginePropellerPairs)
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
