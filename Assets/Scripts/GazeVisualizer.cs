using UnityEngine;
using UnityEngine.InputSystem;

public class GazeVisualizer : MonoBehaviour
{
    [SerializeField] private InputActionReference eyePose;
    [SerializeField] private float gazeDistance = 5f; // how far ahead the dot appears
    [SerializeField] private GameObject gazeDot;      // drag your sphere here

    private void OnEnable()
    {
        eyePose.action.Enable();
    }

    private void Update()
    {
        var pose = eyePose.action.ReadValue<UnityEngine.InputSystem.XR.PoseState>();

        Vector3 gazeOrigin    = pose.position;
        Vector3 gazeDirection = pose.rotation * Vector3.forward;

        // Move the dot along the gaze ray
        if (gazeDot != null)
        {
            // If gaze hits something, snap dot to hit point
            if (Physics.Raycast(gazeOrigin, gazeDirection, out RaycastHit hit, 20f))
            {
                gazeDot.transform.position = hit.point;
            }
            else
            {
                // Otherwise float it at a fixed distance ahead
                gazeDot.transform.position = gazeOrigin + gazeDirection * gazeDistance;
            }
        }

        // Draw a debug ray in Scene view (only visible in editor)
        Debug.DrawRay(gazeOrigin, gazeDirection * 10f, Color.cyan);
    }
}