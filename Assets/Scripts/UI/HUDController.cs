using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TheOtherSide
{
    /// <summary>
    /// Runtime-built horror/sci-fi HUD. The scene owns the Canvas root, while the
    /// widgets are generated once so the layout stays consistent at any resolution.
    /// All damage/ability state comes from the existing managers; this class never
    /// owns gameplay state.
    /// </summary>
    [DisallowMultipleComponent]
    public class HUDController : MonoBehaviour
    {
        public static HUDController Instance { get; private set; }

        static readonly Color PanelColor = new Color(0.027f, 0.031f, 0.043f, 0.84f);
        static readonly Color PanelSoftColor = new Color(0.055f, 0.063f, 0.082f, 0.88f);
        static readonly Color TextColor = new Color(0.93f, 0.92f, 0.87f, 1f);
        static readonly Color MutedTextColor = new Color(0.68f, 0.68f, 0.64f, 1f);
        static readonly Color Cyan = new Color(0.35f, 0.9f, 1f, 1f);
        static readonly Color Purple = new Color(0.72f, 0.45f, 1f, 1f);
        static readonly Color Blood = new Color(0.88f, 0.1f, 0.12f, 1f);
        static readonly Color Healthy = new Color(0.42f, 1f, 0.62f, 1f);
        static readonly Color Warning = new Color(1f, 0.78f, 0.32f, 1f);
        static readonly Color Danger = new Color(1f, 0.24f, 0.26f, 1f);

        [Header("Optional scene bindings")]
        [SerializeField] RawImage dimensionOverlay;

        // Runtime widgets
        CanvasGroup _hudGroup;
        CanvasGroup _promptGroup;
        CanvasGroup _notificationGroup;
        CanvasGroup _bloodGroup;
        CanvasGroup _lowHealthGroup;
        CanvasGroup _modalGroup;
        CanvasGroup _crosshairGroup;

        RectTransform _crosshairRoot;
        readonly List<Image> _crosshairParts = new List<Image>();
        bool _crosshairInteractable;
        bool _crosshairVisible = true;

        RectTransform _notificationRect;
        RectTransform _modalPanel;
        RectTransform _freezeCard;
        RectTransform _dimensionCard;

        Image _healthFill;
        Image _healthFlash;
        Image _freezeCooldownFill;
        Image _dimensionCooldownFill;
        Image _dimensionIcon;
        Image _modalBackdrop;
        Image _restartButtonImage;

        TMP_Text _healthText;
        TMP_Text _healthStateText;
        TMP_Text _timerText;
        TMP_Text _relicsText;
        TMP_Text _promptText;
        TMP_Text _notificationText;
        TMP_Text _freezeStatusText;
        TMP_Text _freezeCooldownText;
        TMP_Text _dimensionStatusText;
        TMP_Text _dimensionCooldownText;
        TMP_Text _modalTitle;
        TMP_Text _modalBody;
        TMP_Text _modalStats;
        TMP_Text _modalButtonLabel;

        readonly List<Image> _lifeIcons = new List<Image>();
        Button _restartButton;

        TMP_FontAsset _fontRegular;
        TMP_FontAsset _fontSemiBold;
        TMP_FontAsset _fontBold;

        Sprite _panelSprite;
        Sprite _softPanelSprite;
        Sprite _barSprite;
        Sprite _flatSprite;
        Sprite _heartSprite;
        Sprite _freezeSprite;
        Sprite _dimensionSprite;
        Texture2D _bloodTexture;

        Tween _healthTween;
        Tween _healthColorTween;
        Tween _bloodTween;
        Tween _notificationTween;
        Tween _modalTween;
        Tween _lowHealthTween;
        Tween _healthFlashTween;

        bool _built;
        bool _gameManagerBound;
        bool _dimensionBound;
        bool _healthBound;
        bool _freezeBound;
        bool _restarting;

        PlayerHealth _boundHealth;
        TimeFreezeAbility _boundFreeze;

        int _lastLives = -1;
        Dimension _lastDimension = (Dimension)(-1);
        string _lastPrompt = string.Empty;
        float _lastTimerValue = -1f;
        float _lastFreezeCooldown = -1f;
        float _lastDimensionCooldown = -1f;
        string _lastFreezeStatus = string.Empty;
        string _lastFreezeDetail = string.Empty;
        string _lastDimensionLabel = string.Empty;
        string _lastDimensionDetail = string.Empty;
        bool _modalVisible;
        bool _modalHasButton;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("HUDController: duplicate HUD removed.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            BuildDefaultUI();
        }

        void OnEnable()
        {
            BindGameManager();
            BindDimensionManager();
            BindPlayerHealth();
            BindTimeFreeze();
        }

        void Start()
        {
            BindGameManager();
            BindDimensionManager();
            BindPlayerHealth();
            BindTimeFreeze();
            RefreshAll(true);
        }

        void OnDisable()
        {
            UnbindAll();
        }

        void OnDestroy()
        {
            UnbindAll();

            if (_restartButton != null)
                _restartButton.onClick.RemoveListener(OnRestartClicked);

            KillTweens();

            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            // Runtime objects are created by GameBootstrap after the scene HUD may
            // have woken, so bind lazily as well as in Start.
            BindGameManager();
            BindDimensionManager();
            BindPlayerHealth();
            BindTimeFreeze();

            RefreshTimer();
            RefreshAbilityCards();
            RefreshDimensionCard();
            HandleModalKeyboard();
        }

        // ------------------------------------------------------------------
        // Public API kept compatible with existing gameplay scripts
        // ------------------------------------------------------------------

        public void SetPrompt(string prompt)
        {
            if (!_built || _promptText == null)
                return;

            string next = prompt ?? string.Empty;
            if (next == _lastPrompt)
                return;

            _lastPrompt = next;
            bool show = !string.IsNullOrEmpty(next);
            if (show)
                _promptText.text = $"E  —  {next}";

            _promptGroup.DOKill();
            _promptGroup.DOFade(show ? 1f : 0f, 0.12f)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (_promptGroup != null && !show)
                        _promptGroup.gameObject.SetActive(false);
                });

            if (show)
                _promptGroup.gameObject.SetActive(true);
        }

        public void ShowTimeFreezeMessage(string message, float duration = 2.5f)
        {
            ShowNotification(message, duration, Cyan);
        }

        public void ShowNotification(string message, float duration = 2f, Color? color = null)
        {
            if (!_built || _notificationGroup == null || string.IsNullOrEmpty(message))
                return;

            _notificationTween?.Kill();
            _notificationGroup.gameObject.SetActive(true);
            _notificationText.text = message;
            _notificationText.color = color ?? TextColor;

            _notificationGroup.alpha = 0f;
            _notificationRect.localScale = Vector3.one * 0.97f;

            Sequence sequence = DOTween.Sequence().SetUpdate(true);
            sequence.Append(_notificationGroup.DOFade(1f, 0.14f));
            sequence.Join(_notificationRect.DOScale(1f, 0.2f).SetEase(Ease.OutBack));
            sequence.AppendInterval(Mathf.Max(0.2f, duration));
            sequence.Append(_notificationGroup.DOFade(0f, 0.3f));
            sequence.OnComplete(() =>
            {
                if (_notificationGroup != null)
                    _notificationGroup.gameObject.SetActive(false);
            });

            _notificationTween = sequence;
        }

        // ------------------------------------------------------------------
        // UI construction
        // ------------------------------------------------------------------

        public void BuildDefaultUI()
        {
            if (_built)
                return;

            _built = true;

            EnsureCanvas();
            CacheStyleAssets();
            BuildFonts();

            // Everything gameplay-facing lives under one group so overlays can sit
            // above it without covering the notification/prompt stack accidentally.
            _hudGroup = CreateCanvasGroup("HUDRoot", transform);
            _hudGroup.interactable = false;
            _hudGroup.blocksRaycasts = false;

            BuildHealthPanel(_hudGroup.transform);
            BuildStatsPanel(_hudGroup.transform);
            BuildAbilityPanel(_hudGroup.transform);
            BuildPrompt(_hudGroup.transform);
            BuildNotification(_hudGroup.transform);
            BuildCrosshair(_hudGroup.transform);

            BuildDimensionOverlay();
            BuildBloodOverlays();
            BuildModal();

            BindOverlayToManager();
            ApplyHealthImmediate(PlayerHealth.Instance != null ? PlayerHealth.Instance.CurrentHealth : 5, 5);
            OnLivesChanged(GameManager.Instance != null ? GameManager.Instance.Lives : GameManager.StartingLives);
            ApplyRelics(GameManager.Instance != null ? GameManager.Instance.RelicsCollected : 0, GameManager.RelicGoal);
            ApplyDimensionImmediate(DimensionManager.Instance != null ? DimensionManager.Instance.Current : Dimension.Reality);
        }

        void EnsureCanvas()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
                canvas = gameObject.AddComponent<Canvas>();

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = gameObject.AddComponent<CanvasScaler>();

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
        }

        void BuildFonts()
        {
            _fontRegular = LoadFontAsset("UI/Fonts/Rajdhani-Regular");
            _fontSemiBold = LoadFontAsset("UI/Fonts/Rajdhani-SemiBold");
            _fontBold = LoadFontAsset("UI/Fonts/Rajdhani-Bold");

            if (_fontRegular == null)
                _fontRegular = DefaultFont();
            if (_fontSemiBold == null)
                _fontSemiBold = _fontRegular;
            if (_fontBold == null)
                _fontBold = _fontSemiBold != null ? _fontSemiBold : _fontRegular;
        }

        void CacheStyleAssets()
        {
            _panelSprite = GetPanelSprite(false);
            _softPanelSprite = GetPanelSprite(true);
            _barSprite = GetBarSprite();
            _flatSprite = GetFlatSprite();
            _heartSprite = GetHeartSprite();
            _freezeSprite = GetFreezeSprite();
            _dimensionSprite = GetDimensionSprite();
            _bloodTexture = GetBloodTexture();
        }

        void BuildHealthPanel(Transform parent)
        {
            RectTransform healthPanel = CreateRect("HealthPanel", parent);
            SetTopLeft(healthPanel, 36f, 36f, 470f, 158f);

            AddImage(healthPanel, _panelSprite, PanelColor, Image.Type.Sliced);
            AddAccent(healthPanel, Cyan, 3f, 0.55f);

            TMP_Text title = CreateText("VitalLabel", healthPanel, _fontSemiBold, 17f, MutedTextColor, TextAlignmentOptions.Left);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -14f), new Vector2(240f, 22f));
            title.text = "VITAL SIGNS";
            title.characterSpacing = 8f;

            _healthText = CreateText("HealthValue", healthPanel, _fontBold, 24f, TextColor, TextAlignmentOptions.Right);
            SetRect(_healthText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -11f), new Vector2(160f, 28f));
            _healthText.text = "5 / 5";

            RectTransform bar = CreateRect("HealthBar", healthPanel);
            SetRect(bar, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -46f), new Vector2(422f, 26f));

            AddImage(bar, _barSprite, new Color(0.08f, 0.09f, 0.11f, 0.95f), Image.Type.Sliced, false);

            RectTransform fillRect = CreateRect("Fill", bar);
            SetRect(fillRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);

            _healthFill = AddImage(fillRect, _barSprite, Healthy, Image.Type.Filled, false);
            _healthFill.fillMethod = Image.FillMethod.Horizontal;
            _healthFill.fillOrigin = 0;
            _healthFill.fillAmount = 1f;

            // The flash must be its own Image: adding it to the fill's GameObject
            // would reuse the same component and fade the health bar itself to zero.
            RectTransform flashRect = CreateRect("HealthFlash", bar);
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;
            flashRect.pivot = new Vector2(0.5f, 0.5f);
            flashRect.offsetMin = new Vector2(2f, 2f);
            flashRect.offsetMax = new Vector2(-2f, -2f);
            _healthFlash = AddImage(flashRect, _flatSprite, new Color(1f, 0.1f, 0.1f, 0f), Image.Type.Simple, false);

            for (int i = 1; i < 5; i++)
            {
                RectTransform tick = CreateRect($"Tick_{i}", bar);
                tick.anchorMin = new Vector2(0f, 0.5f);
                tick.anchorMax = new Vector2(0f, 0.5f);
                tick.pivot = new Vector2(0.5f, 0.5f);
                tick.sizeDelta = new Vector2(2f, 18f);
                tick.anchoredPosition = new Vector2(i * (422f / 5f), 0f);
                AddImage(tick, _flatSprite, new Color(0f, 0f, 0f, 0.65f), Image.Type.Simple, false);
            }

            TMP_Text livesLabel = CreateText("LivesLabel", healthPanel, _fontSemiBold, 15f, MutedTextColor, TextAlignmentOptions.Left);
            SetRect(livesLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 20f), new Vector2(100f, 24f));
            livesLabel.text = "LIVES";
            livesLabel.characterSpacing = 6f;

            for (int i = 0; i < GameManager.StartingLives; i++)
            {
                RectTransform icon = CreateRect($"Life_{i + 1}", healthPanel);
                SetRect(icon, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(112f + i * 38f, 18f), new Vector2(30f, 30f));
                Image image = AddImage(icon, _heartSprite, TextColor, Image.Type.Simple, false);
                _lifeIcons.Add(image);
            }

            _healthStateText = CreateText("HealthState", healthPanel, _fontSemiBold, 13f, MutedTextColor, TextAlignmentOptions.Right);
            SetRect(_healthStateText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 22f), new Vector2(260f, 22f));
            _healthStateText.text = "STABLE";
            _healthStateText.characterSpacing = 3f;
        }

        void BuildStatsPanel(Transform parent)
        {
            RectTransform panel = CreateRect("StatsPanel", parent);
            SetTopRight(panel, 36f, 36f, 340f, 112f);
            AddImage(panel, _panelSprite, PanelColor, Image.Type.Sliced);
            AddAccent(panel, Purple, 3f, 0.55f);

            TMP_Text label = CreateText("SurvivalLabel", panel, _fontSemiBold, 15f, MutedTextColor, TextAlignmentOptions.Left);
            SetRect(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -14f), new Vector2(280f, 22f));
            label.text = "SURVIVAL";
            label.characterSpacing = 8f;

            _timerText = CreateText("Timer", panel, _fontBold, 38f, TextColor, TextAlignmentOptions.Left);
            SetRect(_timerText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -36f), new Vector2(200f, 48f));
            _timerText.text = "00:00";

            _relicsText = CreateText("Relics", panel, _fontSemiBold, 15f, MutedTextColor, TextAlignmentOptions.Right);
            SetRect(_relicsText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-22f, 16f), new Vector2(230f, 22f));
            _relicsText.text = $"RELICS  0/{GameManager.RelicGoal}";
            _relicsText.characterSpacing = 2f;
        }

        void BuildAbilityPanel(Transform parent)
        {
            RectTransform panel = CreateRect("AbilityPanel", parent);
            SetBottomLeft(panel, 36f, 36f, 430f, 178f);
            AddImage(panel, _panelSprite, PanelColor, Image.Type.Sliced);
            AddAccent(panel, Cyan, 2f, 0.35f);

            TMP_Text label = CreateText("AbilityLabel", panel, _fontSemiBold, 14f, MutedTextColor, TextAlignmentOptions.Left);
            SetRect(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(220f, 20f));
            label.text = "ABILITIES";
            label.characterSpacing = 8f;

            _freezeCard = BuildAbilityCard(
                panel,
                "FreezeCard",
                new Vector2(16f, 14f),
                _freezeSprite,
                Cyan,
                "TIME FREEZE",
                "F",
                out _freezeStatusText,
                out _freezeCooldownText,
                out _freezeCooldownFill,
                out _);

            _dimensionCard = BuildAbilityCard(
                panel,
                "DimensionCard",
                new Vector2(220f, 14f),
                _dimensionSprite,
                Purple,
                "DIMENSION",
                "Q",
                out _dimensionStatusText,
                out _dimensionCooldownText,
                out _dimensionCooldownFill,
                out _dimensionIcon);
        }

        RectTransform BuildAbilityCard(
            Transform parent,
            string name,
            Vector2 anchoredPos,
            Sprite iconSprite,
            Color accent,
            string title,
            string key,
            out TMP_Text statusText,
            out TMP_Text cooldownText,
            out Image cooldownFill,
            out Image iconImage)
        {
            RectTransform card = CreateRect(name, parent);
            SetBottomLeft(card, anchoredPos.x, anchoredPos.y, 194f, 138f);
            AddImage(card, _softPanelSprite, PanelSoftColor, Image.Type.Sliced);

            RectTransform iconBg = CreateRect("IconBg", card);
            SetRect(iconBg, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -12f), new Vector2(36f, 36f));
            AddImage(iconBg, _flatSprite, new Color(accent.r, accent.g, accent.b, 0.13f), Image.Type.Simple, false);

            RectTransform iconRect = CreateRect("Icon", iconBg);
            SetRect(iconRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 24f));
            iconImage = AddImage(iconRect, iconSprite, accent, Image.Type.Simple, false);

            TMP_Text titleText = CreateText("Title", card, _fontSemiBold, 15f, TextColor, TextAlignmentOptions.Left);
            SetRect(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(56f, -12f), new Vector2(110f, 36f));
            titleText.text = title;
            titleText.characterSpacing = 2f;

            RectTransform keyBg = CreateRect("Key", card);
            SetRect(keyBg, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -14f), new Vector2(28f, 28f));
            AddImage(keyBg, _flatSprite, new Color(accent.r, accent.g, accent.b, 0.22f), Image.Type.Simple, false);

            TMP_Text keyText = CreateText("KeyText", keyBg, _fontBold, 16f, accent, TextAlignmentOptions.Center);
            SetRect(keyText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            keyText.text = key;

            statusText = CreateText("Status", card, _fontSemiBold, 17f, TextColor, TextAlignmentOptions.Left);
            SetRect(statusText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -56f), new Vector2(170f, 24f));
            statusText.text = "READY";

            cooldownText = CreateText("Cooldown", card, _fontRegular, 13f, MutedTextColor, TextAlignmentOptions.Left);
            SetRect(cooldownText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -78f), new Vector2(170f, 20f));
            cooldownText.text = "10.0s cooldown";

            RectTransform bar = CreateRect("CooldownBar", card);
            SetRect(bar, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(12f, 10f), new Vector2(170f, 5f));
            AddImage(bar, _flatSprite, new Color(1f, 1f, 1f, 0.08f), Image.Type.Simple, false);

            RectTransform fillRect = CreateRect("Fill", bar);
            SetRect(fillRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            cooldownFill = AddImage(fillRect, _flatSprite, accent, Image.Type.Filled, false);
            cooldownFill.fillMethod = Image.FillMethod.Horizontal;
            cooldownFill.fillOrigin = 0;
            cooldownFill.fillAmount = 1f;

            return card;
        }

        void BuildPrompt(Transform parent)
        {
            RectTransform promptRect = CreateRect("Prompt", parent);
            SetBottomCenter(promptRect, 0f, 98f, 760f, 38f);

            _promptGroup = promptRect.gameObject.AddComponent<CanvasGroup>();
            _promptGroup.alpha = 0f;
            _promptGroup.blocksRaycasts = false;
            _promptGroup.gameObject.SetActive(false);

            _promptText = CreateText("PromptText", promptRect, _fontSemiBold, 21f, TextColor, TextAlignmentOptions.Center);
            SetRect(_promptText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _promptText.text = string.Empty;

            Shadow shadow = _promptText.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(1f, -1f);
        }

        void BuildNotification(Transform parent)
        {
            RectTransform panel = CreateRect("Notification", parent);
            SetTopCenter(panel, 0f, 148f, 580f, 54f);
            _notificationRect = panel;

            _notificationGroup = panel.gameObject.AddComponent<CanvasGroup>();
            _notificationGroup.alpha = 0f;
            _notificationGroup.blocksRaycasts = false;

            AddImage(panel, _softPanelSprite, new Color(0.035f, 0.04f, 0.055f, 0.9f), Image.Type.Sliced, false);
            RectTransform accent = CreateRect("Accent", panel);
            SetRect(accent, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(3f, -14f));
            AddImage(accent, _flatSprite, Cyan, Image.Type.Simple, false);

            _notificationText = CreateText("NotificationText", panel, _fontSemiBold, 19f, TextColor, TextAlignmentOptions.Center);
            SetRect(_notificationText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _notificationText.margin = new Vector4(24f, 0f, 24f, 0f);

            panel.gameObject.SetActive(false);
        }

        void BuildCrosshair(Transform parent)
        {
            _crosshairRoot = CreateRect("Crosshair", parent);
            SetRect(_crosshairRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 34f));
            _crosshairGroup = _crosshairRoot.gameObject.AddComponent<CanvasGroup>();
            _crosshairGroup.alpha = 1f;
            _crosshairGroup.blocksRaycasts = false;
            _crosshairGroup.interactable = false;

            Color idle = new Color(0.9f, 0.9f, 0.84f, 0.52f);

            // Four short ticks plus a pin-prick centre: small enough to leave the
            // environment readable, but readable against both dark and lit surfaces.
            AddCrosshairPart("Tick_Top", new Vector2(0f, 4.5f), new Vector2(1.6f, 7f), new Vector2(0.5f, 0f), idle);
            AddCrosshairPart("Tick_Bottom", new Vector2(0f, -4.5f), new Vector2(1.6f, 7f), new Vector2(0.5f, 1f), idle);
            AddCrosshairPart("Tick_Left", new Vector2(-4.5f, 0f), new Vector2(7f, 1.6f), new Vector2(1f, 0.5f), idle);
            AddCrosshairPart("Tick_Right", new Vector2(4.5f, 0f), new Vector2(7f, 1.6f), new Vector2(0f, 0.5f), idle);

            RectTransform dot = CreateRect("Dot", _crosshairRoot);
            SetRect(dot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2.4f, 2.4f));
            Image dotImage = AddImage(dot, _flatSprite, new Color(0.95f, 0.95f, 0.9f, 0.7f), Image.Type.Simple, false);
            _crosshairParts.Add(dotImage);
        }

        void AddCrosshairPart(string name, Vector2 anchoredPosition, Vector2 size, Vector2 pivot, Color color)
        {
            RectTransform rect = CreateRect(name, _crosshairRoot);
            SetRect(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pivot, anchoredPosition, size);
            _crosshairParts.Add(AddImage(rect, _flatSprite, color, Image.Type.Simple, false));
        }

        /// <summary>Subtle response when the centre ray lands on a usable interactable.</summary>
        public void SetCrosshairState(bool interactable)
        {
            if (_crosshairRoot == null || _crosshairInteractable == interactable)
                return;

            _crosshairInteractable = interactable;

            _crosshairRoot.DOKill();
            _crosshairRoot.DOScale(interactable ? 1.18f : 1f, 0.14f)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);

            Color target = interactable
                ? new Color(Cyan.r, Cyan.g, Cyan.b, 0.95f)
                : new Color(0.9f, 0.9f, 0.84f, 0.52f);

            for (int i = 0; i < _crosshairParts.Count; i++)
            {
                Image part = _crosshairParts[i];
                if (part == null)
                    continue;

                part.DOKill();
                part.DOColor(target, 0.14f).SetUpdate(true);
            }
        }

        public void SetCrosshairVisible(bool visible)
        {
            if (_crosshairGroup == null || _crosshairVisible == visible)
                return;

            _crosshairVisible = visible;
            _crosshairGroup.DOKill();
            _crosshairGroup.DOFade(visible ? 1f : 0f, 0.12f).SetUpdate(true);
        }

        void BuildDimensionOverlay()
        {
            if (dimensionOverlay == null)
            {
                RectTransform rect = CreateFullScreenRaw("DimensionOverlay", transform, null);
                dimensionOverlay = rect.GetComponent<RawImage>();
            }

            dimensionOverlay.raycastTarget = false;
            dimensionOverlay.color = new Color(1f, 1f, 1f, 0f);
            dimensionOverlay.gameObject.SetActive(false);
            BindOverlayToManager();
        }

        void BuildBloodOverlays()
        {
            RectTransform low = CreateFullScreenRaw("LowHealthVignette", transform, _bloodTexture);
            RawImage lowImage = low.GetComponent<RawImage>();
            lowImage.color = new Color(Blood.r, Blood.g, Blood.b, 0.8f);
            _lowHealthGroup = low.gameObject.AddComponent<CanvasGroup>();
            _lowHealthGroup.alpha = 0f;
            _lowHealthGroup.blocksRaycasts = false;

            RectTransform blood = CreateFullScreenRaw("BloodOverlay", transform, _bloodTexture);
            RawImage bloodImage = blood.GetComponent<RawImage>();
            bloodImage.color = Color.white;
            _bloodGroup = blood.gameObject.AddComponent<CanvasGroup>();
            _bloodGroup.alpha = 0f;
            _bloodGroup.blocksRaycasts = false;
        }

        void BuildModal()
        {
            RectTransform modal = CreateRect("ModalRoot", transform);
            SetRect(modal, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            modal.offsetMin = Vector2.zero;
            modal.offsetMax = Vector2.zero;

            _modalGroup = modal.gameObject.AddComponent<CanvasGroup>();
            _modalGroup.alpha = 0f;
            _modalGroup.interactable = false;
            _modalGroup.blocksRaycasts = false;

            _modalBackdrop = AddImage(modal, _flatSprite, new Color(0.01f, 0.01f, 0.02f, 0.78f), Image.Type.Simple, true);
            _modalBackdrop.raycastTarget = true;

            _modalPanel = CreateRect("ModalPanel", modal);
            SetRect(_modalPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(590f, 350f));
            AddImage(_modalPanel, _panelSprite, new Color(0.035f, 0.038f, 0.052f, 0.97f), Image.Type.Sliced);
            AddAccent(_modalPanel, Blood, 4f, 0.85f);

            _modalTitle = CreateText("Title", _modalPanel, _fontBold, 54f, TextColor, TextAlignmentOptions.Center);
            SetRect(_modalTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(-60f, 70f));
            _modalTitle.text = "GAME OVER";
            _modalTitle.characterSpacing = 6f;

            _modalBody = CreateText("Body", _modalPanel, _fontSemiBold, 20f, MutedTextColor, TextAlignmentOptions.Center);
            SetRect(_modalBody.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -118f), new Vector2(-60f, 32f));
            _modalBody.text = "THE OTHER SIDE HAS YOU";

            RectTransform divider = CreateRect("Divider", _modalPanel);
            SetRect(divider, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -166f), new Vector2(420f, 1f));
            AddImage(divider, _flatSprite, new Color(1f, 1f, 1f, 0.12f), Image.Type.Simple, false);

            _modalStats = CreateText("Stats", _modalPanel, _fontSemiBold, 17f, TextColor, TextAlignmentOptions.Center);
            SetRect(_modalStats.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -184f), new Vector2(-60f, 30f));
            _modalStats.text = "SURVIVED 00:00   |   RELICS 0/4";

            RectTransform buttonRect = CreateRect("RestartButton", _modalPanel);
            SetRect(buttonRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(230f, 56f));

            _restartButtonImage = AddImage(buttonRect, _panelSprite, new Color(0.42f, 0.035f, 0.05f, 0.96f), Image.Type.Sliced, true);
            _restartButton = buttonRect.gameObject.AddComponent<Button>();
            _restartButton.targetGraphic = _restartButtonImage;
            _restartButton.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = _restartButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.selectedColor = Color.white;
            _restartButton.colors = colors;
            _restartButton.onClick.AddListener(OnRestartClicked);

            _modalButtonLabel = CreateText("Label", buttonRect, _fontBold, 20f, TextColor, TextAlignmentOptions.Center);
            SetRect(_modalButtonLabel.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _modalButtonLabel.text = "RESTART";
            _modalButtonLabel.characterSpacing = 4f;

            AddHoverTween(buttonRect);

            modal.gameObject.SetActive(false);
        }

        void AddHoverTween(RectTransform target)
        {
            EventTrigger trigger = target.gameObject.AddComponent<EventTrigger>();

            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => target.DOScale(1.035f, 0.12f).SetUpdate(true));
            trigger.triggers.Add(enter);

            EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => target.DOScale(1f, 0.12f).SetUpdate(true));
            trigger.triggers.Add(exit);

            EventTrigger.Entry down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            down.callback.AddListener(_ => target.DOScale(0.97f, 0.08f).SetUpdate(true));
            trigger.triggers.Add(down);

            EventTrigger.Entry up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            up.callback.AddListener(_ => target.DOScale(1.035f, 0.1f).SetUpdate(true));
            trigger.triggers.Add(up);
        }

        // ------------------------------------------------------------------
        // Layout helpers
        // ------------------------------------------------------------------

        static CanvasGroup CreateCanvasGroup(string name, Transform parent)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect.gameObject.AddComponent<CanvasGroup>();
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
        }

        static void SetTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            SetRect(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(width, height));
        }

        static void SetTopRight(RectTransform rect, float right, float top, float width, float height)
        {
            SetRect(rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-right, -top), new Vector2(width, height));
        }

        static void SetBottomLeft(RectTransform rect, float x, float y, float width, float height)
        {
            SetRect(rect, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(x, y), new Vector2(width, height));
        }

        static void SetBottomCenter(RectTransform rect, float x, float bottom, float width, float height)
        {
            SetRect(rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, bottom), new Vector2(width, height));
        }

        static void SetTopCenter(RectTransform rect, float x, float top, float width, float height)
        {
            SetRect(rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, -top), new Vector2(width, height));
        }

        static RectTransform CreateFullScreenRaw(string name, Transform parent, Texture texture)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            RawImage image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            return rect;
        }

        static Image AddImage(RectTransform rect, Sprite sprite, Color color, Image.Type type, bool raycast = false)
        {
            Image image = rect.gameObject.GetComponent<Image>();
            if (image == null)
                image = rect.gameObject.AddComponent<Image>();

            image.sprite = sprite;
            image.color = color;
            image.type = type;
            image.raycastTarget = raycast;
            if (type == Image.Type.Sliced)
                image.pixelsPerUnitMultiplier = 1f;
            return image;
        }

        void AddAccent(RectTransform panel, Color color, float height, float alpha)
        {
            RectTransform accent = CreateRect("AccentLine", panel);
            SetRect(accent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(0f, height));
            accent.offsetMin = new Vector2(14f, -height);
            accent.offsetMax = new Vector2(-14f, 0f);
            AddImage(accent, _flatSprite, new Color(color.r, color.g, color.b, alpha), Image.Type.Simple, false);
        }

        TMP_Text CreateText(string name, Transform parent, TMP_FontAsset font, float size, Color color, TextAlignmentOptions alignment)
        {
            RectTransform rect = CreateRect(name, parent);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;
            text.margin = Vector4.zero;
            return text;
        }

        // ------------------------------------------------------------------
        // Bindings / refresh
        // ------------------------------------------------------------------

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
            DimensionManager.Instance.TransitionStarted += OnDimensionTransitionStarted;
            _dimensionBound = true;
            BindOverlayToManager();
            ApplyDimensionImmediate(DimensionManager.Instance.Current);
        }

        void UnbindDimensionManager()
        {
            if (!_dimensionBound || DimensionManager.Instance == null)
            {
                _dimensionBound = false;
                return;
            }

            DimensionManager.Instance.DimensionChanged -= OnDimensionChanged;
            DimensionManager.Instance.TransitionStarted -= OnDimensionTransitionStarted;
            _dimensionBound = false;
        }

        void BindPlayerHealth()
        {
            PlayerHealth health = PlayerHealth.Instance;
            if (_healthBound && _boundHealth == health)
                return;

            UnbindPlayerHealth();
            if (health == null)
                return;

            _boundHealth = health;
            health.HealthChanged += OnHealthChanged;
            health.Damaged += OnDamaged;
            health.Died += OnDied;
            health.Respawned += OnRespawned;
            _healthBound = true;
            ApplyHealthImmediate(health.CurrentHealth, health.MaxHealth);
        }

        void UnbindPlayerHealth()
        {
            if (_healthBound && _boundHealth != null)
            {
                _boundHealth.HealthChanged -= OnHealthChanged;
                _boundHealth.Damaged -= OnDamaged;
                _boundHealth.Died -= OnDied;
                _boundHealth.Respawned -= OnRespawned;
            }

            _healthBound = false;
            _boundHealth = null;
        }

        void BindTimeFreeze()
        {
            TimeFreezeAbility ability = TimeFreezeAbility.Instance;
            if (_freezeBound && _boundFreeze == ability)
                return;

            UnbindTimeFreeze();
            if (ability == null)
                return;

            _boundFreeze = ability;
            ability.Activated += OnTimeFreezeActivated;
            ability.CooldownBecameReady += OnTimeFreezeReady;
            _freezeBound = true;
        }

        void UnbindTimeFreeze()
        {
            if (_freezeBound && _boundFreeze != null)
            {
                _boundFreeze.Activated -= OnTimeFreezeActivated;
                _boundFreeze.CooldownBecameReady -= OnTimeFreezeReady;
            }

            _freezeBound = false;
            _boundFreeze = null;
        }

        void UnbindAll()
        {
            UnbindGameManager();
            UnbindDimensionManager();
            UnbindPlayerHealth();
            UnbindTimeFreeze();
        }

        void BindOverlayToManager()
        {
            if (dimensionOverlay != null && DimensionManager.Instance != null)
                DimensionManager.Instance.BindOverlay(dimensionOverlay);
        }

        void RefreshAll(bool immediate)
        {
            if (GameManager.Instance != null)
            {
                OnLivesChanged(GameManager.Instance.Lives);
                OnRelicsChanged(GameManager.Instance.RelicsCollected, GameManager.RelicGoal);
                OnStateChanged(GameManager.Instance.State);
            }

            if (PlayerHealth.Instance != null)
                ApplyHealthImmediate(PlayerHealth.Instance.CurrentHealth, PlayerHealth.Instance.MaxHealth);

            if (DimensionManager.Instance != null)
                ApplyDimensionImmediate(DimensionManager.Instance.Current);

            RefreshTimer();
            RefreshAbilityCards();
        }

        void RefreshTimer()
        {
            if (_timerText == null)
                return;

            float seconds = GameManager.Instance != null ? GameManager.Instance.SurvivalTime : 0f;
            if (Mathf.Abs(seconds - _lastTimerValue) < 0.2f)
                return;

            _lastTimerValue = seconds;
            int whole = Mathf.Max(0, Mathf.FloorToInt(seconds));
            int minutes = whole / 60;
            int secs = whole % 60;
            _timerText.text = $"{minutes:00}:{secs:00}";
        }

        void RefreshAbilityCards()
        {
            TimeFreezeAbility ability = TimeFreezeAbility.Instance;
            if (ability == null || _freezeStatusText == null)
                return;

            bool ready = ability.IsReady;
            float remaining = ability.CooldownRemaining;

            // Compare rendered labels rather than a float delta: the cooldown can
            // settle on 0 while the "READY" state flips, which a small-delta guard
            // would skip, leaving "0.0s remaining" on screen forever.
            string status = ready ? "READY" : "CHARGING";
            string detail = ready
                ? $"Freeze {ability.FreezeDuration:0}s"
                : $"{remaining:0.0}s remaining";

            if (status != _lastFreezeStatus)
            {
                _lastFreezeStatus = status;
                _freezeStatusText.text = status;
                _freezeStatusText.color = ready ? Cyan : TextColor;
                _freezeCooldownFill.color = ready ? Cyan : new Color(0.3f, 0.65f, 0.75f, 0.9f);
            }

            if (detail != _lastFreezeDetail)
            {
                _lastFreezeDetail = detail;
                _freezeCooldownText.text = detail;
            }

            if (Mathf.Abs(remaining - _lastFreezeCooldown) >= 0.05f || ready)
            {
                _lastFreezeCooldown = remaining;
                _freezeCooldownFill.fillAmount = ability.CooldownProgress01;
            }
        }

        void RefreshDimensionCard()
        {
            DimensionManager dimensions = DimensionManager.Instance;
            if (dimensions == null || _dimensionStatusText == null)
                return;

            string label = dimensions.Current == Dimension.Void ? "THE VOID" : "REALITY";
            Color accent = dimensions.Current == Dimension.Void ? Purple : Cyan;
            bool ready = dimensions.IsSwitchReady;
            float remaining = dimensions.CooldownRemaining;

            if (label != _lastDimensionLabel)
            {
                _lastDimensionLabel = label;
                _dimensionStatusText.text = label;
                _dimensionStatusText.color = accent;
                _dimensionIcon.color = accent;
            }

            string detail = dimensions.IsTransitioning
                ? "CROSSING OVER..."
                : ready
                    ? "Ready  -  Q"
                    : $"{remaining:0.0}s cooldown";

            if (detail != _lastDimensionDetail)
            {
                _lastDimensionDetail = detail;
                _dimensionCooldownText.text = detail;
            }

            if (Mathf.Abs(remaining - _lastDimensionCooldown) >= 0.05f || ready)
            {
                _lastDimensionCooldown = remaining;
                _dimensionCooldownFill.fillAmount = ready
                    ? 1f
                    : Mathf.Clamp01(1f - remaining / Mathf.Max(0.01f, dimensions.SwitchCooldown));
            }

            if (_lastDimension != dimensions.Current)
                ApplyDimensionImmediate(dimensions.Current);
        }

        void ApplyDimensionImmediate(Dimension dimension)
        {
            _lastDimension = dimension;
            if (_dimensionCooldownText == null)
                return;

            Color accent = dimension == Dimension.Void ? Purple : Cyan;
            _dimensionIcon.color = accent;
            _dimensionStatusText.text = dimension == Dimension.Void ? "THE VOID" : "REALITY";
            _dimensionStatusText.color = accent;
        }

        void HandleModalKeyboard()
        {
            if (!_modalVisible || !_modalHasButton || GameManager.Instance == null)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.rKey.wasPressedThisFrame ||
                keyboard.enterKey.wasPressedThisFrame ||
                keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                OnRestartClicked();
            }
        }

        // ------------------------------------------------------------------
        // Gameplay events
        // ------------------------------------------------------------------

        void OnLivesChanged(int lives)
        {
            if (_lifeIcons.Count == 0)
                return;

            bool animate = _lastLives >= 0 && lives < _lastLives;
            int lostIndex = lives;

            for (int i = 0; i < _lifeIcons.Count; i++)
            {
                Image icon = _lifeIcons[i];
                bool alive = i < lives;

                icon.DOKill();
                if (alive)
                {
                    icon.color = TextColor;
                    icon.rectTransform.localScale = Vector3.one;
                }
                else
                {
                    icon.color = new Color(0.4f, 0.16f, 0.18f, 0.55f);
                    icon.rectTransform.localScale = Vector3.one * 0.86f;
                }
            }

            if (animate && lostIndex >= 0 && lostIndex < _lifeIcons.Count)
            {
                Image lost = _lifeIcons[lostIndex];
                lost.rectTransform.localScale = Vector3.one;
                lost.rectTransform.DOPunchScale(Vector3.one * 0.35f, 0.35f, 8, 0.6f).SetUpdate(true);
            }

            if (lives < _lastLives && lives > 0)
                ShowNotification($"LIFE LOST  -  {lives} REMAIN", 2.4f, Blood);
            else if (lives == 0 && _lastLives > 0)
                ShowNotification("NO LIVES LEFT", 2.4f, Danger);

            _lastLives = lives;
        }

        void OnRelicsChanged(int collected, int goal)
        {
            ApplyRelics(collected, goal);
        }

        void ApplyRelics(int collected, int goal)
        {
            if (_relicsText != null)
                _relicsText.text = $"RELICS  {collected}/{goal}";
        }

        void OnHealthChanged(int current, int max)
        {
            AnimateHealth(current, max);
        }

        void OnDamaged(int current, int max)
        {
            bool fatal = current <= 0;
            float strength = fatal ? 0.98f : 0.66f;

            PlayBlood(strength, fatal);
            PlayHealthFlash(fatal);

            CameraShake shake = CameraShake.Instance;
            shake?.Shake(fatal ? 0.55f : 0.18f, fatal ? 0.55f : 0.25f);

            if (!fatal)
                ShowNotification($"HIT  -  {current}/{max} VITALITY", 1.1f, Danger);
        }

        void OnDied()
        {
            _healthStateText.text = "CRITICAL";
            _healthStateText.color = Danger;
        }

        void OnRespawned()
        {
            ClearBlood();
            CameraShake.Instance?.ResetShake();

            if (_boundHealth != null)
                ApplyHealthImmediate(_boundHealth.CurrentHealth, _boundHealth.MaxHealth);

            ShowNotification(
                $"RESPAWNED  -  {PlayerHealth.Instance?.ImmunityRemaining ?? 0f:0.0}s SHIELD",
                2.4f,
                Cyan);
        }

        void OnStateChanged(GameState state)
        {
            switch (state)
            {
                case GameState.Paused:
                    ShowModal("PAUSED", "ESC TO RESUME", string.Empty, false, false);
                    break;
                case GameState.GameOver:
                    ShowModal("GAME OVER", "THE OTHER SIDE HAS YOU", BuildStatsLine(), true, false);
                    break;
                case GameState.Won:
                    ShowModal("ESCAPED", "EVERY RELIC CLAIMED", BuildStatsLine(), true, true);
                    break;
                default:
                    HideModal();
                    break;
            }
        }

        void OnDimensionChanged(Dimension previous, Dimension current)
        {
            ApplyDimensionImmediate(current);
            ShowNotification(
                current == Dimension.Void ? "ENTERED THE VOID" : "RETURNED TO REALITY",
                2f,
                current == Dimension.Void ? Purple : Cyan);
        }

        void OnDimensionTransitionStarted(Dimension previous, Dimension next)
        {
            if (_dimensionCooldownText != null)
                _dimensionCooldownText.text = "CROSSING OVER...";
        }

        void OnTimeFreezeActivated()
        {
            _freezeCard.DOKill();
            _freezeCard.localScale = Vector3.one;
            _freezeCard.DOPunchScale(Vector3.one * 0.04f, 0.3f, 8, 0.5f).SetUpdate(true);

            ShowNotification("TIME FREEZE  -  GHOST FROZEN", 2.2f, Cyan);
            RefreshAbilityCards();
        }

        void OnTimeFreezeReady()
        {
            RefreshAbilityCards();
            ShowNotification("TIME FREEZE READY", 1.8f, Cyan);
        }

        // ------------------------------------------------------------------
        // Health / damage feedback
        // ------------------------------------------------------------------

        void ApplyHealthImmediate(int current, int max)
        {
            float target = max <= 0 ? 0f : Mathf.Clamp01((float)current / max);

            _healthTween?.Kill();
            if (_healthFill != null)
                _healthFill.fillAmount = target;
            if (_healthText != null)
                _healthText.text = $"{current} / {max}";

            UpdateHealthColor(current, max, false);
            UpdateHealthState(current, max);
            UpdateLowHealthFeedback(current, max);
        }

        void AnimateHealth(int current, int max)
        {
            if (_healthFill == null)
                return;

            float target = max <= 0 ? 0f : Mathf.Clamp01((float)current / max);

            _healthTween?.Kill();
            _healthTween = DOTween.To(
                    () => _healthFill.fillAmount,
                    value => _healthFill.fillAmount = value,
                    target,
                    0.28f)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);

            if (_healthText != null)
                _healthText.text = $"{current} / {max}";

            UpdateHealthColor(current, max, true);
            UpdateHealthState(current, max);
            UpdateLowHealthFeedback(current, max);
        }

        void UpdateHealthColor(int current, int max, bool animate)
        {
            Color color = current <= 0 ? Blood
                : current <= 2 ? Danger
                : current <= 3 ? Warning
                : Healthy;

            if (_healthFill == null)
                return;

            _healthColorTween?.Kill();
            if (animate)
                _healthColorTween = _healthFill.DOColor(color, 0.2f).SetUpdate(true);
            else
                _healthFill.color = color;
        }

        void UpdateHealthState(int current, int max)
        {
            if (_healthStateText == null)
                return;

            if (current <= 0)
            {
                _healthStateText.text = "CRITICAL";
                _healthStateText.color = Danger;
            }
            else if (current <= 2)
            {
                _healthStateText.text = "WOUNDED";
                _healthStateText.color = Danger;
            }
            else if (current <= 3)
            {
                _healthStateText.text = "UNSTABLE";
                _healthStateText.color = Warning;
            }
            else
            {
                _healthStateText.text = "STABLE";
                _healthStateText.color = MutedTextColor;
            }
        }

        void UpdateLowHealthFeedback(int current, int max)
        {
            if (_lowHealthGroup == null)
                return;

            bool low = current > 0 && current <= 2;
            if (low)
            {
                if (_lowHealthTween != null && _lowHealthTween.IsActive())
                    return;

                _lowHealthGroup.DOKill();
                _lowHealthGroup.alpha = 0.08f;
                _lowHealthTween = _lowHealthGroup
                    .DOFade(0.26f, 1.05f)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }
            else
            {
                _lowHealthTween?.Kill();
                _lowHealthTween = null;
                _lowHealthGroup.DOKill();
                _lowHealthGroup.DOFade(0f, 0.3f).SetUpdate(true);
            }
        }

        void PlayHealthFlash(bool fatal)
        {
            if (_healthFlash == null)
                return;

            _healthFlashTween?.Kill();
            _healthFlash.color = new Color(1f, 0.12f, 0.1f, fatal ? 0.7f : 0.35f);
            _healthFlashTween = _healthFlash.DOFade(0f, fatal ? 0.65f : 0.35f).SetUpdate(true);
        }

        void PlayBlood(float strength, bool fatal)
        {
            if (_bloodGroup == null)
                return;

            _bloodTween?.Kill();
            _bloodGroup.gameObject.SetActive(true);
            _bloodGroup.alpha = 0f;

            Sequence sequence = DOTween.Sequence().SetUpdate(true);
            sequence.Append(_bloodGroup.DOFade(strength, fatal ? 0.06f : 0.05f).SetEase(Ease.OutQuad));
            sequence.AppendInterval(fatal ? 0.42f : 0.1f);
            sequence.Append(_bloodGroup.DOFade(0f, fatal ? 1.0f : 0.55f).SetEase(Ease.InQuad));
            sequence.OnComplete(() =>
            {
                if (_bloodGroup != null)
                    _bloodGroup.alpha = 0f;
            });

            _bloodTween = sequence;
        }

        void ClearBlood()
        {
            _bloodTween?.Kill();
            _bloodTween = null;
            if (_bloodGroup == null)
                return;

            _bloodGroup.DOKill();
            _bloodGroup.DOFade(0f, 0.18f).SetUpdate(true);
        }

        // ------------------------------------------------------------------
        // Modal
        // ------------------------------------------------------------------

        string BuildStatsLine()
        {
            float seconds = GameManager.Instance != null ? GameManager.Instance.SurvivalTime : 0f;
            int whole = Mathf.Max(0, Mathf.FloorToInt(seconds));
            int relics = GameManager.Instance != null ? GameManager.Instance.RelicsCollected : 0;
            return $"SURVIVED {whole / 60:00}:{whole % 60:00}   |   RELICS {relics}/{GameManager.RelicGoal}";
        }

        void ShowModal(string title, string body, string stats, bool showButton, bool won)
        {
            if (!_built || _modalGroup == null)
                return;

            _modalVisible = true;
            _modalHasButton = showButton;

            _modalTitle.text = title;
            _modalTitle.color = showButton ? (won ? Cyan : Blood) : TextColor;
            _modalBody.text = body;
            _modalStats.text = stats;
            _modalStats.gameObject.SetActive(!string.IsNullOrEmpty(stats));
            _restartButton.gameObject.SetActive(showButton);
            _modalButtonLabel.text = won ? "PLAY AGAIN" : "RESTART";

            _modalBackdrop.color = won
                ? new Color(0.01f, 0.02f, 0.03f, 0.74f)
                : new Color(0.02f, 0.005f, 0.008f, 0.8f);

            _modalGroup.gameObject.SetActive(true);
            _modalGroup.transform.SetAsLastSibling();
            _modalGroup.interactable = showButton;
            _modalGroup.blocksRaycasts = true;

            _modalTween?.Kill();
            _modalGroup.alpha = 0f;
            _modalPanel.localScale = Vector3.one * 0.94f;

            Sequence sequence = DOTween.Sequence().SetUpdate(true);
            sequence.Append(_modalGroup.DOFade(1f, 0.18f));
            sequence.Join(_modalPanel.DOScale(1f, 0.24f).SetEase(Ease.OutBack));
            _modalTween = sequence;

            if (showButton && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_restartButton.gameObject);
        }

        void HideModal()
        {
            if (!_built || _modalGroup == null || !_modalGroup.gameObject.activeSelf)
                return;

            _modalVisible = false;
            _modalHasButton = false;
            _modalGroup.interactable = false;
            _modalGroup.blocksRaycasts = false;

            _modalTween?.Kill();
            _modalTween = _modalGroup.DOFade(0f, 0.16f)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (_modalGroup != null)
                        _modalGroup.gameObject.SetActive(false);
                });
        }

        void OnRestartClicked()
        {
            if (_restarting || GameManager.Instance == null)
                return;

            _restarting = true;
            _modalTween?.Kill();
            _modalTween = _modalGroup.DOFade(0f, 0.12f)
                .SetUpdate(true)
                .OnComplete(() => GameManager.Instance?.RestartGame());
        }

        // ------------------------------------------------------------------
        // Style assets (procedural, free, no external dependencies)
        // ------------------------------------------------------------------

        static readonly Dictionary<string, TMP_FontAsset> FontCache = new Dictionary<string, TMP_FontAsset>();

        static TMP_FontAsset LoadFontAsset(string resourcePath)
        {
            if (FontCache.TryGetValue(resourcePath, out TMP_FontAsset cached) && cached != null)
                return cached;

            Font font = Resources.Load<Font>(resourcePath);
            if (font == null)
                return null;

            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font);
            if (asset == null)
                return null;

            asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            asset.isMultiAtlasTexturesEnabled = true;
            FontCache[resourcePath] = asset;
            return asset;
        }

        static TMP_FontAsset DefaultFont()
        {
            // TMP_Settings.defaultFontAsset throws when the TMP Essential Resources
            // package has not been imported yet, which must not break the HUD.
            try
            {
                if (TMP_Settings.defaultFontAsset != null)
                    return TMP_Settings.defaultFontAsset;
            }
            catch (System.Exception)
            {
                // Missing TMP settings asset: fall through to the resource lookup.
            }

            TMP_FontAsset fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (fallback != null)
                return fallback;

            // Last resort for a project without TMP essentials: build an atlas from
            // any font Unity can load. Rajdhani is shipped in Resources for this HUD.
            Font font = Resources.Load<Font>("UI/Fonts/Rajdhani-Regular");
            return font != null ? TMP_FontAsset.CreateFontAsset(font) : null;
        }

        static Sprite _cachedPanel;
        static Sprite _cachedSoftPanel;
        static Sprite _cachedBar;
        static Sprite _cachedFlat;
        static Sprite _cachedHeart;
        static Sprite _cachedFreeze;
        static Sprite _cachedDimension;
        static Texture2D _cachedBlood;

        static Sprite GetPanelSprite(bool soft)
        {
            if (soft)
            {
                if (_cachedSoftPanel == null)
                    _cachedSoftPanel = CreateRoundedSprite(96, 96, 18, new Color(1f, 1f, 1f, 0.94f), 20);
                return _cachedSoftPanel;
            }

            if (_cachedPanel == null)
                _cachedPanel = CreateRoundedSprite(96, 96, 18, Color.white, 20);
            return _cachedPanel;
        }

        static Sprite GetBarSprite()
        {
            if (_cachedBar == null)
                _cachedBar = CreateRoundedSprite(64, 24, 9, Color.white, 10);
            return _cachedBar;
        }

        static Sprite GetFlatSprite()
        {
            if (_cachedFlat == null)
            {
                Texture2D texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                Color32[] pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(255, 255, 255, 255);
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                _cachedFlat = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);
            }

            return _cachedFlat;
        }

        static Sprite GetHeartSprite()
        {
            if (_cachedHeart != null)
                return _cachedHeart;

            const int size = 96;
            Texture2D texture = NewUiTexture(size, size);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2.5f - 1.25f;
                    float ny = (y + 0.5f) / size * 2.4f - 1.15f;
                    float a = nx * nx + ny * ny - 1f;
                    float inside = a * a * a - nx * nx * ny * ny * ny;
                    float alpha = inside <= 0f ? 1f : 0f;

                    // A soft 1px edge keeps the icon readable at small sizes.
                    if (alpha == 0f && inside < 0.06f)
                        alpha = Mathf.Clamp01(1f - inside / 0.06f) * 0.7f;

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _cachedHeart = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _cachedHeart;
        }

        static Sprite GetFreezeSprite()
        {
            if (_cachedFreeze != null)
                return _cachedFreeze;

            const int size = 96;
            Texture2D texture = NewUiTexture(size, size);
            Color32[] pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - center;
                    float alpha = 0f;

                    for (int arm = 0; arm < 6; arm++)
                    {
                        float angle = arm * Mathf.PI / 3f;
                        Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                        alpha = Mathf.Max(alpha, LineAlpha(p, dir * 37f, 1.9f, 1.1f));

                        Vector2 branchBase = dir * 20f;
                        Vector2 side = new Vector2(-dir.y, dir.x);
                        alpha = Mathf.Max(alpha, LineAlpha(p, branchBase + dir * 9f + side * 9f, 1.5f, 1f));
                        alpha = Mathf.Max(alpha, LineAlpha(p, branchBase + dir * 9f - side * 9f, 1.5f, 1f));
                    }

                    alpha = Mathf.Max(alpha, Mathf.Clamp01(1f - p.magnitude / 5f));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _cachedFreeze = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _cachedFreeze;
        }

        static Sprite GetDimensionSprite()
        {
            if (_cachedDimension != null)
                return _cachedDimension;

            const int size = 96;
            Texture2D texture = NewUiTexture(size, size);
            Color32[] pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - center;
                    float radius = p.magnitude;
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(radius - 34f) / 3.5f);
                    float diamond = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Abs(p.x) + Mathf.Abs(p.y) - 17f) / 2.4f);
                    float dot = Mathf.Clamp01(1f - radius / 5f);
                    float alpha = Mathf.Max(ring, Mathf.Max(diamond * 0.8f, dot));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _cachedDimension = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _cachedDimension;
        }

        static Texture2D GetBloodTexture()
        {
            if (_cachedBlood != null)
                return _cachedBlood;

            const int size = 512;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color32[] pixels = new Color32[size * size];
            System.Random random = new System.Random(7331);
            var blobs = new List<Vector4>();
            var drops = new List<Vector4>();

            for (int i = 0; i < 34; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float radius = 0.62f + (float)random.NextDouble() * 0.42f;
                float blobSize = 0.035f + (float)random.NextDouble() * 0.10f;
                blobs.Add(new Vector4(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    blobSize,
                    0.55f + (float)random.NextDouble() * 0.45f));
            }

            for (int i = 0; i < 120; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float radius = 0.48f + (float)random.NextDouble() * 0.62f;
                float dropSize = 0.004f + (float)random.NextDouble() * 0.018f;
                drops.Add(new Vector4(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    dropSize,
                    0.35f + (float)random.NextDouble() * 0.65f));
            }

            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float radius = Mathf.Sqrt(nx * nx + ny * ny) / 1.4142f;

                    // Clear centre keeps the player's view unobstructed.
                    float vignette = Mathf.SmoothStep(0.42f, 1f, radius) * 0.92f;
                    float alpha = vignette;

                    for (int i = 0; i < blobs.Count; i++)
                    {
                        Vector4 blob = blobs[i];
                        float dx = nx - blob.x;
                        float dy = ny - blob.y;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float v = Mathf.Clamp01(1f - d / blob.z);
                        alpha = Mathf.Max(alpha, v * v * blob.w);
                    }

                    for (int i = 0; i < drops.Count; i++)
                    {
                        Vector4 drop = drops[i];
                        float dx = nx - drop.x;
                        float dy = ny - drop.y;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d < drop.z)
                            alpha = Mathf.Max(alpha, (1f - d / drop.z) * drop.w);
                    }

                    alpha *= Mathf.SmoothStep(0.24f, 0.58f, radius);
                    byte a = (byte)(Mathf.Clamp01(alpha) * 255f);
                    pixels[y * size + x] = new Color32(90, 2, 5, a);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _cachedBlood = texture;
            return _cachedBlood;
        }

        static Texture2D NewUiTexture(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        static Sprite CreateRoundedSprite(int width, int height, int radius, Color color, int border)
        {
            Texture2D texture = NewUiTexture(width, height);
            Color32[] pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = Mathf.Max(Mathf.Max(radius - x, 0f), x - (width - radius - 1));
                    float dy = Mathf.Max(Mathf.Max(radius - y, 0f), y - (height - radius - 1));
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius + 0.5f - distance);
                    pixels[y * width + x] = new Color32(
                        (byte)(color.r * 255f),
                        (byte)(color.g * 255f),
                        (byte)(color.b * 255f),
                        (byte)(color.a * alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
        }

        static float LineAlpha(Vector2 point, Vector2 end, float thickness, float falloff)
        {
            Vector2 delta = end;
            float t = Mathf.Clamp01(Vector2.Dot(point, delta) / Mathf.Max(0.001f, delta.sqrMagnitude));
            float distance = (point - delta * t).magnitude;
            return Mathf.Clamp01(1f - Mathf.Max(0f, distance - thickness) / Mathf.Max(0.001f, falloff));
        }

        void KillTweens()
        {
            _healthTween?.Kill();
            _healthColorTween?.Kill();
            _bloodTween?.Kill();
            _notificationTween?.Kill();
            _modalTween?.Kill();
            _lowHealthTween?.Kill();
            _healthFlashTween?.Kill();
        }
    }
}
