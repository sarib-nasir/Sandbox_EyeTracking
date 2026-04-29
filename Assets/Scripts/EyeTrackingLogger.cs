using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using VIVE.OpenXR;
using UnityEngine.InputSystem;
using VIVE.OpenXR.EyeTracker;
public class EyeTrackingLogger : MonoBehaviour
{

    [Header("Attention Tracking")]
    [SerializeField] private Color highlightColor = Color.yellow;

    private GameObject currentGazedObject = null;
    private float gazeStartTime = 0f;
    private Dictionary<string, float> totalAttentionTime 
        = new Dictionary<string, float>();
    private Dictionary<GameObject, Color> originalColors 
        = new Dictionary<GameObject, Color>();
    private Dictionary<GameObject, GazeTimerUI> timerUIs 
        = new Dictionary<GameObject, GazeTimerUI>();
    [Header("Input")]
    [SerializeField] private InputActionReference eyePose;

    [Header("Logging Settings")]
    [SerializeField] private float logIntervalSeconds = 0.05f; 
    [SerializeField] private string fileName = "eye_tracking_data";

    [Header("Fixation Settings")]
    [SerializeField] private float fixationAngleThresholdDeg = 1.5f;  
    [SerializeField] private float fixationDurationThreshold = 0.15f; 

    [Header("Focus Raycasting")]
    [SerializeField] private float gazeRayDistance = 50f;
    [SerializeField] private LayerMask focusLayerMask = ~0; 

    [Header("Distraction Detection")]
    [SerializeField] private string primaryObjectName = "RedCube";

    // Distraction tracking
    private bool  isDistracted         = false;
    private float distractionStartTime = 0f;
    private int   distractionCount     = 0;
    private float totalDistractionTime = 0f;
    private string distractionTarget   = "None";

    // Distraction CSV writer
    private StreamWriter distractionWriter;
    private string       distractionFilePath;
    // Internal state
    private string filePath;
    private StreamWriter writer;
    private float nextLogTime;
    private float sessionStartTime;

    // Pupil dilation baseline
    private float baselineLeftPupil = -1f;
    private float baselineRightPupil = -1f;
    private int baselineSampleCount = 0;
    private const int BASELINE_SAMPLES = 60; // average over first 60 frames

    // Fixation detection
    private Vector3 fixationAnchorDirection = Vector3.zero;
    private float fixationStartTime = -1f;
    private bool isFixating = false;
    private string fixationTargetName = "None";

    void Start()
    {
        sessionStartTime = Time.time;




        GazeTarget[] targets = FindObjectsOfType<GazeTarget>();
        foreach (var t in targets)
        {
            Renderer r = t.GetComponent<Renderer>();
            if (r != null)
                originalColors[t.gameObject] = r.material.color;

            totalAttentionTime[t.gameObject.name] = 0f;

            GazeTimerUI ui = t.GetComponentInChildren<GazeTimerUI>();
            if (ui != null)
                timerUIs[t.gameObject] = ui;
        }
        
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string desktopPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);

        distractionFilePath = Path.Combine(desktopPath,
            $"distractions_{timestamp}.csv");
        distractionWriter = new StreamWriter(
            distractionFilePath, false, Encoding.UTF8);
        distractionWriter.WriteLine(
            "distraction_number," +
            "left_primary_at_s," +
            "distracted_to_object," +
            "returned_at_s," +
            "distraction_duration_s"
        );

        filePath = Path.Combine(desktopPath, $"{fileName}_{timestamp}.csv");

        if (eyePose != null)
                eyePose.action.Enable();
            
            sessionStartTime = Time.time;
        writer = new StreamWriter(filePath, false, Encoding.UTF8);
        WriteHeader();

