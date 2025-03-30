using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class BackgroundLoader : MonoBehaviour
{
    [SerializeField] private string gameSceneName = "Default Scene";
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private GameObject startButton;

    private AsyncOperation asyncLoad;
    private bool isLoading = false;
    private bool loadComplete = false;


    # region Unity Lifecycle Functions

    void Start()
    {
        UpdateProgressBar(0f);
        SetupButtonCallback();
        StartBackgroundLoading();
    }


    void Update()
    {
        if (isLoading && !loadComplete)
        {
            // Calculate progress (AsyncOperation.progress goes from 0 to 0.9)
            float progress = asyncLoad.progress / 0.9f;
            UpdateProgressBar(progress);

            if (asyncLoad.progress >= 0.9f)
            {
                loadComplete = true;
                startButton.SetActive(true); // Enable the start button
            }
        }
    }

    # endregion


    void SetupButtonCallback()
    {
        if (startButton != null)
        {
            UnityEngine.UI.Button button = startButton.GetComponent<UnityEngine.UI.Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners(); // Clear existing listeners
                button.onClick.AddListener(StartGame);
                Debug.Log("Button listener added programmatically");
            }
        }

    }


    private void StartBackgroundLoading()
    {
        try
        {
            // Start loading the game scene in background
            asyncLoad = SceneManager.LoadSceneAsync(gameSceneName);

            if (asyncLoad == null)
            {
                Debug.LogError($"Scene '{gameSceneName}' not found in build settings");
                progressText.text = "Error loading scene";
                return;
            }

            asyncLoad.allowSceneActivation = false; // Don't activate until ready
            isLoading = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error loading scene: {e.Message}");
            progressText.text = "Error loading scene";
        }
    }


    private void UpdateProgressBar(float progress)
    {
        // Map progress (0-1) to number of filled characters (0-10)
        int filledChars = Mathf.FloorToInt(progress * 10);

        // Build the progress bar string
        char[] progressBar = new char[12]; // 10 chars + 2 brackets
        progressBar[0] = '[';

        for (int i = 0; i < 10; i++) progressBar[i + 1] = i < filledChars ? '#' : '.';
        progressBar[11] = ']';
        progressText.text = new string(progressBar);
    }


    private void StartGame()
    {
        Debug.Log("StartGame method called");

        if (loadComplete && asyncLoad != null)
        {
            Debug.Log("Activating scene");
            asyncLoad.allowSceneActivation = true;
        }
        else
        {
            Debug.Log("Scene not loaded or asyncLoad is null");
        }
    }
}