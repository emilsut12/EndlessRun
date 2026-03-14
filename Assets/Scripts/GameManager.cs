using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Central manager for global game settings, state, and shared references.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { MainMenu, Playing, GameOver }
    public GameState CurrentState { get; private set; } = GameState.MainMenu;

    [Header("Global References")]
    [Tooltip("Master reference to the player object.")]
    public Transform playerTransform;

    [Tooltip("The number of ground tiles to spawn ahead of the player.")]
    [Range(5, 50)]
    public int renderDistance = 15;

    [Header("Testing & Gameplay Settings")]
    [Tooltip("If true, the player will phase through obstacles.")]
    public bool isGhost = false;

    [Tooltip("Global speed multiplier for the game environment.")]
    [Range(0.1f, 5f)]
    public float gameSpeed = 1.0f;

    [Tooltip("Amount of lives the player starts with.")]
    public int startingLives = 3;
    public int CurrentLives { get; private set; }

    [Header("UI Panels")]
    public GameObject startScreen;
    public GameObject gameOverScreen;

    private void Awake()
    {
        // Enforce Singleton
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        if (playerTransform == null) Debug.LogError("GameManager: Player Transform is not assigned!");

        // Initialize lives
        CurrentLives = startingLives;

        // Start the game in the menu state
        ShowMainMenu();
    }

    private void Update()
    {
        // If game over, wait for any key press to restart
        if (CurrentState == GameState.GameOver && Input.anyKeyDown)
        {
            // Reload the current scene
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }

    public void ShowMainMenu()
    {
        CurrentState = GameState.MainMenu;

        if (startScreen != null) startScreen.SetActive(true);
        if (gameOverScreen != null) gameOverScreen.SetActive(false);

        // Freeze gameplay while the menu is up
        Time.timeScale = 0f;
    }

    public void StartGame()
    {
        CurrentState = GameState.Playing;

        if (startScreen != null) startScreen.SetActive(false);
        if (gameOverScreen != null) gameOverScreen.SetActive(false);

        // Unfreeze gameplay
        Time.timeScale = 1f;
    }

    // New method to handle taking damage
    public void LoseLife()
    {
        CurrentLives--;
        Debug.Log("Lost a life! Lives remaining: " + CurrentLives);
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