        Debug.Log($"[EyeTrackingLogger] Saving to: {filePath}");
    }

    void WriteHeader()
    {
        writer.WriteLine(
            "Timestamp_s," +
            "Combined_Gaze_PosX,Combined_Gaze_PosY,Combined_Gaze_PosZ," +
            "Combined_Gaze_DirX,Combined_Gaze_DirY,Combined_Gaze_DirZ," +
            "Left_Gaze_DirX,Left_Gaze_DirY,Left_Gaze_DirZ," +
            "Right_Gaze_DirX,Right_Gaze_DirY,Right_Gaze_DirZ," +
            "Left_Pupil_Diameter_mm,Right_Pupil_Diameter_mm," +
            "Left_Pupil_Dilation_mm,Right_Pupil_Dilation_mm," +
            "Left_Eye_Openness,Right_Eye_Openness," +
            "Is_Fixating,Fixation_Duration_s,Fixation_Target," +
            "Primary_Focus_Object,Primary_Focus_Distance_m," +
            "Secondary_Focus_Object,Secondary_Focus_Distance_m"
        );
    }

    void Update()
    {
        // Debug.Log("Update running: " + Time.time);
        if (Time.time < nextLogTime) return;
        nextLogTime = Time.time + logIntervalSeconds;
        // Debug.Log("Passed time check: " + Time.time);
        float timestamp = Time.time - sessionStartTime;


        var pose = eyePose.action.ReadValue<UnityEngine.InputSystem.XR.PoseState>();
        Vector3 gazeOrigin = pose.position;
        Vector3 gazeDirection = pose.rotation * Vector3.forward;

        if (gazeOrigin == Vector3.zero)
        {
            Camera cam = Camera.main;
            if(cam != null)
            {
                gazeOrigin = cam.transform.position;
                gazeDirection = cam.transform.forward;
            }
        }




        Vector3 leftDir = Vector3.zero, rightDir = Vector3.zero;
        float leftPupil = 0f, rightPupil = 0f;
        float leftOpenness = 0f, rightOpenness = 0f;


        if (leftPupil > 0f)
            Debug.Log($"REAL DATA: Left pupil = {leftPupil}mm");
        else
            Debug.LogWarning("No pupil data yet...");


            
        XR_HTC_eye_tracker.Interop.GetEyeGazeData(out XrSingleEyeGazeDataHTC[] gazes);
        if (gazes != null)
        {
            var left  = gazes[(int)XrEyePositionHTC.XR_EYE_POSITION_LEFT_HTC];
            var right = gazes[(int)XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC];

            if (left.isValid)
                leftDir  = left.gazePose.orientation.ToUnityQuaternion() * Vector3.forward;
            if (right.isValid)
                rightDir = right.gazePose.orientation.ToUnityQuaternion() * Vector3.forward;
        }

        XR_HTC_eye_tracker.Interop.GetEyePupilData(out XrSingleEyePupilDataHTC[] pupils);
        if (pupils != null)
        {
            var lp = pupils[(int)XrEyePositionHTC.XR_EYE_POSITION_LEFT_HTC];
            var rp = pupils[(int)XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC];
            if (lp.isDiameterValid) leftPupil  = lp.pupilDiameter * 1000f; // convert m → mm
            if (rp.isDiameterValid) rightPupil = rp.pupilDiameter * 1000f;
        }

        XR_HTC_eye_tracker.Interop.GetEyeGeometricData(out XrSingleEyeGeometricDataHTC[] openness);
        if (openness != null)
        {
            var lo = openness[(int)XrEyePositionHTC.XR_EYE_POSITION_LEFT_HTC];
            var ro = openness[(int)XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC];
            if (lo.isValid) leftOpenness  = lo.eyeOpenness;
            if (ro.isValid) rightOpenness = ro.eyeOpenness;
        }


        float leftDilation = 0f, rightDilation = 0f;
        if (baselineSampleCount < BASELINE_SAMPLES && leftPupil > 0f)
        {
            // Accumulate baseline
            baselineLeftPupil  = baselineSampleCount == 0 ? leftPupil  : (baselineLeftPupil  + leftPupil)  / 2f;
            baselineRightPupil = baselineSampleCount == 0 ? rightPupil : (baselineRightPupil + rightPupil) / 2f;
            baselineSampleCount++;
        }
        if (baselineSampleCount >= BASELINE_SAMPLES)
        {
            leftDilation  = leftPupil  - baselineLeftPupil;
            rightDilation = rightPupil - baselineRightPupil;
        }


        UpdateFixation(gazeDirection, Time.time);
        float fixationDuration = isFixating ? (Time.time - fixationStartTime) : 0f;


        string primaryObject = "None",   secondaryObject = "None";
        float  primaryDist   = 0f,       secondaryDist   = 0f;
        GetFocusObjects(gazeOrigin, gazeDirection,
            out primaryObject, out primaryDist,
            out secondaryObject, out secondaryDist);


        GameObject hitGameObject = null;
        RaycastHit singleHit;
        if (Physics.Raycast(gazeOrigin, gazeDirection, out singleHit, gazeRayDistance, focusLayerMask))
            hitGameObject = singleHit.collider.gameObject;

        HandleGazeObject(hitGameObject);
        DetectDistraction(primaryObject, timestamp);

        if (currentGazedObject != null && timerUIs.ContainsKey(currentGazedObject))
        {
            float current = Time.time - gazeStartTime;
            float total   = totalAttentionTime.ContainsKey(currentGazedObject.name)
                            ? totalAttentionTime[currentGazedObject.name] : 0f;
            timerUIs[currentGazedObject].UpdateTimer(current, total);
        }


        try
        {
            // Debug.Log($"Writing row at {timestamp:F4}");
            writer.WriteLine(
                $"{timestamp:F4}," +
                $"{gazeOrigin.x:F4},{gazeOrigin.y:F4},{gazeOrigin.z:F4}," +
                $"{gazeDirection.x:F4},{gazeDirection.y:F4},{gazeDirection.z:F4}," +
                $"{leftDir.x:F4},{leftDir.y:F4},{leftDir.z:F4}," +
                $"{rightDir.x:F4},{rightDir.y:F4},{rightDir.z:F4}," +
                $"{leftPupil:F3},{rightPupil:F3}," +
                $"{leftDilation:F3},{rightDilation:F3}," +
                $"{leftOpenness:F3},{rightOpenness:F3}," +
                $"{(isFixating ? 1 : 0)},{fixationDuration:F3},{fixationTargetName}," +
                $"{primaryObject},{primaryDist:F3}," +
                $"{secondaryObject},{secondaryDist:F3}"
            );
            writer.Flush();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error writing to log file: {e.Message}");  
        }
        

    }

    void UpdateFixation(Vector3 currentDir, float now)
    {
        if (fixationAnchorDirection == Vector3.zero)
        {
            fixationAnchorDirection = currentDir;
            fixationStartTime = now;
            return;
        }

        float angle = Vector3.Angle(fixationAnchorDirection, currentDir);

        if (angle < fixationAngleThresholdDeg)
        {

            float duration = now - fixationStartTime;
            if (duration >= fixationDurationThreshold)
            {
                isFixating = true;

            }
        }
        else
        {

            isFixating = false;
            fixationAnchorDirection = currentDir;
            fixationStartTime = now;
            fixationTargetName = "None";
        }
    }

    void GetFocusObjects(Vector3 origin, Vector3 direction,
        out string primaryName, out float primaryDist,
        out string secondaryName, out float secondaryDist)
    {
        primaryName = "None"; primaryDist = 0f;
        secondaryName = "None"; secondaryDist = 0f;

        Ray ray = new Ray(origin, direction);
        RaycastHit[] hits = Physics.RaycastAll(ray, gazeRayDistance, focusLayerMask);


        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        if (hits.Length >= 1)
        {
            primaryName = hits[0].collider.gameObject.name;
            primaryDist = hits[0].distance;

            // Update fixation target
            if (isFixating) fixationTargetName = primaryName;
        }
        if (hits.Length >= 2)
        {
            secondaryName = hits[1].collider.gameObject.name;
            secondaryDist = hits[1].distance;
        }
    }

    void HandleGazeObject(GameObject hitObject)
    {
        if (hitObject == currentGazedObject) return;


        if (currentGazedObject != null)
        {
            Renderer r = currentGazedObject.GetComponent<Renderer>();
            if (r != null && originalColors.ContainsKey(currentGazedObject))
                r.material.color = originalColors[currentGazedObject];

            float duration = Time.time - gazeStartTime;
            if (totalAttentionTime.ContainsKey(currentGazedObject.name))
                totalAttentionTime[currentGazedObject.name] += duration;

            if (timerUIs.ContainsKey(currentGazedObject))
                timerUIs[currentGazedObject].Hide();
        }

        currentGazedObject = hitObject;
        gazeStartTime      = Time.time;


        if (currentGazedObject != null && 
            currentGazedObject.GetComponent<GazeTarget>() != null)
        {
            Renderer r = currentGazedObject.GetComponent<Renderer>();
            if (r != null) r.material.color = highlightColor;
        }
    }

    void DetectDistraction(string currentObject, float timestamp)
    {
        bool onPrimary = currentObject == primaryObjectName;

        if (!isDistracted && !onPrimary && currentObject != "None")
        {
            // Just got distracted — left primary object
            isDistracted         = true;
            distractionStartTime = Time.time;
            distractionTarget    = currentObject;
            Debug.Log($"[Distraction] Left {primaryObjectName} → {currentObject}");
        }
        else if (isDistracted && onPrimary)
        {
            // Returned to primary — distraction ended
            float duration = Time.time - distractionStartTime;
            totalDistractionTime += duration;
            distractionCount++;

            try
            {
                distractionWriter.WriteLine(
                    $"{distractionCount}," +
                    $"{distractionStartTime - sessionStartTime:F4}," +
                    $"{distractionTarget}," +
                    $"{Time.time - sessionStartTime:F4}," +
                    $"{duration:F4}"
                );
                distractionWriter.Flush();
            }
            catch (Exception e)
            {
                Debug.LogError($"Distraction write error: {e.Message}");
            }

            Debug.Log($"[Distraction #{distractionCount}] " +
                    $"Looked at {distractionTarget} for {duration:F2}s");

            isDistracted      = false;
            distractionTarget = "None";
        }
    }

    void OnDestroy()
    {
        if (writer != null)
        {
            writer.Flush();
            writer.Close();
            // Debug.Log($"[EyeTrackingLogger] File saved: {filePath}");
        }
        if (distractionWriter != null)
        {
            distractionWriter.Flush();
            distractionWriter.Close();
        }
        GenerateSessionSummary();
    }

    void GenerateSessionSummary()
    {
        float totalSessionTime = Time.time - sessionStartTime;
        
        // Find most and least attended
        string mostAttended = "None",  leastAttended = "None";
        float  mostTime     = 0f,      leastTime     = float.MaxValue;
        float  totalAttentionAllObjects = 0f;

        foreach (var kvp in totalAttentionTime)
            totalAttentionAllObjects += kvp.Value;

        foreach (var kvp in totalAttentionTime)
        {
            if (kvp.Value > mostTime)
            {
                mostTime     = kvp.Value;
                mostAttended = kvp.Key;
            }
            if (kvp.Value < leastTime)
            {
                leastTime     = kvp.Value;
                leastAttended = kvp.Key;
            }
        }

        // Build summary text
        StringBuilder summary = new StringBuilder();
        summary.AppendLine("========== SESSION SUMMARY ==========");
        summary.AppendLine($"Date:             {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        summary.AppendLine($"Total Duration:   {totalSessionTime:F1}s");
        summary.AppendLine($"Total Distractions: {distractionCount}");
        summary.AppendLine($"Avg Distraction Duration: {(distractionCount > 0 ? totalDistractionTime / distractionCount : 0):F2}s");
        summary.AppendLine("");
        summary.AppendLine("--- Object Attention Breakdown ---");

        foreach (var kvp in totalAttentionTime)
        {
            float percentage = totalAttentionAllObjects > 0
                ? (kvp.Value / totalAttentionAllObjects) * 100f : 0f;
            summary.AppendLine(
                $"{kvp.Key,-20} {kvp.Value:F1}s  ({percentage:F1}%)");
        }

        summary.AppendLine("");
        summary.AppendLine($"Most Attended:    {mostAttended} ({mostTime:F1}s)");
        summary.AppendLine($"Least Attended:   {leastAttended} ({leastTime:F1}s)");
        summary.AppendLine("=====================================");

        // Print to Console
        Debug.Log(summary.ToString());

        // Save to Desktop
        string desktopPath = Environment.GetFolderPath(
                                Environment.SpecialFolder.Desktop);
        string summaryPath = Path.Combine(desktopPath,
            $"session_summary_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
        File.WriteAllText(summaryPath, summary.ToString());
        Debug.Log($"[Summary] Saved to: {summaryPath}");
    }
    public void ForceSave()
    {
        writer?.Flush();
        // Debug.Log("[EyeTrackingLogger] Force saved.");
    }
}