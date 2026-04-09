using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// Central manager for game state, speed progression, lives, UI panels, and display settings.
// Singleton — access via GameManager.Instance from any script.
public class GameManager : MonoBehaviour
{
    [Serializable]
    private class LeaderboardEntry
    {
        public string playerName;
        public int score;
    }

    [Serializable]
    private class LeaderboardData
    {
        public List<LeaderboardEntry> entries = new List<LeaderboardEntry>();
    }

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

    [Header("Webcam Tracking")]
    [Tooltip("When enabled, uses ML pose estimation (Sentis + MoveNet) instead of " +
             "background subtraction for webcam tracking.")]
    public bool useMLPoseTracking = false;

    [Tooltip("The background-subtraction webcam provider.")]
    [SerializeField] private WebcamInputProvider _webcamInput;

    [Tooltip("The ML pose estimation provider.")]
    [SerializeField] private PoseInputProvider _poseInput;
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

    [Header("UI References")]
    [SerializeField] private GameObject _livesHudRoot;
    [SerializeField] private GameObject _scoreHudRoot;
    [SerializeField] private TMP_Text _livesCounterText;
    [SerializeField] private TMP_Text _scoreCounterText;
    [SerializeField] private TMP_Text _finalScoreText;
    [SerializeField] private TMP_Text _leaderboardText;
    [SerializeField] private TMP_Text _nameEntryText;
    [SerializeField] private TMP_Text _saveScoreButtonLabel;
    [SerializeField] private Button _saveScoreButton;
    [SerializeField] private Button _restartButton;
    [SerializeField] private GameObject _continuePrompt;
    [SerializeField] private TMP_Text _continuePromptText;

    public int CurrentScore { get; private set; }

    // Tracks whether the display mode has already been set this application session.
    // Static so it survives scene reloads (SceneManager.LoadScene destroys instances).
    private static bool _displayInitialized = false;

    private const string PrefKeyFullscreen = "IsFullscreen";
    private const int MaxLeaderboardEntries = 3;
    private const int LeaderboardVisibleNameChars = 30;
    private const float LeaderboardScrollSpeed = 3f;
    private const string LeaderboardScrollGap = "   ";

    private Canvas _mainCanvas;
    private GraphicRaycaster _graphicRaycaster;
    private EventSystem _eventSystem;
    private InputSystemUIInputModule _uiInputModule;
    private TMP_FontAsset _uiFontAsset;
    private Sprite _uiSprite;

    private Button _hoveredButton;
    private Button _pressedButton;

    private float _runStartZ;
    private string _enteredPlayerName = string.Empty;
    private bool _scoreSubmitted;
    private LeaderboardData _cachedLeaderboardData = new LeaderboardData();

