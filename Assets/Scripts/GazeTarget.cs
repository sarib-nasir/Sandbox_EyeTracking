using UnityEngine;

public class GazeTarget : MonoBehaviour
{
    [Header("Object Info")]
    public string objectLabel = "";
    void Start()
    {
        if (string.IsNullOrEmpty(objectLabel))
        {
            objectLabel = gameObject.name;
        }
        Debug.Log("Gaze target clicked: " + objectLabel);
    }
}