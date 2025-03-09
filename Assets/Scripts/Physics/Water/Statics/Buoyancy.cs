using System.Diagnostics;
using System.IO;
using WaterInteraction;
using UnityEngine;

[RequireComponent(typeof(Submersion))]
public class Buoyancy : MonoBehaviour
{
    public bool buoyancyForceActive = true;
    private Vector3 buoyancyCenter = new Vector3();
    private Submersion submersion;
    private Rigidbody rigidBody;
    
    private Vector3 currentForceVector;
    
    void Start()
    {
        rigidBody = GetComponent<Rigidbody>();
        submersion = GetComponent<Submersion>();
    }

    
    void FixedUpdate()
    {
        if (!buoyancyForceActive) return;
        ApplyBuoyancyVolume();
    }


    private void ApplyBuoyancyVolume() 
    {
        buoyancyCenter = submersion.submerged.data.centroid;
        float displacedVolume = submersion.submerged.data.volume;
        float buoyancyForce = Constants.waterDensity*Constants.gravity*displacedVolume;
        Vector3 forceVector = new Vector3(0f, buoyancyForce, 0f);
        rigidBody.AddForceAtPosition(forceVector, buoyancyCenter);
        
        currentForceVector = new Vector3(0f, buoyancyForce, 0f);
    }
    
    [Header("Debug Visualization")]
    public bool showDebugVisuals = true;
    public Color buoyancyForceColor = Color.blue;
    public Color buoyancyCenterColor = Color.yellow;
    public float forceVectorScale = 0.001f; // Scale factor for force visualization
    public float centerMarkerSize = 0.5f;   // Size of the center marker
    
     private void OnDrawGizmos()
    {
        if (!showDebugVisuals || !Application.isPlaying) return;

        // Draw buoyancy center marker
        Gizmos.color = buoyancyCenterColor;
        Gizmos.DrawWireSphere(buoyancyCenter, centerMarkerSize);
        
        // Draw cross at buoyancy center
        Vector3 crossSize = Vector3.one * centerMarkerSize;
        Gizmos.DrawLine(buoyancyCenter - crossSize, buoyancyCenter + crossSize);
        Gizmos.DrawLine(
            buoyancyCenter - new Vector3(crossSize.x, 0, crossSize.z), 
            buoyancyCenter + new Vector3(crossSize.x, 0, crossSize.z)
        );
        Gizmos.DrawLine(
            buoyancyCenter - new Vector3(crossSize.x, crossSize.y, 0), 
            buoyancyCenter + new Vector3(crossSize.x, crossSize.y, 0)
        );

        // Draw force vector
        if (currentForceVector.magnitude > 0)
        {
            Gizmos.color = buoyancyForceColor;
            Vector3 scaledForce = currentForceVector * forceVectorScale;
            Gizmos.DrawLine(buoyancyCenter, buoyancyCenter + scaledForce);
            
            // Draw arrow head
            float arrowHeadSize = scaledForce.magnitude * 0.2f;
            Vector3 arrowTip = buoyancyCenter + scaledForce;
            Vector3 right = transform.right * arrowHeadSize;
            Vector3 forward = transform.forward * arrowHeadSize;
            Vector3 down = -scaledForce.normalized * arrowHeadSize;

            Gizmos.DrawLine(arrowTip, arrowTip + down + right);
            Gizmos.DrawLine(arrowTip, arrowTip + down - right);
            Gizmos.DrawLine(arrowTip, arrowTip + down + forward);
            Gizmos.DrawLine(arrowTip, arrowTip + down - forward);
        }

        // Draw text labels in scene view
        UnityEngine.Debug.DrawLine(buoyancyCenter, buoyancyCenter); // Force Unity to update the scene view
#if UNITY_EDITOR
        UnityEditor.Handles.Label(buoyancyCenter + Vector3.up * centerMarkerSize, 
            $"Buoyancy Force: {currentForceVector.magnitude:F2}N\n" +
            $"Volume: {submersion.submerged.data.volume:F2}m³");
#endif
    }
}