    private string LeaderboardFilePath => Path.Combine(Application.persistentDataPath, "leaderboard.json");

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        ApplyTrackingMode();
    }

    /// <summary>
    /// Enables the correct webcam tracking provider based on <see cref="useMLPoseTracking"/>.
    /// Safe to call at any time — disables the inactive provider so only one uses the camera.
    /// </summary>
    public void ApplyTrackingMode()
    {
        if (_poseInput != null)
            _poseInput.enabled = useMLPoseTracking;

        if (_webcamInput != null)
            _webcamInput.enabled = !useMLPoseTracking;

        Debug.Log($"[GameManager] Tracking mode: {(useMLPoseTracking ? "ML Pose (Sentis)" : "Background Subtraction")}");
    }

    private void Start()
    {
        if (playerTransform == null) Debug.LogError("GameManager: Player Transform is not assigned!");

        CurrentLives = startingLives;
        currentSpeed = baseSpeed;
        CurrentScore = 0;
        _runStartZ = playerTransform != null ? playerTransform.position.z : 0f;

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

        InitializeRuntimeUi();

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

        HandleMenuButtonInput();

        if (CurrentState == GameState.Playing)
        {
            UpdateCurrentScore();

            if (currentSpeed < maxSpeed)
            {
                // Speed ramps faster when the player has fewer lives
                float lifeMultiplier = (float)startingLives / Mathf.Max(1, CurrentLives);
                float dynamicIncreaseRate = speedIncreaseRate * lifeMultiplier;

                currentSpeed += dynamicIncreaseRate * Time.deltaTime;
                currentSpeed = Mathf.Min(currentSpeed, maxSpeed);
            }
        }

        if (CurrentState == GameState.GameOver)
        {
            UpdateAnimatedLeaderboardText();
            HandleNameEntryInput();
        }
    }

    public void ShowMainMenu()
    {
        CurrentState = GameState.MainMenu;

        if (startScreen != null) startScreen.SetActive(true);
        if (gameOverScreen != null) gameOverScreen.SetActive(false);

        ClearButtonPointerState();
        SetLivesHudVisible(false);

        Time.timeScale = 0f;
    }

    public void StartGame()
    {
        CurrentState = GameState.Playing;
        CurrentLives = startingLives;
        currentSpeed = baseSpeed;
        CurrentScore = 0;
        _runStartZ = playerTransform != null ? playerTransform.position.z : 0f;
        _enteredPlayerName = string.Empty;
        _scoreSubmitted = false;

        if (startScreen != null) startScreen.SetActive(false);
        if (gameOverScreen != null) gameOverScreen.SetActive(false);

        ClearButtonPointerState();
        UpdateLivesHud();
        UpdateGameOverUi();
        SetLivesHudVisible(true);

        Time.timeScale = 1f;

        // Re-calibrate pose tracking baseline at the start of each run
        if (_poseInput != null && _poseInput.isActiveAndEnabled)
            _poseInput.Recalibrate();
    }

    public void LoseLife()
    {
        CurrentLives = Mathf.Max(0, CurrentLives - 1);
        Debug.Log("Lost a life! Lives remaining: " + CurrentLives);

        currentSpeed -= speedPenaltyOnLifeLost;
        currentSpeed = Mathf.Max(currentSpeed, baseSpeed);

        UpdateLivesHud();
    }

    public void TriggerGameOver()
    {
        if (CurrentState == GameState.GameOver) return;

        CurrentState = GameState.GameOver;
        UpdateCurrentScore();
        _enteredPlayerName = string.Empty;
        _scoreSubmitted = false;
        RefreshLeaderboardCache();

        if (gameOverScreen != null) gameOverScreen.SetActive(true);
        if (_continuePrompt != null) _continuePrompt.SetActive(true);

        ClearButtonPointerState();
        SetLivesHudVisible(false);
        UpdateGameOverUi();

        Time.timeScale = 0f;
    }

    public void RestartGame()
    {
        // Release webcam hardware before reloading the scene so the new
        // providers don't race with the OS device handle still being held.
        if (_poseInput != null)  _poseInput.enabled  = false;
        if (_webcamInput != null) _webcamInput.enabled = false;

        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
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

    private void InitializeRuntimeUi()
    {
        // Find the main UI canvas (Display 0) — not a runtime-created overlay canvas
        // on another display (e.g. WebcamDisplay3's canvas on Display 2/3).
        _mainCanvas = null;
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas.targetDisplay == 0 && canvas.GetComponent<GraphicRaycaster>() != null)
            {
                _mainCanvas = canvas;
                break;
            }
        }
        _graphicRaycaster = _mainCanvas != null ? _mainCanvas.GetComponent<GraphicRaycaster>() : null;
        _eventSystem = FindFirstObjectByType<EventSystem>();
        _uiInputModule = FindFirstObjectByType<InputSystemUIInputModule>();

        if (_uiInputModule != null)
            _uiInputModule.enabled = false;

        if (_mainCanvas == null)
        {
            Debug.LogError("GameManager: Canvas not found, runtime UI could not be created.");
            return;
        }

        _uiFontAsset = TMP_Settings.defaultFontAsset;
        _uiSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");

        if (_uiFontAsset == null)
            Debug.LogError("GameManager: TMP default font asset is missing.");

        TryAssignEditorUiReferences();
        RefreshLeaderboardCache();
        BindRuntimeButtonCallbacks();
        UpdateLivesHud();
        UpdateScoreHud();
        UpdateGameOverUi();
    }

    private void BindRuntimeButtonCallbacks()
    {
        if (_saveScoreButton != null)
        {
            _saveScoreButton.onClick.RemoveListener(SaveScore);
            _saveScoreButton.onClick.AddListener(SaveScore);
        }

        if (_restartButton != null)
        {
            _restartButton.onClick.RemoveListener(RestartGame);
            _restartButton.onClick.AddListener(RestartGame);
        }
    }

    private void UpdateCurrentScore()
    {
        if (playerTransform == null)
        {
            CurrentScore = 0;
            return;
        }

        CurrentScore = Mathf.Max(0, Mathf.FloorToInt(playerTransform.position.z - _runStartZ));
        UpdateScoreHud();
        if (_finalScoreText != null && CurrentState == GameState.GameOver)
            _finalScoreText.text = $"Score: {CurrentScore}";
    }

    private void UpdateLivesHud()
    {
        if (_livesCounterText == null) return;
        _livesCounterText.text = $"<color=#D95A66>\u2665</color> {CurrentLives}";
    }

    private void UpdateScoreHud()
    {
        if (_scoreCounterText == null) return;
        _scoreCounterText.text = $"Score: {CurrentScore}";
    }

    private void SetLivesHudVisible(bool isVisible)
    {
        if (_livesHudRoot != null)
            _livesHudRoot.SetActive(isVisible);

        if (_scoreHudRoot != null)
            _scoreHudRoot.SetActive(isVisible);
    }

    private void UpdateGameOverUi()
    {
        if (_finalScoreText != null)
            _finalScoreText.text = $"Score: {CurrentScore}";

        if (_continuePromptText != null)
            _continuePromptText.text = "Save score, then click Restart";

        if (_leaderboardText != null)
            _leaderboardText.text = BuildLeaderboardText();

        if (_nameEntryText != null)
        {
            if (_scoreSubmitted)
            {
                _nameEntryText.text = $"Saved as {SanitizeName(_enteredPlayerName)}";
                _nameEntryText.color = new Color(0.2f, 0.23f, 0.32f);
            }
            else if (string.IsNullOrEmpty(_enteredPlayerName))
            {
                _nameEntryText.text = "YOUR NAME";
                _nameEntryText.color = new Color(0.52f, 0.54f, 0.6f);
            }
            else
            {
                _nameEntryText.text = _enteredPlayerName;
                _nameEntryText.color = new Color(0.2f, 0.23f, 0.32f);
            }
        }

        if (_saveScoreButton != null)
            _saveScoreButton.interactable = !_scoreSubmitted;

        if (_saveScoreButtonLabel != null)
            _saveScoreButtonLabel.text = _scoreSubmitted ? "Saved" : "Save Score";
    }

    private void HandleNameEntryInput()
    {
        if (_scoreSubmitted) return;

        bool changed = false;
        string typedCharacters = Input.inputString;

        for (int i = 0; i < typedCharacters.Length; i++)
        {
            char currentCharacter = typedCharacters[i];

            if (currentCharacter == '\b')
            {
                if (_enteredPlayerName.Length > 0)
                {
                    _enteredPlayerName = _enteredPlayerName.Substring(0, _enteredPlayerName.Length - 1);
                    changed = true;
                }

                continue;
            }

            if (currentCharacter == '\n' || currentCharacter == '\r')
            {
                SaveScore();
                return;
            }

            if (!char.IsControl(currentCharacter) && IsAllowedNameCharacter(currentCharacter))
            {
                _enteredPlayerName += currentCharacter;
                changed = true;
            }
        }

        if (changed)
            UpdateGameOverUi();
    }

    private static bool IsAllowedNameCharacter(char currentCharacter)
    {
        return char.IsLetterOrDigit(currentCharacter) || currentCharacter == ' ' || currentCharacter == '-' || currentCharacter == '_';
    }

    private void SaveScore()
    {
        if (_scoreSubmitted) return;

        string playerName = SanitizeName(_enteredPlayerName);
        LeaderboardData leaderboardData = LoadLeaderboard();
        leaderboardData.entries.Add(new LeaderboardEntry
        {
            playerName = playerName,
            score = CurrentScore,
        });

        leaderboardData.entries.Sort((left, right) => right.score.CompareTo(left.score));
        if (leaderboardData.entries.Count > MaxLeaderboardEntries)
            leaderboardData.entries.RemoveRange(MaxLeaderboardEntries, leaderboardData.entries.Count - MaxLeaderboardEntries);

        SaveLeaderboard(leaderboardData);
        _cachedLeaderboardData = leaderboardData;

        _enteredPlayerName = playerName;
        _scoreSubmitted = true;
        UpdateGameOverUi();
    }

    private LeaderboardData LoadLeaderboard()
    {
        if (File.Exists(LeaderboardFilePath))
        {
            string serializedData = File.ReadAllText(LeaderboardFilePath);
            if (!string.IsNullOrWhiteSpace(serializedData))
            {
                LeaderboardData fileData = JsonUtility.FromJson<LeaderboardData>(serializedData);
                if (fileData != null)
                    return fileData;
            }
        }

        // One-time migration path from older PlayerPrefs storage.
        string legacySerializedData = PlayerPrefs.GetString("LocalLeaderboard", string.Empty);
        if (string.IsNullOrEmpty(legacySerializedData))
            return new LeaderboardData();

        LeaderboardData legacyData = JsonUtility.FromJson<LeaderboardData>(legacySerializedData) ?? new LeaderboardData();
        SaveLeaderboard(legacyData);
        PlayerPrefs.DeleteKey("LocalLeaderboard");
        return legacyData;
    }

    private void SaveLeaderboard(LeaderboardData leaderboardData)
    {
        string serializedData = JsonUtility.ToJson(leaderboardData);
        Directory.CreateDirectory(Application.persistentDataPath);
        File.WriteAllText(LeaderboardFilePath, serializedData);
    }

    private void RefreshLeaderboardCache()
    {
        _cachedLeaderboardData = LoadLeaderboard();
    }

    private string BuildLeaderboardText()
    {
        LeaderboardData leaderboardData = _cachedLeaderboardData ?? new LeaderboardData();
        List<string> lines = new List<string>(MaxLeaderboardEntries);

        for (int index = 0; index < MaxLeaderboardEntries; index++)
        {
            if (index < leaderboardData.entries.Count)
            {
                LeaderboardEntry entry = leaderboardData.entries[index];
                lines.Add($"{index + 1}. {FormatLeaderboardName(entry.playerName)} - {entry.score}");
            }
            else
            {
                lines.Add($"{index + 1}. --- - 0");
            }
        }

        return string.Join("\n", lines);
    }

    private static string SanitizeName(string playerName)
    {
        return string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim();
    }

    private void UpdateAnimatedLeaderboardText()
    {
        if (_leaderboardText != null)
            _leaderboardText.text = BuildLeaderboardText();
    }

    private string FormatLeaderboardName(string playerName)
    {
        string sanitizedName = SanitizeName(playerName);
        if (sanitizedName.Length <= LeaderboardVisibleNameChars)
            return sanitizedName.PadRight(LeaderboardVisibleNameChars);

        string scrollSource = sanitizedName + LeaderboardScrollGap;
        int scrollOffset = Mathf.FloorToInt(Time.unscaledTime * LeaderboardScrollSpeed) % scrollSource.Length;

        if (scrollOffset + LeaderboardVisibleNameChars <= scrollSource.Length)
            return scrollSource.Substring(scrollOffset, LeaderboardVisibleNameChars);

        int tailLength = scrollSource.Length - scrollOffset;
        return scrollSource.Substring(scrollOffset, tailLength) + scrollSource.Substring(0, LeaderboardVisibleNameChars - tailLength);
    }

    private void HandleMenuButtonInput()
    {
        if (CurrentState == GameState.Playing || _graphicRaycaster == null || _eventSystem == null)
            return;

        if (!TryGetCanvasPointerPosition(out Vector2 pointerPosition))
        {
            if (Input.GetMouseButtonUp(0) && _pressedButton != null)
                ReleasePressedButton(null, pointerPosition);

            if (_hoveredButton != null)
            {
                PointerEventData exitEvent = CreatePointerEvent(pointerPosition);
                ExecuteEvents.Execute(_hoveredButton.gameObject, exitEvent, ExecuteEvents.pointerExitHandler);
                _hoveredButton = null;
            }

            return;
        }

        Button buttonUnderPointer = RaycastButton(pointerPosition);

        if (buttonUnderPointer != _hoveredButton)
        {
            if (_hoveredButton != null)
            {
                PointerEventData exitEvent = CreatePointerEvent(pointerPosition);
                ExecuteEvents.Execute(_hoveredButton.gameObject, exitEvent, ExecuteEvents.pointerExitHandler);
            }

            _hoveredButton = buttonUnderPointer;

            if (_hoveredButton != null)
            {
                PointerEventData enterEvent = CreatePointerEvent(pointerPosition);
                ExecuteEvents.Execute(_hoveredButton.gameObject, enterEvent, ExecuteEvents.pointerEnterHandler);
            }
        }

        if (Input.GetMouseButtonDown(0) && buttonUnderPointer != null && buttonUnderPointer.interactable)
        {
            _pressedButton = buttonUnderPointer;
            PointerEventData downEvent = CreatePointerEvent(pointerPosition);
            ExecuteEvents.Execute(_pressedButton.gameObject, downEvent, ExecuteEvents.pointerDownHandler);
        }

        if (Input.GetMouseButtonUp(0) && _pressedButton != null)
            ReleasePressedButton(buttonUnderPointer, pointerPosition);
    }

    private void ReleasePressedButton(Button buttonUnderPointer, Vector2 pointerPosition)
    {
        if (_pressedButton == null)
            return;

        Button buttonToRelease = _pressedButton;
        _pressedButton = null;

        PointerEventData upEvent = CreatePointerEvent(pointerPosition);
        ExecuteEvents.Execute(buttonToRelease.gameObject, upEvent, ExecuteEvents.pointerUpHandler);

        if (buttonUnderPointer == buttonToRelease && buttonToRelease.interactable)
            buttonToRelease.onClick.Invoke();
    }

    private void ClearButtonPointerState()
    {
        _hoveredButton = null;
        _pressedButton = null;
    }

    private bool TryGetCanvasPointerPosition(out Vector2 pointerPosition)
    {
        pointerPosition = Vector2.zero;
        if (_mainCanvas == null)
            return false;

        Vector3 relativeMouse = Display.RelativeMouseAt(Input.mousePosition);
        bool hasRelativeMouse = relativeMouse != Vector3.zero;

        if (hasRelativeMouse)
        {
            if ((int)relativeMouse.z != _mainCanvas.targetDisplay)
                return false;

            pointerPosition = new Vector2(relativeMouse.x, relativeMouse.y);
            return pointerPosition.x >= 0f && pointerPosition.y >= 0f;
        }

        if (Display.displays.Length > 1 && _mainCanvas.targetDisplay != 0)
            return false;

        pointerPosition = Input.mousePosition;
        return pointerPosition.x >= 0f && pointerPosition.y >= 0f && pointerPosition.x <= Screen.width && pointerPosition.y <= Screen.height;
    }

    private PointerEventData CreatePointerEvent(Vector2 pointerPosition)
    {
        return new PointerEventData(_eventSystem)
        {
            position = pointerPosition,
            button = PointerEventData.InputButton.Left,
        };
    }

    private Button RaycastButton(Vector2 pointerPosition)
    {
        PointerEventData pointerEvent = CreatePointerEvent(pointerPosition);
        List<RaycastResult> raycastResults = new List<RaycastResult>();
        _graphicRaycaster.Raycast(pointerEvent, raycastResults);

        for (int index = 0; index < raycastResults.Count; index++)
        {
            Button button = raycastResults[index].gameObject.GetComponentInParent<Button>();
            if (button != null)
                return button;
        }

        return null;
    }

    private void TryAssignEditorUiReferences()
    {
        if (_mainCanvas == null)
            return;

        if (_livesHudRoot == null)
        {
            Transform livesHudTransform = FindNamedDescendant(_mainCanvas.transform, "LivesHud");
            if (livesHudTransform != null)
                _livesHudRoot = livesHudTransform.gameObject;
        }

        if (_livesCounterText == null && _livesHudRoot != null)
            _livesCounterText = FindNamedComponentInChildren<TMP_Text>(_livesHudRoot.transform, "LivesCounter")
                ?? _livesHudRoot.GetComponentInChildren<TMP_Text>(true);

        if (_livesHudRoot == null && _livesCounterText != null)
            _livesHudRoot = ResolveCanvasRoot(_livesCounterText.transform);

        if (_scoreHudRoot == null)
        {
            if (_scoreCounterText != null)
            {
                _scoreHudRoot = ResolveCanvasRoot(_scoreCounterText.transform);
            }
            else
            {
                Transform scoreHudTransform = FindNamedDescendant(_mainCanvas.transform, "ScoreHud")
                    ?? FindNamedDescendant(_mainCanvas.transform, "Score");
                if (scoreHudTransform != null)
                    _scoreHudRoot = scoreHudTransform.gameObject;
            }
        }

        if (_scoreCounterText == null)
        {
            Transform scoreSearchRoot = _scoreHudRoot != null ? _scoreHudRoot.transform : _mainCanvas.transform;
            Transform scoreTransform = FindNamedDescendant(scoreSearchRoot, "ScoreCounter");
            if (scoreTransform != null)
                _scoreCounterText = scoreTransform.GetComponent<TMP_Text>();
        }

        if (_scoreHudRoot == null && _scoreCounterText != null)
            _scoreHudRoot = ResolveCanvasRoot(_scoreCounterText.transform);

        if (gameOverScreen == null)
            return;

        AssignTextReference(ref _finalScoreText, gameOverScreen.transform, "FinalScoreText");
        AssignTextReference(ref _leaderboardText, gameOverScreen.transform, "LeaderboardText");
        AssignTextReference(ref _nameEntryText, gameOverScreen.transform, "NameEntryText");

        if (_saveScoreButton == null)
        {
            Transform saveButtonTransform = FindNamedDescendant(gameOverScreen.transform, "SaveScoreButton");
            if (saveButtonTransform != null)
                _saveScoreButton = saveButtonTransform.GetComponent<Button>();
        }

        if (_restartButton == null)
        {
            Transform restartButtonTransform = FindNamedDescendant(gameOverScreen.transform, "RestartButton");
            if (restartButtonTransform != null)
                _restartButton = restartButtonTransform.GetComponent<Button>();
        }

        if (_saveScoreButtonLabel == null && _saveScoreButton != null)
            _saveScoreButtonLabel = _saveScoreButton.GetComponentInChildren<TMP_Text>(true);

        if (_continuePrompt == null)
        {
            Transform continuePromptTransform = FindNamedDescendant(gameOverScreen.transform, "Continue");
            if (continuePromptTransform != null)
                _continuePrompt = continuePromptTransform.gameObject;
        }

        if (_continuePromptText == null && _continuePrompt != null)
            _continuePromptText = _continuePrompt.GetComponentInChildren<TMP_Text>(true);
    }

    private static void AssignTextReference(ref TMP_Text targetText, Transform root, string childName)
    {
        if (targetText != null || root == null)
            return;

        Transform childTransform = FindNamedDescendant(root, childName);
        if (childTransform != null)
            targetText = childTransform.GetComponent<TMP_Text>();
    }

    private static Transform FindNamedDescendant(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < descendants.Length; index++)
        {
            if (descendants[index].name == childName)
                return descendants[index];
        }

        return null;
    }

    private static T FindNamedComponentInChildren<T>(Transform root, string childName) where T : Component
    {
        Transform childTransform = FindNamedDescendant(root, childName);
        return childTransform != null ? childTransform.GetComponent<T>() : null;
    }

    private GameObject ResolveCanvasRoot(Transform childTransform)
    {
        if (childTransform == null || _mainCanvas == null)
            return null;

        Transform current = childTransform;
        while (current.parent != null && current.parent != _mainCanvas.transform)
            current = current.parent;

        return current.parent == _mainCanvas.transform ? current.gameObject : null;
    }

    private TMP_Text CreateText(
        string objectName,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        float fontSize,
        FontStyles fontStyle,
        Color color,
        TextAlignmentOptions alignment,
        bool allowRichText)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = sizeDelta;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = _uiFontAsset;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.richText = allowRichText;
        text.raycastTarget = false;
        return text;
    }

    private Image CreatePanel(
        string objectName,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        Color color)
    {
        GameObject panelObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rectTransform = panelObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = sizeDelta;

        Image image = panelObject.GetComponent<Image>();
        image.sprite = _uiSprite;
        image.type = Image.Type.Sliced;
        image.color = color;
        return image;
    }

    private Button CreateButton(
        string objectName,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        string label,
        UnityAction onClick,
        out TMP_Text buttonLabel)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = sizeDelta;

        Image image = buttonObject.GetComponent<Image>();
        image.sprite = _uiSprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(0.91f, 0.92f, 0.97f, 1f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        ColorBlock colorBlock = button.colors;
        colorBlock.normalColor = new Color(0.91f, 0.92f, 0.97f, 1f);
        colorBlock.highlightedColor = new Color(0.98f, 0.98f, 1f, 1f);
        colorBlock.pressedColor = new Color(0.8f, 0.83f, 0.9f, 1f);
        colorBlock.selectedColor = colorBlock.highlightedColor;
        colorBlock.disabledColor = new Color(0.75f, 0.76f, 0.8f, 0.7f);
        button.colors = colorBlock;

        buttonLabel = CreateText(
            $"{objectName}Label",
            rectTransform,
            new Vector2(0f, 0f),
            new Vector2(1f, 1f),
            Vector2.zero,
            Vector2.zero,
            22f,
            FontStyles.Bold,
            new Color(0.24f, 0.27f, 0.36f),
            TextAlignmentOptions.Center,
            false);
        buttonLabel.text = label;

        return button;
    }

