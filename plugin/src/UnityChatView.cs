using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LilithMod;

internal sealed class UnityChatView : IChatView
{
    private const float CompactHeight = 156f;
    private const float ExpandedHeight = 500f;
    private const float PanelWidth = 560f;
    private const float SafeMargin = 18f;
    private const float HistoryWheelStep = 72f;
    private const float HistoryScrollSharpness = 16f;
    private const float HistoryRowGap = 10f;
    private const float HistoryRowMinimum = 58f;
    private const float HistoryBodyPadding = 24f;
    private const int MaxHistoryRows = 24;

    private static readonly Color White = new(0.94f, 0.94f, 0.92f, 1f);
    private static readonly Color SoftWhite = new(0.78f, 0.78f, 0.76f, 1f);
    private static readonly Color Red = new(0.66f, 0.04f, 0.06f, 1f);
    private static readonly Color RedHover = new(0.82f, 0.08f, 0.1f, 1f);
    private static readonly Color Black = new(0.025f, 0.025f, 0.025f, 0.97f);
    private static readonly Color Dark = new(0.075f, 0.075f, 0.075f, 0.97f);

    private readonly Action<string> _onSubmit;
    private readonly Action _onConfigure;
    private readonly Action _onResetSession;
    private readonly Action _onForgetPlayer;
    private readonly string _preferencesPath;
    private readonly IChatFocusController _focusController;
    private readonly ICharacterScreenLocator _characterScreenLocator;
    private readonly List<ChatMessage> _messages = new();
    private readonly List<HistoryRowView> _historyRows = new();

    private GameObject? _root;
    private GameObject? _ownedEventSystem;
    private Canvas? _canvas;
    private CanvasScaler? _canvasScaler;
    private RectTransform? _canvasRect;
    private RectTransform? _panel;
    private RectTransform? _header;
    private GameObject? _historyRoot;
    private RectTransform? _historyContent;
    private ScrollRect? _historyScroll;
    private TMP_InputField? _input;
    private RectTransform? _inputViewport;
    private Button? _sendButton;
    private TextMeshProUGUI? _titleLabel;
    private TextMeshProUGUI? _historyButtonLabel;
    private TextMeshProUGUI? _settingsButtonLabel;
    private TextMeshProUGUI? _placeholderLabel;
    private TextMeshProUGUI? _sendButtonLabel;
    private TextMeshProUGUI? _resetLabel;
    private TextMeshProUGUI? _statusText;
    private GameObject? _memoryMenu;
    private TextMeshProUGUI? _forgetLabel;
    private TMP_FontAsset? _font;
    private Sprite? _avatar;
    private Texture2D? _avatarTexture;
    private Sprite? _fallbackBackground;
    private Texture2D? _fallbackBackgroundTexture;
    private bool _expanded;
    private bool _dragging;
    private bool _forgetArmed;
    private bool _historyScrollAnimating;
    private bool _historyDirty = true;
    private float _historyScrollTarget;
    private Vector2 _dragPointerStart;
    private Vector2 _dragPanelStart;
    private float _userScale = 1f;
    private bool _hasSessionPosition;
    private float _sessionPositionX = 0.5f;
    private float _sessionPositionY = 0.08f;
    private int _lastScreenWidth;
    private int _lastScreenHeight;
    private float _nextPreferencesPoll;
    private DateTime _preferencesWriteTimeUtc;
    private int _restoreInputFocusFrame = -1;
    private string _locale = "";
    private bool _disposed;

