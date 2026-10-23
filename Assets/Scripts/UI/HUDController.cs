using UnityEngine;
using UnityEngine.UI;

namespace TheOtherSide
{
    public class HUDController : MonoBehaviour
    {
        public static HUDController Instance { get; private set; }

        [SerializeField] Text livesText;
        [SerializeField] Text relicsText;
        [SerializeField] Text promptText;
        [SerializeField] Text statusText;
        [SerializeField] Text dimensionText;
        [SerializeField] RawImage dimensionOverlay;

        Font _font;
        bool _gameManagerBound;
        bool _dimensionBound;
        string _lastDimensionHud;

        void Awake()
        {
            Instance = this;
        }

        void OnEnable()
        {
            BindGameManager();
            BindDimensionManager();
        }

        void Start()
        {
            BindGameManager();
            BindDimensionManager();
        }

        void OnDisable()
        {
            UnbindGameManager();
            UnbindDimensionManager();
        }

        void OnDestroy()
        {
            UnbindGameManager();
            UnbindDimensionManager();

            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            RefreshDimensionHud();
        }

        public void BuildDefaultUI()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null)
                _font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            gameObject.AddComponent<GraphicRaycaster>();

            livesText = CreateText("Lives", new Vector2(40f, -40f), new Vector2(0f, 1f), TextAnchor.UpperLeft, 32);
            dimensionText = CreateText("Dimension", new Vector2(40f, -88f), new Vector2(0f, 1f), TextAnchor.UpperLeft, 24);
            relicsText = CreateText("Relics", new Vector2(-40f, -40f), new Vector2(1f, 1f), TextAnchor.UpperRight, 32);
            promptText = CreateText("Prompt", new Vector2(0f, 80f), new Vector2(0.5f, 0f), TextAnchor.LowerCenter, 28);
            statusText = CreateText("Status", Vector2.zero, new Vector2(0.5f, 0.5f), TextAnchor.MiddleCenter, 48);
            dimensionOverlay = CreateOverlay("DimensionOverlay");
            statusText.transform.SetAsLastSibling();

            promptText.gameObject.SetActive(false);
            statusText.gameObject.SetActive(false);
            BindOverlayToManager();
        }

        public void SetPrompt(string prompt)
        {
            if (promptText == null)
                return;

            bool show = !string.IsNullOrEmpty(prompt);
            promptText.gameObject.SetActive(show);
            if (show)
                promptText.text = $"[{prompt}]  E / Y";
        }

        void BindGameManager()
        {
            if (_gameManagerBound || GameManager.Instance == null)
                return;

            GameManager.Instance.LivesChanged += OnLivesChanged;
            GameManager.Instance.RelicsChanged += OnRelicsChanged;
            GameManager.Instance.StateChanged += OnStateChanged;
            _gameManagerBound = true;

            OnLivesChanged(GameManager.Instance.Lives);
            OnRelicsChanged(GameManager.Instance.RelicsCollected, GameManager.RelicGoal);
            OnStateChanged(GameManager.Instance.State);
        }

        void UnbindGameManager()
        {
            if (!_gameManagerBound || GameManager.Instance == null)
            {
                _gameManagerBound = false;
                return;
            }

            GameManager.Instance.LivesChanged -= OnLivesChanged;
            GameManager.Instance.RelicsChanged -= OnRelicsChanged;
            GameManager.Instance.StateChanged -= OnStateChanged;
            _gameManagerBound = false;
        }

        void BindDimensionManager()
        {
            if (_dimensionBound || DimensionManager.Instance == null)
                return;

            DimensionManager.Instance.DimensionChanged += OnDimensionChanged;
            _dimensionBound = true;
            BindOverlayToManager();
            RefreshDimensionHud(true);
        }

        void UnbindDimensionManager()
        {
            if (!_dimensionBound || DimensionManager.Instance == null)
            {
                _dimensionBound = false;
                return;
            }

            DimensionManager.Instance.DimensionChanged -= OnDimensionChanged;
            _dimensionBound = false;
        }

        void BindOverlayToManager()
        {
            if (dimensionOverlay != null && DimensionManager.Instance != null)
                DimensionManager.Instance.BindOverlay(dimensionOverlay);
        }

        void OnDimensionChanged(Dimension previous, Dimension current)
        {
            RefreshDimensionHud(true);
        }

        void RefreshDimensionHud(bool force = false)
        {
            if (dimensionText == null)
                return;

            DimensionManager dimensions = DimensionManager.Instance;
            if (dimensions == null)
                return;

            string readiness = dimensions.IsSwitchReady ? "Ready" : $"{dimensions.CooldownRemaining:0.0}s";
            string label = dimensions.Current == Dimension.Void ? "The Void" : "Reality";
            string hud = $"{label}   {readiness}";
            if (!force && hud == _lastDimensionHud)
                return;

            _lastDimensionHud = hud;
            dimensionText.text = hud;
            dimensionText.color = dimensions.Current == Dimension.Void
                ? new Color(0.72f, 0.55f, 1f)
                : Color.white;
        }

        void OnLivesChanged(int lives)
        {
            if (livesText != null)
                livesText.text = $"Lives  {lives}";
        }

        void OnRelicsChanged(int collected, int goal)
        {
            if (relicsText != null)
                relicsText.text = $"Relics  {collected}/{goal}";
        }

        void OnStateChanged(GameState state)
        {
            if (statusText == null)
                return;

            switch (state)
            {
                case GameState.Paused:
                    statusText.gameObject.SetActive(true);
                    statusText.text = "PAUSED\nEsc to resume";
                    break;
                case GameState.GameOver:
                    statusText.gameObject.SetActive(true);
                    statusText.text = "GAME OVER";
                    break;
                case GameState.Won:
                    statusText.gameObject.SetActive(true);
                    statusText.text = "YOU ESCAPED";
                    break;
                default:
                    statusText.gameObject.SetActive(false);
                    break;
            }
        }

        Text CreateText(string name, Vector2 anchoredPos, Vector2 anchor, TextAnchor align, int size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(700f, 120f);

            Text text = go.AddComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.alignment = align;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        RawImage CreateOverlay(string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.SetAsLastSibling();

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            RawImage image = go.AddComponent<RawImage>();
            image.texture = Texture2D.whiteTexture;
            image.color = new Color(1f, 1f, 1f, 0f);
            image.raycastTarget = false;
            go.SetActive(false);
            return image;
        }
    }
}
