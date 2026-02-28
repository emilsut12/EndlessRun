using UnityEngine;

/// <summary>
/// Central manager for global game settings, state, and shared references.
/// </summary>
public class GameManager : MonoBehaviour
{
    // The single instance of this class
    public static GameManager Instance { get; private set; }

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

    private void Awake()
    {
        // Enforce the Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            // Keeps the GameManager alive if you switch or reload scenes
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return; // Stop execution if this is a duplicate
        }
    }

    private void Start()
    {
        // Throw an error immediately if setup is incomplete
        if (playerTransform == null)
        {
            Debug.LogError("GameManager: Player Transform is not assigned in the Inspector!");
        }
    }
}