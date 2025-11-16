using UnityEngine;
using UnityEngine.UI;

public class TESUI : MonoBehaviour
{
    [Header("Splash Screen")]
    [Tooltip("Splash panel GameObject (should contain progress bar)")]
    public GameObject _splashPanel;
    
    [Tooltip("Progress bar slider (optional, for showing loading progress)")]
    public UnityEngine.UI.Slider _progressBar;
    
    [Tooltip("Progress text (optional, for showing loading status)")]
    public UnityEngine.UI.Text _progressText;
    
    private bool _isInitialized = false;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Show splash screen by default (will be hidden when loading completes)
        ShowSplash();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    
    /// <summary>
    /// Shows the splash screen
    /// </summary>
    public void ShowSplash()
    {
        if (_splashPanel != null)
        {
            _splashPanel.SetActive(true);
            Debug.Log("TESUI: Splash panel shown");
        }
        else
        {
            Debug.LogWarning("TESUI: Splash panel is not assigned!");
        }
        
        // Reset progress bar if available
        if (_progressBar != null)
        {
            _progressBar.value = 0f;
        }
        
        // Clear progress text if available
        if (_progressText != null)
        {
            _progressText.text = "Loading...";
        }
        
        // Force UI update
        Canvas.ForceUpdateCanvases();
    }
    
    /// <summary>
    /// Hides the splash screen
    /// </summary>
    public void HideSplash()
    {
        if (_splashPanel != null)
        {
            _splashPanel.SetActive(false);
            Debug.Log("TESUI: Splash panel hidden");
            // Force UI update
            Canvas.ForceUpdateCanvases();
        }
        else
        {
            Debug.LogWarning("TESUI: Splash panel is not assigned, cannot hide!");
        }
    }
    
    /// <summary>
    /// Updates the progress bar (0.0 to 1.0)
    /// </summary>
    /// <param name="progress">Progress value from 0.0 to 1.0</param>
    public void SetProgress(float progress)
    {
        if (_progressBar != null)
        {
            _progressBar.value = Mathf.Clamp01(progress);
            // Force UI update
            Canvas.ForceUpdateCanvases();
        }
        else
        {
            Debug.LogWarning("TESUI: Progress bar is not assigned!");
        }
    }
    
    /// <summary>
    /// Updates the progress text
    /// </summary>
    /// <param name="text">Status text to display</param>
    public void SetProgressText(string text)
    {
        if (_progressText != null)
        {
            _progressText.text = text;
            // Force UI update
            Canvas.ForceUpdateCanvases();
        }
        else
        {
            Debug.LogWarning("TESUI: Progress text is not assigned!");
        }
    }
    
    /// <summary>
    /// Updates both progress bar and text
    /// </summary>
    /// <param name="progress">Progress value from 0.0 to 1.0</param>
    /// <param name="text">Status text to display</param>
    public void UpdateProgress(float progress, string text)
    {
        SetProgress(progress);
        SetProgressText(text);
        Debug.Log($"TESUI: Progress updated - {progress * 100f:F1}% - {text}");
    }
}