#if UNITY_EDITOR
    [ContextMenu("Create Or Refresh Editor UI")]
    private void CreateOrRefreshEditorUi()
    {
        _mainCanvas = FindFirstObjectByType<Canvas>();
        if (_mainCanvas == null)
        {
            Debug.LogError("GameManager: Canvas not found.");
            return;
        }

        if (gameOverScreen == null)
        {
            Debug.LogError("GameManager: GameOver screen is not assigned.");
            return;
        }

        _uiFontAsset = TMP_Settings.defaultFontAsset;
        _uiSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");

        _livesHudRoot = FindOrCreateUiRoot("LivesHud", _mainCanvas.transform, new Vector2(20f, -20f), new Vector2(220f, 48f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
        RectTransform hudRect = _livesHudRoot.GetComponent<RectTransform>();
        hudRect.sizeDelta = new Vector2(220f, 48f);
        _livesCounterText = FindOrCreateTextElement("LivesCounter", _livesHudRoot.transform, new Vector2(0f, -18f), new Vector2(0f, 34f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), 30f, FontStyles.Bold, new Color(0.2f, 0.23f, 0.32f), TextAlignmentOptions.Left, true, "<color=#D95A66>\u2665</color> 3");

        _scoreHudRoot = FindOrCreateUiRoot("ScoreHud", _mainCanvas.transform, new Vector2(-20f, -20f), new Vector2(220f, 48f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), "Score");
        RectTransform scoreHudRect = _scoreHudRoot.GetComponent<RectTransform>();
        scoreHudRect.sizeDelta = new Vector2(220f, 48f);
        _scoreCounterText = FindOrCreateTextElement("ScoreCounter", _scoreHudRoot.transform, new Vector2(0f, -10f), new Vector2(-24f, 32f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), 24f, FontStyles.Bold, new Color(0.2f, 0.23f, 0.32f), TextAlignmentOptions.Left, false, "Score: 0");

        RectTransform gameOverRect = gameOverScreen.GetComponent<RectTransform>();
        _finalScoreText = FindOrCreateTextElement("FinalScoreText", gameOverRect, new Vector2(0f, 78f), new Vector2(420f, 40f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 28f, FontStyles.Bold, new Color(0.22f, 0.25f, 0.34f), TextAlignmentOptions.Center, false, "Score: 0");
        FindOrCreateTextElement("LeaderboardTitle", gameOverRect, new Vector2(0f, 24f), new Vector2(360f, 36f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 24f, FontStyles.Bold, new Color(0.22f, 0.25f, 0.34f), TextAlignmentOptions.Center, false, "Top 3");
        _leaderboardText = FindOrCreateTextElement("LeaderboardText", gameOverRect, new Vector2(0f, -28f), new Vector2(420f, 120f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 22f, FontStyles.Normal, new Color(0.2f, 0.23f, 0.32f), TextAlignmentOptions.Center, false, "1. --- - 0\n2. --- - 0\n3. --- - 0");
        FindOrCreateTextElement("NamePromptText", gameOverRect, new Vector2(0f, -118f), new Vector2(420f, 32f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 20f, FontStyles.Normal, new Color(0.24f, 0.27f, 0.36f), TextAlignmentOptions.Center, false, "Type your name, then save your score");

        Image nameEntryBackground = FindOrCreatePanelElement("NameEntryBackground", gameOverRect, new Vector2(0f, -165f), new Vector2(280f, 48f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Color(0.96f, 0.96f, 0.98f, 0.98f));
        _nameEntryText = FindOrCreateTextElement("NameEntryText", nameEntryBackground.rectTransform, Vector2.zero, new Vector2(-24f, -12f), new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), 24f, FontStyles.Normal, new Color(0.52f, 0.54f, 0.6f), TextAlignmentOptions.Center, false, "YOUR NAME");

        _saveScoreButton = FindOrCreateButtonElement("SaveScoreButton", gameOverRect, new Vector2(-92f, -228f), new Vector2(170f, 46f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), out _saveScoreButtonLabel, "Save Score");
        _restartButton = FindOrCreateButtonElement("RestartButton", gameOverRect, new Vector2(92f, -228f), new Vector2(170f, 46f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), out _, "Restart");

        if (_continuePrompt != null)
        {
            _continuePromptText = _continuePrompt.GetComponentInChildren<TMP_Text>(true);
            if (_continuePromptText != null)
                _continuePromptText.text = "Save score, then click Restart";
        }

        TryAssignEditorUiReferences();
        EditorUtility.SetDirty(this);
        EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private GameObject FindOrCreateUiRoot(string objectName, Transform parent, Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, params string[] alternateNames)
    {
        Transform existingTransform = parent.Find(objectName);
        if (existingTransform == null)
        {
            for (int index = 0; index < alternateNames.Length; index++)
            {
                existingTransform = parent.Find(alternateNames[index]);
                if (existingTransform != null)
                    break;
            }
        }

        GameObject targetObject = existingTransform != null ? existingTransform.gameObject : new GameObject(objectName, typeof(RectTransform));
        RectTransform rectTransform = targetObject.GetComponent<RectTransform>();
        if (rectTransform == null)
            rectTransform = targetObject.AddComponent<RectTransform>();

        if (existingTransform == null)
            Undo.RegisterCreatedObjectUndo(targetObject, $"Create {objectName}");

        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = pivot;
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = sizeDelta;
        return targetObject;
    }

    private TMP_Text FindOrCreateTextElement(string objectName, Transform parent, Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, float fontSize, FontStyles fontStyle, Color color, TextAlignmentOptions alignment, bool allowRichText, string textValue)
    {
        Transform existingTransform = parent.Find(objectName);
        GameObject targetObject = existingTransform != null ? existingTransform.gameObject : new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        if (existingTransform == null)
            Undo.RegisterCreatedObjectUndo(targetObject, $"Create {objectName}");

        RectTransform rectTransform = targetObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = pivot;
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = sizeDelta;

        TextMeshProUGUI textComponent = targetObject.GetComponent<TextMeshProUGUI>();
        textComponent.font = _uiFontAsset;
        textComponent.fontSize = fontSize;
        textComponent.fontStyle = fontStyle;
        textComponent.color = color;
        textComponent.alignment = alignment;
        textComponent.enableWordWrapping = false;
        textComponent.richText = allowRichText;
        textComponent.raycastTarget = false;
        textComponent.text = textValue;
        EditorUtility.SetDirty(textComponent);
        return textComponent;
    }

    private Image FindOrCreatePanelElement(string objectName, Transform parent, Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Color color)
    {
        Transform existingTransform = parent.Find(objectName);
        GameObject targetObject = existingTransform != null ? existingTransform.gameObject : new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        if (existingTransform == null)
            Undo.RegisterCreatedObjectUndo(targetObject, $"Create {objectName}");

        RectTransform rectTransform = targetObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = pivot;
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = sizeDelta;

        Image image = targetObject.GetComponent<Image>();
        image.sprite = _uiSprite;
        image.type = Image.Type.Sliced;
        image.color = color;
        EditorUtility.SetDirty(image);
        return image;
    }

    private Button FindOrCreateButtonElement(string objectName, Transform parent, Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, out TMP_Text buttonLabel, string label)
    {
        Transform existingTransform = parent.Find(objectName);
        GameObject targetObject = existingTransform != null ? existingTransform.gameObject : new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        if (existingTransform == null)
            Undo.RegisterCreatedObjectUndo(targetObject, $"Create {objectName}");

        RectTransform rectTransform = targetObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = pivot;
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = sizeDelta;

        Image image = targetObject.GetComponent<Image>();
        image.sprite = _uiSprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(0.91f, 0.92f, 0.97f, 1f);

        Button button = targetObject.GetComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colorBlock = button.colors;
        colorBlock.normalColor = new Color(0.91f, 0.92f, 0.97f, 1f);
        colorBlock.highlightedColor = new Color(0.98f, 0.98f, 1f, 1f);
        colorBlock.pressedColor = new Color(0.8f, 0.83f, 0.9f, 1f);
        colorBlock.selectedColor = colorBlock.highlightedColor;
        colorBlock.disabledColor = new Color(0.75f, 0.76f, 0.8f, 0.7f);
        button.colors = colorBlock;

        buttonLabel = FindOrCreateTextElement($"{objectName}Label", rectTransform, Vector2.zero, Vector2.zero, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), 22f, FontStyles.Bold, new Color(0.24f, 0.27f, 0.36f), TextAlignmentOptions.Center, false, label);

        EditorUtility.SetDirty(button);
        return button;
    }
#endif
}