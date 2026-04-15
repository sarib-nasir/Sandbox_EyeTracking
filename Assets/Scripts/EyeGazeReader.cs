using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

public class EyeGazeReader : MonoBehaviour
{
    [SerializeField]
    private InputActionAsset actionAsset;
    [SerializeField]
    private InputActionReference eyePose;
    // Start is called before the first frame update
    void OnEnable()
    {
        if (actionAsset != null)
        {
            actionAsset.Enable();
        }
    }

    // Update is called once per frame
    void Update()
    {
        UnityEngine.InputSystem.XR.PoseState  pose = eyePose.action.ReadValue<UnityEngine.InputSystem.XR.PoseState>();
        Debug.Log("Eye Gaze Position: " + pose.position);
        Debug.Log("Eye Gaze Rotation: " + pose.rotation);
    }
}
