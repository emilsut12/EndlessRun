using UnityEngine;
using UnityEngine.SceneManagement;

// Central manager for game state, speed progression, lives, UI panels, and display settings.
// Singleton — access via GameManager.Instance from any script.
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { MainMenu, Playing, GameOver }
    public GameState CurrentState { get; private set; } = GameState.MainMenu;

    [Header("Global References")]
    public Transform playerTransform;

    [Tooltip("How many ground tiles to keep spawned ahead of the player.")]
    [Range(5, 50)]
    public int renderDistance = 25;

    [Header("Testing & Gameplay Settings")]
    [Tooltip("Player phases through obstacles when enabled.")]
    public bool isGhost = false;

    [Tooltip("Global speed multiplier applied to forward movement and obstacle scroll.")]
    [Range(0.1f, 5f)]
    public float gameSpeed = 1.0f;

    [Tooltip("Lives the player starts each run with.")]
    public int startingLives = 3;
    public int CurrentLives { get; private set; }

    [Header("Speed Settings")]
    public float baseSpeed = 10f;
    public float currentSpeed;
    public float maxSpeed = 30f;

    [Tooltip("Speed gained per second at full health.")]
    public float speedIncreaseRate = 0.25f;

    [Tooltip("Flat speed penalty when the player loses a life.")]
    public float speedPenaltyOnLifeLost = 5f;

    [Header("Display Settings")]
    [Tooltip("Resolution width used when launching in windowed mode.")]
    public int windowedWidth = 1280;
    [Tooltip("Resolution height used when launching in windowed mode.")]
    public int windowedHeight = 720;

    [Header("UI Panels")]
    public GameObject startScreen;
    public GameObject gameOverScreen;

    // Tracks whether the display mode has already been set this application session.
    // Static so it survives scene reloads (SceneManager.LoadScene destroys instances).
    private static bool _displayInitialized = false;

    private const string PrefKeyFullscreen = "IsFullscreen";

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (playerTransform == null) Debug.LogError("GameManager: Player Transform is not assigned!");

        CurrentLives = startingLives;
        currentSpeed = baseSpeed;

        // Only apply display settings on the very first scene load of the session.
        // This prevents F11 fullscreen from being reverted when the scene reloads after death.
        if (!_displayInitialized)
        {
            _displayInitialized = true;

            // Restore the player's last fullscreen preference, default to windowed
            if (PlayerPrefs.GetInt(PrefKeyFullscreen, 0) == 1)
                Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, FullScreenMode.FullScreenWindow);
            else
                Screen.SetResolution(windowedWidth, windowedHeight, FullScreenMode.Windowed);
        }

        ShowMainMenu();
    }

    private void Update()
    {
        // F11 toggles between windowed and borderless fullscreen, persisted across runs
        if (Input.GetKeyDown(KeyCode.F11))
        {
            if (Screen.fullScreen)
            {
                Screen.SetResolution(windowedWidth, windowedHeight, FullScreenMode.Windowed);
                PlayerPrefs.SetInt(PrefKeyFullscreen, 0);
            }
            else
            {
                Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, FullScreenMode.FullScreenWindow);
                PlayerPrefs.SetInt(PrefKeyFullscreen, 1);
            }
        }

        if (CurrentState == GameState.GameOver && Input.anyKeyDown)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
        else if (CurrentState == GameState.Playing)
        {
            if (currentSpeed < maxSpeed)
            {
                // Speed ramps faster when the player has fewer lives
                float lifeMultiplier = (float)startingLives / Mathf.Max(1, CurrentLives);
                float dynamicIncreaseRate = speedIncreaseRate * lifeMultiplier;

                currentSpeed += dynamicIncreaseRate * Time.deltaTime;
                currentSpeed = Mathf.Min(currentSpeed, maxSpeed);
            }
        }
    }

    public void ShowMainMenu()
    {
        CurrentState = GameState.MainMenu;

        if (startScreen != null) startScreen.SetActive(true);
        if (gameOverScreen != null) gameOverScreen.SetActive(false);

        Time.timeScale = 0f;
    }

    public void StartGame()
    {
        CurrentState = GameState.Playing;

        if (startScreen != null) startScreen.SetActive(false);
        if (gameOverScreen != null) gameOverScreen.SetActive(false);

        Time.timeScale = 1f;
    }

    public void LoseLife()
    {
        CurrentLives--;
        Debug.Log("Lost a life! Lives remaining: " + CurrentLives);

        currentSpeed -= speedPenaltyOnLifeLost;
        currentSpeed = Mathf.Max(currentSpeed, baseSpeed);
    }

    public void TriggerGameOver()
    {
        if (CurrentState == GameState.GameOver) return;

        CurrentState = GameState.GameOver;
        if (gameOverScreen != null) gameOverScreen.SetActive(true);
    }

    public void QuitGame()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    public void OpenSettings()
    {
        Debug.Log("Settings menu not implemented yet!");
    }
}