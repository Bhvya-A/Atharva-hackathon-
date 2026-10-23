using System;
using UnityEngine;

namespace TheOtherSide
{
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public const int StartingLives = 5;
        public const int RelicGoal = 4;

        public static GameManager Instance { get; private set; }

        public GameState State { get; private set; } = GameState.Playing;
        public int Lives { get; private set; } = StartingLives;
        public int RelicsCollected { get; private set; }

        public event Action<GameState> StateChanged;
        public event Action<int> LivesChanged;
        public event Action<int, int> RelicsChanged;

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

        public void LoseLife(int amount = 1)
        {
            if (State != GameState.Playing)
                return;

            Lives = Mathf.Max(0, Lives - amount);
            LivesChanged?.Invoke(Lives);

            if (Lives <= 0)
                SetState(GameState.GameOver);
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
