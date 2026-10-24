using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheOtherSide
{
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public const int StartingLives = 3;
        public const int RelicGoal = 4;

        public static GameManager Instance { get; private set; }

        public GameState State { get; private set; } = GameState.Playing;
        public int Lives { get; private set; } = StartingLives;
        public int RelicsCollected { get; private set; }

        /// <summary>Seconds spent in the Playing state since this run started.</summary>
        public float SurvivalTime => _survivalTime;

        public event Action<GameState> StateChanged;
        public event Action<int> LivesChanged;
        public event Action<int, int> RelicsChanged;

        float _survivalTime;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            GameInput.Initialize();
            ApplyState(GameState.Playing, true);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            if (State == GameState.Playing)
                _survivalTime += Time.deltaTime;

            if (!GameInput.IsReady)
                return;

            if (GameInput.CancelPressed)
            {
                if (State == GameState.Playing)
                    SetState(GameState.Paused);
                else if (State == GameState.Paused)
                    SetState(GameState.Playing);
            }
        }

        public void SetState(GameState next)
        {
            ApplyState(next, false);
        }

        public void TogglePause()
        {
            if (State == GameState.Playing)
                SetState(GameState.Paused);
            else if (State == GameState.Paused)
                SetState(GameState.Playing);
        }

        public bool IsGameplayActive => State == GameState.Playing;

        /// <summary>
        /// Removes one life. Returns the remaining life count.
        /// PlayerHealth owns the health -> life -> respawn flow; this method only
        /// touches the shared life counter and the Game Over transition.
        /// </summary>
        public int LoseLife(int amount = 1)
        {
            if (State != GameState.Playing || amount <= 0)
                return Lives;

            int next = Mathf.Max(0, Lives - amount);
            if (next == Lives)
                return Lives;

            Lives = next;

            if (Lives <= 0)
                SetState(GameState.GameOver);
            else
                LivesChanged?.Invoke(Lives);

            return Lives;
        }

        public void CollectRelic()
        {
            if (State != GameState.Playing)
                return;

            RelicsCollected = Mathf.Min(RelicGoal, RelicsCollected + 1);
            RelicsChanged?.Invoke(RelicsCollected, RelicGoal);

            if (RelicsCollected >= RelicGoal)
                SetState(GameState.Won);
        }

        /// <summary>
        /// Rebuilds the run from scratch. Reloading the active scene disposes the
        /// runtime player, ghost and HUD together, so no stale event subscriptions
        /// or duplicate managers survive a restart.
        /// </summary>
        public void RestartGame()
        {
            // Tweens and the pause clock are static/global state and must be cleared
            // before the scene reload, otherwise the new run can start frozen.
            Time.timeScale = 1f;
            DG.Tweening.DOTween.KillAll();
            _survivalTime = 0f;

            Scene scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0 && scene.buildIndex < SceneManager.sceneCountInBuildSettings)
                SceneManager.LoadScene(scene.buildIndex);
            else
                SceneManager.LoadScene(scene.name);
        }

        void ApplyState(GameState next, bool force)
        {
            if (!force && State == next)
                return;

            State = next;
            Time.timeScale = next == GameState.Playing ? 1f : 0f;

            bool lockCursor = next == GameState.Playing;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;

            if (next == GameState.Playing)
                GameInput.EnableGameplay();
            else
                GameInput.DisableGameplay();

            // Keep Cancel available so pause can be toggled.
            GameInput.Cancel?.Enable();

            StateChanged?.Invoke(State);
            LivesChanged?.Invoke(Lives);
            RelicsChanged?.Invoke(RelicsCollected, RelicGoal);
        }
    }
}