    public UnityChatView(
        Action<string> onSubmit,
        Action onConfigure,
        Action onResetSession,
        Action onForgetPlayer)
    {
        _onSubmit = onSubmit;
        _onConfigure = onConfigure;
        _onResetSession = onResetSession;
        _onForgetPlayer = onForgetPlayer;
        _preferencesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LilithAI",
            "ui.json");
        _focusController = ChatFocusController.Create();
        _characterScreenLocator = CharacterScreenLocator.Create();
        Build();
    }

    public bool IsVisible => _root is not null && _root.activeSelf;

    public bool PointerOverPanel => IsVisible
        && _panel is not null
        && RectTransformUtility.RectangleContainsScreenPoint(_panel, Input.mousePosition, null);

    public void Show(ChatOpenReason reason)
    {
        if (_root is null)
        {
            return;
        }
        _root.SetActive(true);
        SetExpanded(reason == ChatOpenReason.LilithBubble);
        ApplyOpenPosition();
        if (!_hasSessionPosition)
        {
            _hasSessionPosition = true;
            CaptureSessionPosition();
            LilithModPlugin.PluginLog.LogInfo(
                $"Chat session position initialized: x={_sessionPositionX:0.000}; " +
                $"y={_sessionPositionY:0.000}");
        }
        ScrollToBottom();
        RequestInputFocus();
    }

    public void Hide()
    {
        _focusController.Release();
        _memoryMenu?.SetActive(false);
        _forgetArmed = false;
        _root?.SetActive(false);
    }

    public void SetMessages(IReadOnlyList<ChatMessage> messages)
    {
        if (MessagesEqual(messages))
        {
            return;
        }
        _messages.Clear();
        _messages.AddRange(messages);
        MarkHistoryDirty();
    }

    public void AddMessage(ChatMessage message)
    {
        _messages.Add(message);
        MarkHistoryDirty();
    }

    public void ClearMessages()
    {
        _messages.Clear();
        MarkHistoryDirty();
    }

    public void SetBusy(bool busy, string status = "")
    {
        if (_sendButton is not null)
        {
            _sendButton.interactable = !busy;
        }
        if (_statusText is not null)
        {
            _statusText.text = busy
                ? (string.IsNullOrWhiteSpace(status) ? ModLocalization.T("chat.status.connecting") : status)
                : string.Empty;
            _statusText.color = busy ? RedHover : SoftWhite;
        }
    }

    public void Tick()
    {
        if (!IsVisible || _panel is null || _header is null || _canvasRect is null)
        {
            return;
        }

        bool pointerOverPanel = PointerOverPanel;
        if (pointerOverPanel)
        {
            PassThroughMouseHook.SetOverCharacter(true);
        }

        UpdateSmoothHistoryScroll();
        UpdateInputFocus();

        if (Input.GetMouseButtonDown(0)
            && _inputViewport is not null
            && RectTransformUtility.RectangleContainsScreenPoint(_inputViewport, Input.mousePosition, null))
        {
            RequestInputFocus();
        }

        if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight)
        {
            UpdateCanvasScale();
            Canvas.ForceUpdateCanvases();
            LoadPreferences();
            if (_hasSessionPosition)
            {
                ApplySessionPosition();
                ClampToCanvas();
                CaptureSessionPosition();
            }
            else
            {
                ApplyOpenPosition();
            }
        }
        if (Time.unscaledTime >= _nextPreferencesPoll)
        {
            _nextPreferencesPoll = Time.unscaledTime + 1f;
            ReloadPreferencesIfChanged();
            ApplyLocalization();
        }

        if (Input.GetMouseButtonDown(0)
            && RectTransformUtility.RectangleContainsScreenPoint(_header, Input.mousePosition, null)
            && ScreenPointInCanvas(Input.mousePosition, out Vector2 pointer))
        {
            _dragging = true;
            _dragPointerStart = pointer;
            _dragPanelStart = _panel.anchoredPosition;
        }
        if (_dragging && Input.GetMouseButton(0) && ScreenPointInCanvas(Input.mousePosition, out Vector2 current))
        {
            _panel.anchoredPosition = _dragPanelStart + current - _dragPointerStart;
            ClampToCanvas();
        }
        if (_dragging && Input.GetMouseButtonUp(0))
        {
            _dragging = false;
            _hasSessionPosition = true;
            CaptureSessionPosition();
            LilithModPlugin.PluginLog.LogInfo(
                $"Chat session position updated: x={_sessionPositionX:0.000}; " +
                $"y={_sessionPositionY:0.000}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _focusController.Dispose();
        if (_root is not null)
        {
            Object.Destroy(_root);
        }
        if (_ownedEventSystem is not null)
        {
            Object.Destroy(_ownedEventSystem);
        }
        if (_avatar is not null)
        {
            Object.Destroy(_avatar);
        }
        if (_avatarTexture is not null)
        {
            Object.Destroy(_avatarTexture);
        }
        if (_fallbackBackground is not null)
        {
            Object.Destroy(_fallbackBackground);
        }
        if (_fallbackBackgroundTexture is not null)
        {
            Object.Destroy(_fallbackBackgroundTexture);
        }
    }

    private void Build()
    {
        _font = FindFont(ModLocalization.CurrentLocale);
        _avatar = LoadAvatar();
        Sprite background = FindSprite("Settings_Bg_FixSize") ?? CreateCheckerBackground();

        _root = CreateObject("LilithMod.ChatCanvas", null);
        Object.DontDestroyOnLoad(_root);
        _canvas = _root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = short.MaxValue - 20;
        _canvasScaler = _root.AddComponent<CanvasScaler>();
        _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        UpdateCanvasScale();
        _root.AddComponent<GraphicRaycaster>();
        _canvasRect = _root.GetComponent<RectTransform>();
        EnsureEventSystem();

        GameObject panelObject = CreateObject("Panel", _root.transform);
        _panel = panelObject.GetComponent<RectTransform>();
        _panel.anchorMin = Vector2.zero;
        _panel.anchorMax = Vector2.zero;
        _panel.pivot = Vector2.zero;
        _panel.sizeDelta = new Vector2(PanelWidth, CompactHeight);
        _panel.anchoredPosition = new Vector2(24f, 80f);
        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.sprite = background;
        panelImage.type = background.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Tiled;
        panelImage.color = Color.white;
        AddHardBorder(panelObject.transform);

        GameObject headerObject = CreateObject("Header", panelObject.transform);
        _header = headerObject.GetComponent<RectTransform>();
        SetTopStretch(_header, 0f, 0f, 0f, 42f);
        Image headerImage = headerObject.AddComponent<Image>();
        headerImage.color = Black;

        RectTransform accent = AddImage("Accent", headerObject.transform, Red);
        SetAnchored(accent, 0f, 0f, 8f, 42f, false, true);

        RectTransform avatarRect = AddImage("Avatar", headerObject.transform, Color.white, _avatar);
        SetAnchored(avatarRect, 15f, -3f, 36f, 36f, false, true);

        _titleLabel = AddText("Title", headerObject.transform, string.Empty, 20f, White, FontStyles.Bold);
        SetAnchored(_titleLabel.rectTransform, 59f, -5f, 220f, 32f, false, true);

        _statusText = AddText("Status", headerObject.transform, string.Empty, 14f, SoftWhite);
        _statusText.alignment = TextAlignmentOptions.MidlineRight;
        SetAnchored(_statusText.rectTransform, 280f, -7f, 66f, 28f, false, true);

        Button historyButton = AddButton(
            "HistoryButton",
            headerObject.transform,
            string.Empty,
            350f,
            -5f,
            68f,
            32f,
            ToggleHistory);
        _historyButtonLabel = historyButton.GetComponentInChildren<TextMeshProUGUI>();
        Button settingsButton = AddButton("SettingsButton", headerObject.transform, string.Empty, 422f, -5f, 68f, 32f, _onConfigure);
        _settingsButtonLabel = settingsButton.GetComponentInChildren<TextMeshProUGUI>();
        AddButton("MemoryButton", headerObject.transform, "⋯", 494f, -5f, 28f, 32f, ToggleMemoryMenu);
        AddButton("CloseButton", headerObject.transform, "×", 524f, -5f, 28f, 32f, Hide);

        BuildHistory(panelObject.transform);
        BuildComposer(panelObject.transform);
        BuildMemoryMenu(panelObject.transform);
        ApplyLocalization(force: true);
        LoadPreferences();
        SetExpanded(false);
        _root.SetActive(false);
    }

    private void BuildHistory(Transform parent)
    {
        _historyRoot = CreateObject("History", parent);
        RectTransform rootRect = _historyRoot.GetComponent<RectTransform>();
        Stretch(rootRect, 12f, 50f, 12f, 104f);
        Image rootImage = _historyRoot.AddComponent<Image>();
        rootImage.color = new Color(0.02f, 0.02f, 0.02f, 0.93f);

        GameObject viewportObject = CreateObject("Viewport", _historyRoot.transform);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        Stretch(viewport, 6f, 6f, 6f, 6f);
        Image viewportImage = viewportObject.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        Mask mask = viewportObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject contentObject = CreateObject("Content", viewportObject.transform);
        _historyContent = contentObject.GetComponent<RectTransform>();
        _historyContent.anchorMin = new Vector2(0f, 1f);
        _historyContent.anchorMax = new Vector2(1f, 1f);
        _historyContent.pivot = new Vector2(0.5f, 1f);
        _historyContent.anchoredPosition = Vector2.zero;
        _historyContent.sizeDelta = Vector2.zero;

        _historyScroll = _historyRoot.AddComponent<ScrollRect>();
        _historyScroll.viewport = viewport;
        _historyScroll.content = _historyContent;
        _historyScroll.horizontal = false;
        _historyScroll.vertical = true;
        _historyScroll.movementType = ScrollRect.MovementType.Clamped;
        _historyScroll.scrollSensitivity = 0f;
    }

    private void BuildComposer(Transform parent)
    {
        GameObject composer = CreateObject("Composer", parent);
        RectTransform composerRect = composer.GetComponent<RectTransform>();
        SetBottomStretch(composerRect, 12f, 12f, 12f, 82f);
        Image composerImage = composer.AddComponent<Image>();
        composerImage.color = Dark;
        AddHardBorder(composer.transform, 1f, new Color(0.65f, 0.65f, 0.63f, 1f));

        GameObject viewportObject = CreateObject("InputViewport", composer.transform);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        _inputViewport = viewport;
        Stretch(viewport, 11f, 8f, 91f, 8f);
        Mask mask = viewportObject.AddComponent<Mask>();
        Image maskImage = viewportObject.AddComponent<Image>();
        maskImage.color = new Color(0f, 0f, 0f, 0.01f);
        mask.showMaskGraphic = false;

        TextMeshProUGUI inputText = AddText("InputText", viewportObject.transform, string.Empty, 18f, White);
        inputText.enableWordWrapping = true;
        inputText.alignment = TextAlignmentOptions.TopLeft;
        Stretch(inputText.rectTransform, 2f, 2f, 2f, 2f);

        _placeholderLabel = AddText("Placeholder", viewportObject.transform, string.Empty, 18f, new Color(0.55f, 0.55f, 0.53f, 1f));
        _placeholderLabel.fontStyle = FontStyles.Italic;
        Stretch(_placeholderLabel.rectTransform, 2f, 2f, 2f, 2f);

        _input = composer.AddComponent<TMP_InputField>();
        _input.textViewport = viewport;
        _input.textComponent = inputText;
        _input.placeholder = _placeholderLabel;
        _input.lineType = TMP_InputField.LineType.MultiLineSubmit;
        _input.characterLimit = 2000;
        _input.richText = false;
        _input.onSubmit.AddListener((UnityAction<string>)SubmitFromInput);
        _input.onSelect.AddListener((UnityAction<string>)InputSelected);

        _sendButton = AddButton("SendButton", composer.transform, string.Empty, 453f, 9f, 73f, 52f, SubmitCurrentInput);
        _sendButtonLabel = _sendButton.GetComponentInChildren<TextMeshProUGUI>();
    }

    private void BuildMemoryMenu(Transform parent)
    {
        _memoryMenu = CreateObject("MemoryMenu", parent);
        RectTransform menuRect = _memoryMenu.GetComponent<RectTransform>();
        menuRect.anchorMin = new Vector2(1f, 1f);
        menuRect.anchorMax = new Vector2(1f, 1f);
        menuRect.pivot = new Vector2(1f, 1f);
        menuRect.sizeDelta = new Vector2(242f, 92f);
        menuRect.anchoredPosition = new Vector2(-8f, -46f);
        Image image = _memoryMenu.AddComponent<Image>();
        image.color = Black;
        AddHardBorder(_memoryMenu.transform, 1f);

        Button reset = AddButton("ResetSession", _memoryMenu.transform, string.Empty, 7f, 49f, 228f, 35f, () =>
        {
            _memoryMenu?.SetActive(false);
            _onResetSession();
        }, false, false);
        _resetLabel = reset.GetComponentInChildren<TextMeshProUGUI>();
        Button forget = AddButton("ForgetPlayer", _memoryMenu.transform, string.Empty, 7f, 8f, 228f, 35f, ConfirmForget, false, false);
        _forgetLabel = forget.GetComponentInChildren<TextMeshProUGUI>();
        _memoryMenu.SetActive(false);
    }

    private void SubmitFromInput(string value)
    {
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            return;
        }
        if (_sendButton is { interactable: false })
        {
            RequestInputFocus();
            return;
        }
        SubmitText(value);
    }

    private void InputSelected(string _)
    {
        LilithModPlugin.PluginLog.LogInfo("Unity chat input selected");
        _focusController.Acquire();
    }

    private void SubmitCurrentInput()
    {
        SubmitText(_input?.text ?? string.Empty);
    }

    private void SubmitText(string value)
    {
        string message = value.Trim();
        if (message.Length == 0 || _sendButton is { interactable: false })
        {
            RequestInputFocus();
            return;
        }
        if (_input is not null)
        {
            _input.text = string.Empty;
        }
        RequestInputFocus();
        _onSubmit(message);
    }

    private void RequestInputFocus()
    {
        // TMP_InputField deactivates itself after invoking onSubmit. Restoring in
        // the following frame avoids having that teardown overwrite our focus.
        _restoreInputFocusFrame = Time.frameCount + 1;
    }

    private void UpdateInputFocus()
    {
        if (_input is null)
        {
            return;
        }

        EventSystem? eventSystem = EventSystem.current;
        bool anotherUnityControlSelected = eventSystem is not null
            && eventSystem.currentSelectedGameObject != _input.gameObject;
        if (_input.isFocused
            && (!_focusController.HasKeyboardFocus || anotherUnityControlSelected))
        {
            _input.DeactivateInputField();
            if (eventSystem?.currentSelectedGameObject == _input.gameObject)
            {
                eventSystem.SetSelectedGameObject(null);
            }
        }

        if (_restoreInputFocusFrame < 0 || Time.frameCount < _restoreInputFocusFrame)
        {
            return;
        }
        _restoreInputFocusFrame = -1;
        FocusInputNow();
    }

    private void FocusInputNow()
    {
        if (_input is null || !_input.interactable || !IsVisible)
        {
            return;
        }

        _focusController.Acquire();
        if (!_focusController.HasKeyboardFocus)
        {
            _input.DeactivateInputField();
            return;
        }

        EventSystem? eventSystem = EventSystem.current;
        eventSystem?.SetSelectedGameObject(_input.gameObject);
        _input.Select();
        _input.ActivateInputField();
        int end = _input.text?.Length ?? 0;
        _input.caretPosition = end;
        _input.selectionAnchorPosition = end;
        _input.selectionFocusPosition = end;
    }

    private void ToggleHistory()
    {
        SetExpanded(!_expanded);
        ScrollToBottom();
        LilithModPlugin.PluginLog.LogInfo($"Chat history expanded={_expanded}");
    }

    private void ToggleMemoryMenu()
    {
        if (_memoryMenu is null)
        {
            return;
        }
        bool show = !_memoryMenu.activeSelf;
        _forgetArmed = false;
        if (_forgetLabel is not null)
        {
            _forgetLabel.text = ModLocalization.T("chat.forget_player");
        }
        _memoryMenu.SetActive(show);
    }

    private void ConfirmForget()
    {
        if (!_forgetArmed)
        {
            _forgetArmed = true;
            if (_forgetLabel is not null)
            {
                _forgetLabel.text = ModLocalization.T("chat.forget_confirm");
                _forgetLabel.color = RedHover;
            }
            return;
        }
        _forgetArmed = false;
        _memoryMenu?.SetActive(false);
        _onForgetPlayer();
    }

    private void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        if (_historyButtonLabel is not null)
        {
            _historyButtonLabel.text = expanded
                ? ModLocalization.T("chat.hide_history")
                : ModLocalization.T("chat.history");
        }
        if (_panel is not null)
        {
            _panel.sizeDelta = new Vector2(
                PanelWidth,
                expanded ? AvailableExpandedHeight() : CompactHeight);
        }
        _historyRoot?.SetActive(expanded);
        if (expanded)
        {
            RenderHistory();
        }
        ClampToCanvas();
    }

    private float AvailableExpandedHeight()
    {
        if (_canvasRect is null)
        {
            return ExpandedHeight;
        }
        return ChatUiScale.FitPanelHeight(
            _canvasRect.rect.height,
            _userScale,
            CompactHeight,
            ExpandedHeight,
            SafeMargin);
    }

    private void RenderHistory()
    {
        if (!_expanded || !_historyDirty || _historyContent is null)
        {
            return;
        }

        float y = 0f;
        int firstMessage = Mathf.Max(0, _messages.Count - MaxHistoryRows);
        int visibleCount = _messages.Count - firstMessage;
        for (int rowIndex = 0; rowIndex < visibleCount; rowIndex++)
        {
            ChatMessage message = _messages[firstMessage + rowIndex];
            float historyWidth = _historyRoot?.GetComponent<RectTransform>().rect.width ?? 0f;
            float bodyWidth = historyWidth > 1f ? Mathf.Max(1f, historyWidth - 62f) : 454f;
            HistoryRowView row = GetOrCreateHistoryRow(rowIndex);
            row.Root.SetActive(true);
            RectTransform rowRect = row.Rect;
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.anchoredPosition = new Vector2(0f, -y);

            TextMeshProUGUI body = row.Body;
            body.text = message.Text;
            body.enableWordWrapping = true;
            body.alignment = TextAlignmentOptions.TopLeft;
            Vector2 preferred = body.GetPreferredValues(message.Text, bodyWidth, Mathf.Infinity);
            // TMP can render a wrapped line outside a short stretched RectTransform.
            // Leave enough vertical room for the measured text plus both padding and
            // keep overflow clipped so adjacent script entries cannot visually overlap.
            float rowHeight = Mathf.Max(HistoryRowMinimum, Mathf.Ceil(preferred.y) + HistoryBodyPadding);
            rowRect.sizeDelta = new Vector2(0f, rowHeight);
            body.overflowMode = TextOverflowModes.Truncate;

            if (message.Speaker == ChatSpeaker.Lilith)
            {
                row.Portrait.gameObject.SetActive(true);
                row.Speaker.gameObject.SetActive(false);
            }
            else
            {
                string name = message.Speaker == ChatSpeaker.User
                    ? ModLocalization.T("common.you")
                    : ModLocalization.T("common.system");
                row.Portrait.gameObject.SetActive(false);
                row.Speaker.gameObject.SetActive(true);
                row.Speaker.text = name;
                row.Speaker.color = message.Speaker == ChatSpeaker.System ? RedHover : SoftWhite;
            }

            body.color = message.Speaker == ChatSpeaker.System ? SoftWhite : White;
            y += rowHeight + HistoryRowGap;
        }
        for (int i = visibleCount; i < _historyRows.Count; i++)
        {
            _historyRows[i].Root.SetActive(false);
        }
        _historyContent.sizeDelta = new Vector2(0f, y);
        _historyDirty = false;
        ScrollToBottom();
    }

    private void MarkHistoryDirty()
    {
        _historyDirty = true;
        if (_expanded)
        {
            RenderHistory();
        }
    }

    private bool MessagesEqual(IReadOnlyList<ChatMessage> messages)
    {
        if (_messages.Count != messages.Count)
        {
            return false;
        }
        for (int i = 0; i < messages.Count; i++)
        {
            if (_messages[i] != messages[i])
            {
                return false;
            }
        }
        return true;
    }

    private HistoryRowView GetOrCreateHistoryRow(int index)
    {
        if (index < _historyRows.Count)
        {
            return _historyRows[index];
        }
        if (_historyContent is null)
        {
            throw new InvalidOperationException("Chat history is not initialized");
        }

        GameObject root = CreateObject("Message", _historyContent);
        RectTransform rect = root.GetComponent<RectTransform>();
        TextMeshProUGUI body = AddText("Body", root.transform, string.Empty, 18f, White);
        body.enableWordWrapping = true;
        body.alignment = TextAlignmentOptions.TopLeft;
        body.overflowMode = TextOverflowModes.Truncate;
        Stretch(body.rectTransform, 54f, 8f, 8f, 8f);

        RectTransform portraitRect = AddImage("Lilith", root.transform, Color.white, _avatar);
        SetAnchored(portraitRect, 8f, -8f, 36f, 36f, false, true);
        Image portrait = portraitRect.GetComponent<Image>();

        TextMeshProUGUI speaker = AddText(
            "Speaker",
            root.transform,
            string.Empty,
            15f,
            SoftWhite,
            FontStyles.Bold);
        speaker.alignment = TextAlignmentOptions.Top;
        SetAnchored(speaker.rectTransform, 5f, -9f, 42f, 28f, false, true);

        var row = new HistoryRowView(root, rect, body, portrait, speaker);
        _historyRows.Add(row);
        return row;
    }

    private void ScrollToBottom()
    {
        if (_historyScroll is null)
        {
            return;
        }
        Canvas.ForceUpdateCanvases();
        _historyScroll.verticalNormalizedPosition = 0f;
        _historyScrollTarget = 0f;
        _historyScrollAnimating = false;
    }

    private void UpdateSmoothHistoryScroll()
    {
        if (!_expanded || _historyScroll is null || _historyContent is null
            || _historyScroll.viewport is null || _historyRoot is null)
        {
            _historyScrollAnimating = false;
            return;
        }

        RectTransform historyRect = _historyRoot.GetComponent<RectTransform>();
        bool pointerOverHistory = RectTransformUtility.RectangleContainsScreenPoint(
            historyRect,
            Input.mousePosition,
            null);
        float wheelDelta = pointerOverHistory ? Input.mouseScrollDelta.y : 0f;
        float scrollableHeight = Mathf.Max(
            0f,
            _historyContent.rect.height - _historyScroll.viewport.rect.height);

        if (Mathf.Abs(wheelDelta) > 0.001f && scrollableHeight > 0.5f)
        {
            if (!_historyScrollAnimating)
            {
                _historyScrollTarget = _historyScroll.verticalNormalizedPosition;
            }
            _historyScrollTarget = Mathf.Clamp01(
                _historyScrollTarget + (wheelDelta * HistoryWheelStep / scrollableHeight));
            _historyScrollAnimating = true;
        }

        if (!_historyScrollAnimating)
        {
            _historyScrollTarget = _historyScroll.verticalNormalizedPosition;
            return;
        }

        float current = _historyScroll.verticalNormalizedPosition;
        float blend = 1f - Mathf.Exp(-HistoryScrollSharpness * Time.unscaledDeltaTime);
        float next = Mathf.Lerp(current, _historyScrollTarget, blend);
        _historyScroll.verticalNormalizedPosition = next;
        if (Mathf.Abs(next - _historyScrollTarget) <= 0.0005f)
        {
            _historyScroll.verticalNormalizedPosition = _historyScrollTarget;
            _historyScrollAnimating = false;
        }
    }

    private TMP_FontAsset? FindFont(string locale)
    {
        try
        {
            DialogueBubbleUI? bubble = Object.FindObjectOfType<DialogueBubbleUI>(true);
            TMP_FontAsset? gameFont = bubble?._dialogueText?.font;
            char required = ModLocalization.NormalizeLocale(locale) switch
            {
                "zh-CN" => '莉',
                "zh-TW" => '體',
                "ja" => 'リ',
                _ => 'A'
            };
            if (gameFont is not null && gameFont.HasCharacter(required, true, false))
            {
                return gameFont;
            }
            TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            return fonts.FirstOrDefault(item => item.HasCharacter(required, true, false))
                ?? gameFont
                ?? fonts.FirstOrDefault(item => item.name.Contains("UIFont", StringComparison.OrdinalIgnoreCase))
                ?? fonts.FirstOrDefault();
        }
        catch
        {
            return TMP_Settings.defaultFontAsset;
        }
    }

    private Sprite? LoadAvatar()
    {
        try
        {
            string path = Path.Combine(Paths.PluginPath, "assets", "lilith-avatar.png");
            if (!File.Exists(path))
            {
                LilithModPlugin.PluginLog.LogWarning($"Chat avatar is missing: {path}");
                return null;
            }
            _avatarTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(_avatarTexture, File.ReadAllBytes(path), false))
            {
                return null;
            }
            _avatarTexture.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(
                _avatarTexture,
                new Rect(0f, 0f, _avatarTexture.width, _avatarTexture.height),
                new Vector2(0.5f, 0.5f),
                100f);
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogWarning($"Chat avatar load failed: {error.GetType().Name}: {error.Message}");
            return null;
        }
    }

    private void EnsureEventSystem()
    {
        EventSystem? current = EventSystem.current;
        if (current is not null && current.isActiveAndEnabled)
        {
            if (current.currentInputModule is null)
            {
                current.gameObject.AddComponent<StandaloneInputModule>();
            }
            LilithModPlugin.PluginLog.LogInfo($"Using game EventSystem: {current.gameObject.name}");
            return;
        }
        _ownedEventSystem = new GameObject("LilithMod.EventSystem");
        Object.DontDestroyOnLoad(_ownedEventSystem);
        _ownedEventSystem.AddComponent<EventSystem>();
        _ownedEventSystem.AddComponent<StandaloneInputModule>();
        LilithModPlugin.PluginLog.LogInfo("Created fallback Unity EventSystem for chat input");
    }

    private Sprite? FindSprite(string name)
    {
        try
        {
            return Resources.FindObjectsOfTypeAll<Sprite>()
                .FirstOrDefault(item => string.Equals(item.name, name, StringComparison.Ordinal));
        }
        catch
        {
            return null;
        }
    }

    private Sprite CreateCheckerBackground()
    {
        _fallbackBackgroundTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        Color first = new(0.08f, 0.08f, 0.08f, 1f);
        Color second = new(0.13f, 0.13f, 0.13f, 1f);
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                _fallbackBackgroundTexture.SetPixel(x, y, ((x / 8) + (y / 8)) % 2 == 0 ? first : second);
            }
        }
        _fallbackBackgroundTexture.wrapMode = TextureWrapMode.Repeat;
        _fallbackBackgroundTexture.Apply();
        _fallbackBackground = Sprite.Create(
            _fallbackBackgroundTexture,
            new Rect(0f, 0f, 32f, 32f),
            new Vector2(0.5f, 0.5f),
            100f);
        return _fallbackBackground;
    }

    private void LoadPreferences()
    {
        try
        {
            if (!File.Exists(_preferencesPath) || _panel is null)
            {
                return;
            }
            ChatUiPreferences preferences = JsonSerializer.Deserialize<ChatUiPreferences>(File.ReadAllText(_preferencesPath))
                ?? new ChatUiPreferences();
            _preferencesWriteTimeUtc = File.GetLastWriteTimeUtc(_preferencesPath);
            _userScale = ChatUiScale.ClampUserScale(preferences.Scale);
            _panel.localScale = new Vector3(_userScale, _userScale, 1f);
            SetExpanded(_expanded);
            if (_hasSessionPosition)
            {
                ApplySessionPosition();
            }
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogWarning($"Chat UI preferences load failed: {error.GetType().Name}: {error.Message}");
        }
        ClampToCanvas();
    }

    private void CaptureSessionPosition()
    {
        if (_panel is null || _canvasRect is null)
        {
            return;
        }
        Vector2 available = AvailablePanelSpace();
        _sessionPositionX = available.x <= 0f
            ? 0.5f
            : Mathf.Clamp01((_panel.anchoredPosition.x - SafeMargin) / available.x);
        _sessionPositionY = available.y <= 0f
            ? 0.1f
            : Mathf.Clamp01((_panel.anchoredPosition.y - SafeMargin) / available.y);
    }

    private void ReloadPreferencesIfChanged()
    {
        try
        {
            if (!File.Exists(_preferencesPath))
            {
                return;
            }
            DateTime writeTime = File.GetLastWriteTimeUtc(_preferencesPath);
            if (writeTime != _preferencesWriteTimeUtc)
            {
                LoadPreferences();
            }
        }
        catch
        {
            // The next poll will retry after the settings process finishes writing.
        }
    }

    private void ApplyLocalization(bool force = false)
    {
        string locale = ModLocalization.CurrentLocale;
        if (!force && string.Equals(locale, _locale, StringComparison.Ordinal))
        {
            return;
        }
        _locale = locale;
        TMP_FontAsset? localizedFont = FindFont(locale);
        if (localizedFont is not null && localizedFont != _font)
        {
            _font = localizedFont;
            if (_root is not null)
            {
                foreach (TextMeshProUGUI label in _root.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    label.font = localizedFont;
                }
            }
        }
        if (_titleLabel is not null)
        {
            _titleLabel.text = ModLocalization.T("chat.title");
            _titleLabel.enableAutoSizing = locale == "en";
            _titleLabel.fontSizeMin = 14f;
            _titleLabel.fontSizeMax = 20f;
        }
        if (_settingsButtonLabel is not null)
        {
            _settingsButtonLabel.text = ModLocalization.T("chat.settings");
            _settingsButtonLabel.enableAutoSizing = locale == "en";
            _settingsButtonLabel.fontSizeMin = 12f;
            _settingsButtonLabel.fontSizeMax = 16f;
        }
        if (_placeholderLabel is not null)
        {
            _placeholderLabel.text = ModLocalization.T("chat.placeholder");
        }
        if (_sendButtonLabel is not null)
        {
            _sendButtonLabel.text = ModLocalization.T("chat.send");
        }
        if (_resetLabel is not null)
        {
            _resetLabel.text = ModLocalization.T("chat.reset_session");
        }
        if (_forgetLabel is not null)
        {
            _forgetLabel.text = _forgetArmed
                ? ModLocalization.T("chat.forget_confirm")
                : ModLocalization.T("chat.forget_player");
        }
        if (_historyButtonLabel is not null)
        {
            _historyButtonLabel.text = _expanded
                ? ModLocalization.T("chat.hide_history")
                : ModLocalization.T("chat.history");
        }
        MarkHistoryDirty();
    }

    private void ApplyOpenPosition()
    {
        if (_panel is null || _canvasRect is null)
        {
            return;
        }

        if (_hasSessionPosition)
        {
            ClampToCanvas();
            if (IsPanelOnVisibleMonitor())
            {
                CaptureSessionPosition();
                return;
            }
            _hasSessionPosition = false;
            LilithModPlugin.PluginLog.LogWarning(
                "Remembered chat position was outside every monitor; docking beside Lilith");
        }

        float canvasScale = _canvasScaler?.scaleFactor ?? 1f;
        bool characterAvailable = _characterScreenLocator.TryGetPlacementArea(out CharacterScreenPlacement placement)
            && canvasScale > 0f;
        if (characterAvailable)
        {
            DockOppositeCharacter(placement, canvasScale);
        }
        else
        {
            _panel.anchoredPosition = new Vector2(24f, 80f);
        }
        ClampToCanvas();
    }

    private void ApplySessionPosition()
    {
        if (_panel is null)
        {
            return;
        }
        Vector2 available = AvailablePanelSpace();
        _panel.anchoredPosition = new Vector2(
            _sessionPositionX * available.x + SafeMargin,
            _sessionPositionY * available.y + SafeMargin);
    }

    private void DockOppositeCharacter(CharacterScreenPlacement placement, float canvasScale)
    {
        if (_panel is null || _canvasRect is null)
        {
            return;
        }
        Vector2 position = _panel.anchoredPosition;
        float areaLeft = placement.Left / canvasScale;
        float areaRight = placement.Right / canvasScale;
        float areaBottom = placement.Bottom / canvasScale;
        float areaTop = placement.Top / canvasScale;
        position.x = ChatPanelPlacement.OppositeEdgeXInArea(
            placement.CharacterX / canvasScale,
            areaLeft,
            areaRight,
            _panel.rect.width,
            _userScale,
            SafeMargin);
        float highestBottom = Math.Max(
            areaBottom + SafeMargin,
            areaTop - _panel.rect.height * _userScale - SafeMargin);
        position.y = Mathf.Clamp(
            areaBottom + 80f,
            areaBottom + SafeMargin,
            highestBottom);
        _panel.anchoredPosition = position;
    }

    private bool IsPanelOnVisibleMonitor()
    {
        if (_panel is null)
        {
            return false;
        }
        Vector3 worldCenter = _panel.TransformPoint(_panel.rect.center);
        Vector2 screenCenter = RectTransformUtility.WorldToScreenPoint(null, worldCenter);
        return _characterScreenLocator.IsScreenPointVisible(screenCenter.x, screenCenter.y);
    }

    private void UpdateCanvasScale()
    {
        if (_canvasScaler is null)
        {
            return;
        }
        _lastScreenWidth = Screen.width;
        _lastScreenHeight = Screen.height;
        float automaticScale = ChatUiScale.Calculate(Screen.height);
        _canvasScaler.scaleFactor = automaticScale;
    }

    private bool ScreenPointInCanvas(Vector2 screenPoint, out Vector2 localPoint)
    {
        localPoint = Vector2.zero;
        return _canvasRect is not null
            && RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPoint, null, out localPoint);
    }

    private Vector2 AvailablePanelSpace()
    {
        if (_canvasRect is null || _panel is null)
        {
            return Vector2.zero;
        }
        float width = Mathf.Max(0f, _canvasRect.rect.width - _panel.rect.width * _userScale - SafeMargin * 2f);
        float height = Mathf.Max(0f, _canvasRect.rect.height - _panel.rect.height * _userScale - SafeMargin * 2f);
        return new Vector2(width, height);
    }

    private void ClampToCanvas()
    {
        if (_panel is null || _canvasRect is null)
        {
            return;
        }
        Vector2 available = AvailablePanelSpace();
        Vector2 position = _panel.anchoredPosition;
        position.x = Mathf.Clamp(position.x, SafeMargin, SafeMargin + available.x);
        position.y = Mathf.Clamp(position.y, SafeMargin, SafeMargin + available.y);
        _panel.anchoredPosition = position;
    }

    private static GameObject CreateObject(string name, Transform? parent)
    {
        var components = new Il2CppReferenceArray<Il2CppSystem.Type>(1);
        components[0] = Il2CppType.Of<RectTransform>();
        var result = new GameObject(name, components);
        if (parent is not null)
        {
            result.transform.SetParent(parent, false);
        }
        return result;
    }

    private TextMeshProUGUI AddText(
        string name,
        Transform parent,
        string text,
        float size,
        Color color,
        FontStyles style = FontStyles.Normal)
    {
        GameObject gameObject = CreateObject(name, parent);
        TextMeshProUGUI result = gameObject.AddComponent<TextMeshProUGUI>();
        result.font = _font;
        result.text = text;
        result.fontSize = size;
        result.fontStyle = style;
        result.color = color;
        result.enableWordWrapping = false;
        result.overflowMode = TextOverflowModes.Overflow;
        result.raycastTarget = false;
        return result;
    }

    private static RectTransform AddImage(string name, Transform parent, Color color, Sprite? sprite = null)
    {
        GameObject gameObject = CreateObject(name, parent);
        Image image = gameObject.AddComponent<Image>();
        image.color = color;
        image.sprite = sprite;
        image.preserveAspect = sprite is not null;
        return gameObject.GetComponent<RectTransform>();
    }

    private Button AddButton(
        string name,
        Transform parent,
        string label,
        float x,
        float y,
        float width,
        float height,
        Action callback,
        bool anchorRight = false,
        bool anchorTop = true)
    {
        GameObject gameObject = CreateObject(name, parent);
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        SetAnchored(rect, x, y, width, height, anchorRight, anchorTop);
        Image image = gameObject.AddComponent<Image>();
        image.color = Dark;
        Button button = gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Dark;
        colors.highlightedColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        colors.pressedColor = Red;
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.08f, 0.08f, 0.08f, 0.6f);
        colors.colorMultiplier = 1f;
        button.colors = colors;
        button.onClick.AddListener((UnityAction)callback);
        AddHardBorder(gameObject.transform, 1f, new Color(0.7f, 0.7f, 0.68f, 1f));

        TextMeshProUGUI text = AddText("Label", gameObject.transform, label, 16f, White);
        text.alignment = TextAlignmentOptions.Center;
        Stretch(text.rectTransform, 2f, 2f, 2f, 2f);
        return button;
    }

    private static void AddHardBorder(Transform parent, float thickness = 2f, Color? color = null)
    {
        Color edge = color ?? White;
        RectTransform top = AddImage("BorderTop", parent, edge);
        SetTopStretch(top, 0f, 0f, 0f, thickness);
        RectTransform bottom = AddImage("BorderBottom", parent, edge);
        SetBottomStretch(bottom, 0f, 0f, 0f, thickness);
        RectTransform left = AddImage("BorderLeft", parent, edge);
        SetAnchored(left, 0f, 0f, thickness, 0f, false, true, true);
        RectTransform right = AddImage("BorderRight", parent, edge);
        SetAnchored(right, 0f, 0f, thickness, 0f, true, true, true);
    }

    private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void SetTopStretch(
        RectTransform rect,
        float left,
        float top,
        float right,
        float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void SetBottomStretch(
        RectTransform rect,
        float left,
        float bottom,
        float right,
        float height)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, bottom + height);
    }

    private static void SetAnchored(
        RectTransform rect,
        float x,
        float y,
        float width,
        float height,
        bool anchorRight,
        bool anchorTop,
        bool stretchVertical = false)
    {
        float ax = anchorRight ? 1f : 0f;
        float ay = anchorTop ? 1f : 0f;
        rect.anchorMin = new Vector2(ax, stretchVertical ? 0f : ay);
        rect.anchorMax = new Vector2(ax, stretchVertical ? 1f : ay);
        rect.pivot = new Vector2(anchorRight ? 1f : 0f, anchorTop ? 1f : 0f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, stretchVertical ? 0f : height);
    }

    private sealed class HistoryRowView
    {
        public HistoryRowView(
            GameObject root,
            RectTransform rect,
            TextMeshProUGUI body,
            Image portrait,
            TextMeshProUGUI speaker)
        {
            Root = root;
            Rect = rect;
            Body = body;
            Portrait = portrait;
            Speaker = speaker;
        }

        public GameObject Root { get; }

        public RectTransform Rect { get; }

        public TextMeshProUGUI Body { get; }

        public Image Portrait { get; }

        public TextMeshProUGUI Speaker { get; }
    }

    private sealed class ChatUiPreferences
    {
        public bool HasPosition { get; set; }

        public float X { get; set; } = 0.5f;

        public float Y { get; set; } = 0.08f;

        public float Scale { get; set; } = 1f;
    }
}
