using UnityEngine;
using TMPro;

public class GazeTimerUI : MonoBehaviour
{
    [SerializeField] public TextMeshProUGUI timerText;
    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;
        Hide();
    }

    void LateUpdate()
    {
        if (mainCamera != null)
        {
            transform.LookAt(
                transform.position + mainCamera.transform.rotation * Vector3.forward, 
                mainCamera.transform.rotation * Vector3.up
            );
        }
    }

    public void UpdateTimer(float current, float total)
    {
        gameObject.SetActive(true);
        timerText.text = $"Now: {current:F1}s \nTotal: {total:F1} seconds";
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}