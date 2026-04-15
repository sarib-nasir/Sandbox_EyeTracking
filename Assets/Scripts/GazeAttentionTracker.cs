using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

public class GazeAttentionTracker : MonoBehaviour
{
    [Header("Gaze Input")]
    [SerializeField] private InputActionReference eyePose;

    [Header("Gaze Settings")]
    [SerializeField] private float gazeRayDistance = 20f;
    [SerializeField] private LayerMask gazeMask;

    [Header("Reticle")]
    [SerializeField] private GameObject reticleDot; 

    [Header("Highlight Settings")]
    [SerializeField] private Color defaultColor   = Color.white;
    [SerializeField] private Color highlightColor = Color.yellow;

    [Header("Logging")]
    [SerializeField] private float logInterval = 0.05f;
    [SerializeField] private string fileName   = "gaze_attention";


    private StreamWriter writer;
    private string filePath;
    private float nextLogTime  = 0f;
    private float sessionStart = 0f;

    
    private GameObject currentGazedObject  = null;
    private GameObject previousGazedObject = null;
    private float gazeStartTime = 0f;

    private Dictionary<string, float> totalAttentionTime 
        = new Dictionary<string, float>();

    private Dictionary<GameObject, Color> originalColors 
        = new Dictionary<GameObject, Color>();

    private Dictionary<GameObject, GazeTimerUI> timerUIs 
        = new Dictionary<GameObject, GazeTimerUI>();

    // -------------------------------------------------------

    void Start()
    {
        sessionStart = Time.time;
        nextLogTime  = 0f;

        if (eyePose != null)
            eyePose.action.Enable();

        // Setup CSV
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string desktop   = Environment.GetFolderPath(
                               Environment.SpecialFolder.Desktop);
        filePath = Path.Combine(desktop, $"{fileName}_{timestamp}.csv");
        writer   = new StreamWriter(filePath, false, Encoding.UTF8);
        WriteHeader();

        // Find all GazeTarget objects in scene automatically
        GazeTarget[] targets = FindObjectsOfType<GazeTarget>();
        foreach (var t in targets)
        {
            Renderer r = t.GetComponent<Renderer>();
            if (r != null)
                originalColors[t.gameObject] = r.material.color;

            totalAttentionTime[t.gameObject.name] = 0f;

            // Find the GazeTimerUI attached to this target
            GazeTimerUI ui = t.GetComponentInChildren<GazeTimerUI>();
            if (ui != null)
                timerUIs[t.gameObject] = ui;
        }

        Debug.Log($"[GazeTracker] Saving to: {filePath}");
    }

    void WriteHeader()
    {
        writer.WriteLine(
            "timestamp_s," +
            "gaze_origin_x,gaze_origin_y,gaze_origin_z," +
            "gaze_dir_x,gaze_dir_y,gaze_dir_z," +
            "hit_object," +
            "hit_pos_x,hit_pos_y,hit_pos_z," +
            "hit_distance_m," +
            "current_gaze_duration_s," +
            "total_attention_s"
        );
    }

    void Update()
    {
        // --- Read gaze ---
        Vector3 gazeOrigin    = Vector3.zero;
        Vector3 gazeDirection = Vector3.forward;

        if (eyePose != null && eyePose.action != null)
        {
            PoseState pose = eyePose.action.ReadValue<PoseState>();
            gazeOrigin    = pose.position;
            gazeDirection = pose.rotation * Vector3.forward;
        }

        // --- Raycast ---
        string   hitObjectName = "None";
        Vector3  hitPosition   = Vector3.zero;
        float    hitDistance   = 0f;
        GameObject hitObject   = null;

        if (Physics.Raycast(gazeOrigin, gazeDirection, 
                            out RaycastHit hit, gazeRayDistance, gazeMask))
        {
            hitObject     = hit.collider.gameObject;
            hitObjectName = hitObject.name;
            hitPosition   = hit.point;
            hitDistance   = hit.distance;

            // Move reticle to hit point
            if (reticleDot != null)
            {
                reticleDot.SetActive(true);
                reticleDot.transform.position = hit.point + 
                                                hit.normal * 0.01f;
            }
        }
        else
        {
            // No hit — place reticle far ahead
            if (reticleDot != null)
            {
                reticleDot.SetActive(true);
                reticleDot.transform.position = gazeOrigin + 
                                                gazeDirection * gazeRayDistance;
            }
        }

        // --- Handle gaze object change ---
        if (hitObject != currentGazedObject)
        {
            // Un-highlight previous object
            if (currentGazedObject != null)
                SetHighlight(currentGazedObject, false);

            // Update timer for previous object
            if (currentGazedObject != null)
            {
                float duration = Time.time - gazeStartTime;
                if (totalAttentionTime.ContainsKey(currentGazedObject.name))
                    totalAttentionTime[currentGazedObject.name] += duration;

                // Hide timer UI
                if (timerUIs.ContainsKey(currentGazedObject))
                    timerUIs[currentGazedObject].Hide();
            }

            currentGazedObject = hitObject;
            gazeStartTime      = Time.time;

            // Highlight new object
            if (currentGazedObject != null)
                SetHighlight(currentGazedObject, true);
        }

        // --- Update floating timer ---
        if (currentGazedObject != null && 
            timerUIs.ContainsKey(currentGazedObject))
        {
            float currentDuration = Time.time - gazeStartTime;
            float totalDuration   = totalAttentionTime.ContainsKey(
                                        currentGazedObject.name)
                                    ? totalAttentionTime[currentGazedObject.name]
                                    : 0f;

            timerUIs[currentGazedObject].UpdateTimer(
                currentDuration, totalDuration);
        }

        // --- Log to CSV ---
        if (Time.time >= nextLogTime)
        {
            nextLogTime = Time.time + logInterval;

            float timestamp       = Time.time - sessionStart;
            float currentDuration = currentGazedObject != null 
                                    ? Time.time - gazeStartTime : 0f;
            float totalDuration   = currentGazedObject != null && 
                                    totalAttentionTime.ContainsKey(
                                        currentGazedObject.name)
                                    ? totalAttentionTime[currentGazedObject.name]
                                    : 0f;

            writer.WriteLine(
                $"{timestamp:F4}," +
                $"{gazeOrigin.x:F4},{gazeOrigin.y:F4},{gazeOrigin.z:F4}," +
                $"{gazeDirection.x:F4},{gazeDirection.y:F4}," +
                $"{gazeDirection.z:F4}," +
                $"{hitObjectName}," +
                $"{hitPosition.x:F4},{hitPosition.y:F4},{hitPosition.z:F4}," +
                $"{hitDistance:F4}," +
                $"{currentDuration:F4}," +
                $"{totalDuration:F4}"
            );
            writer.Flush();
        }
    }

    void SetHighlight(GameObject obj, bool highlighted)
    {
        // Only highlight GazeTarget objects
        if (obj.GetComponent<GazeTarget>() == null) return;

        Renderer r = obj.GetComponent<Renderer>();
        if (r == null) return;

        if (highlighted)
        {
            r.material.color = highlightColor;
        }
        else
        {
            r.material.color = originalColors.ContainsKey(obj)
                ? originalColors[obj]
                : defaultColor;
        }
    }

    void OnDestroy()
    {
        // Log final summary
        Debug.Log("=== GAZE ATTENTION SUMMARY ===");
        foreach (var kvp in totalAttentionTime)
            Debug.Log($"{kvp.Key}: {kvp.Value:F2}s total attention");

        writer?.Flush();
        writer?.Close();
    }
}