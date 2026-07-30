using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace LilithMod;

public sealed class LilithRuntimeBehaviour : MonoBehaviour
{
    private const string SessionId = "desktop-pet";
    private readonly CompanionClient _client = new();
    private readonly ConcurrentQueue<Action> _mainThread = new();
    private readonly MediaPollCoordinator _mediaPollCoordinator = new();
    private readonly MemoryDiagnostics _memoryDiagnostics = new();
    private readonly PlayerNameBridge _playerNameBridge = new();
    private CancellationTokenSource _lifetime = new();
    private string _input = "";
    private string _lastReply = "";
    private string _status = "";
    private bool _requestPending;
    private bool _memoryResetPending;
    private bool _visible;
    private bool _chatWindowInitialized;
    private bool _chatHistoryLoaded;
    private bool _chatHistoryLoading;
    private float _nextChatHistoryLoad;
    private float _nextCompanionHealthCheck;
    private bool _playerNameLogged;
    private float _nextMusicBehaviorSync;
    private object? _forcedMusicScheduler;
    private ILilithBehavior? _forcedMusicBehavior;
    private LilithBehaviorContext? _forcedMusicContext;
    private bool _musicBehaviorSyncLogged;
    private bool _musicSwayLogged;
    private bool _musicAnimationStarted;
    private bool _musicExitPending;
    private float _musicExitDeadline;
    private float _nextMediaPoll;
    private float _nextTrayIntegration;
    private float _replyUntil;
    private GUIStyle? _replyStyle;
    private GUIStyle? _statusStyle;
    private Rect _panel = new(24, 24, 520, 300);
    private readonly NativeChatWindow _chatWindow;

    [HideFromIl2Cpp]
    private RuntimeChatState ChatState => RuntimeChatState.Shared;

    public LilithRuntimeBehaviour(IntPtr pointer) : base(pointer)
    {
        _chatWindow = new NativeChatWindow(
            message => _mainThread.Enqueue(() => SubmitText(message)),
            () => _mainThread.Enqueue(OpenApiConfiguration),
            () => _mainThread.Enqueue(ResetSession),
            () => _mainThread.Enqueue(ForgetPlayer));
        TrayIntegration.Bind(this);
    }

    public void Update()
    {
        EnsureChatWindowInitialized();
        ChatState.View?.Tick();
        UserInterfacePatches.PollCharacterRightClick();
        UserInterfacePatches.PollAutonomousDialogueClick();

        if (Time.unscaledTime >= _nextCompanionHealthCheck)
        {
            _nextCompanionHealthCheck = Time.unscaledTime + 3f;
            LilithModPlugin.EnsureCompanionRunning();
        }

        if (!_chatHistoryLoaded && !_chatHistoryLoading && Time.unscaledTime >= _nextChatHistoryLoad)
        {
            RequestChatHistory();
        }

        if (!_playerNameLogged)
        {
            _playerNameLogged = true;
            LilithModPlugin.PluginLog.LogInfo($"Player name resolved: {PlayerName()}");
        }

        bool windowsF8 = UserInterfacePatches.PollChatHotkey();
        if (Input.GetKeyDown(KeyCode.F8) || windowsF8)
        {
            _visible = !_visible;
            if (_visible)
            {
                ShowChat(ChatOpenReason.Command);
            }
            else
            {
                HideChat();
            }
        }

        if (Time.unscaledTime >= _nextTrayIntegration)
        {
            _nextTrayIntegration = Time.unscaledTime + 1f;
            TrayIntegration.TryInitialize();
        }

        while (_mainThread.TryDequeue(out Action? action))
        {
            action();
        }
        _playerNameBridge.Tick();
        CrossScreenDragFeature.Tick();
        _memoryDiagnostics.Tick(_mediaPollCoordinator);

        if (_musicExitPending && Time.unscaledTime >= _musicExitDeadline)
        {
            CompleteMusicExit();
        }

        if ((_visible || ExternalMusicState.Armed) && Time.unscaledTime >= _nextMediaPoll)
        {
            BeginMediaPoll();
        }

        if (ExternalMusicState.Armed && Time.unscaledTime >= _nextMusicBehaviorSync)
        {
            _nextMusicBehaviorSync = Time.unscaledTime + 0.5f;
            SyncMusicBehavior();
        }
    }

    public void OnGUI()
    {
        EnsureStyles();
        if (!string.IsNullOrWhiteSpace(_lastReply) && Time.unscaledTime <= _replyUntil)
        {
            GUI.Label(FallbackReplyRect(), _lastReply, _replyStyle);
        }
    }

    public void LateUpdate()
    {
        if (ExternalMusicState.Playing
            && MusicBehaviorEligibility.IsAllowed()
            && _forcedMusicBehavior is not null
            && _forcedMusicContext is not null)
        {
            MaintainOriginalMusicSway(_forcedMusicBehavior);
        }
    }

    public void OnDestroy()
    {
        _mediaPollCoordinator.Cancel();
        _lifetime.Cancel();
        LilithModPlugin.ShutdownCompanion();
        ReleaseForcedMusicBehavior();
        if (_musicExitPending)
        {
            _musicExitPending = false;
            CancelOriginalIdleAnimation();
        }
        TrayIntegration.Unbind(this);
        _playerNameBridge.Dispose();
        CrossScreenDragFeature.Shutdown();
        _lifetime.Dispose();
        ChatState.Reset();
        _chatWindow.Dispose();
    }

    public void OnApplicationQuit()
    {
        _mediaPollCoordinator.Cancel();
        _lifetime.Cancel();
        _playerNameBridge.Dispose();
        CrossScreenDragFeature.Shutdown();
        LilithModPlugin.ShutdownCompanion();
    }

    private void Submit()
    {
        SubmitText(_input);
    }

    private void EnsureChatWindowInitialized()
    {
        if (_chatWindowInitialized)
        {
            return;
        }
        _chatWindowInitialized = true;
        Application.runInBackground = true;
        _visible = false;
        ExternalMusicState.SetArmed(true);

        string legacySentinel = Path.Combine(Paths.PluginPath, "LilithMod.legacy-chat");
        if (!File.Exists(legacySentinel))
        {
            try
            {
                ChatState.View = new UnityChatView(
                    message =>
                    {
                        AppendChatMessage(ChatSpeaker.User, message);
                        SubmitText(message);
                    },
                    OpenApiConfiguration,
                    ResetSession,
                    ForgetPlayer);
                ChatState.View.SetMessages(ChatState.Model.Messages);
                LilithModPlugin.PluginLog.LogInfo("Unity chat view initialized");
            }
            catch (Exception error)
            {
                ChatState.View = null;
                LilithModPlugin.PluginLog.LogError(
                    $"Unity chat view initialization failed; using native fallback: {error}");
            }
        }
        else
        {
            LilithModPlugin.PluginLog.LogWarning("Legacy chat sentinel present; using native chat window");
        }

        LilithModPlugin.PluginLog.LogInfo("Unity background execution enabled");
        LilithModPlugin.PluginLog.LogInfo("External music auto-monitor armed");
    }

    [HideFromIl2Cpp]
    private void ShowChat(ChatOpenReason reason)
    {
        EnsureChatWindowInitialized();
        _visible = true;
        if (ChatState.View is not null)
        {
            ChatState.View.Show(reason);
            return;
        }
        _chatWindow.Show();
    }

    private void HideChat()
    {
        _visible = false;
        if (ChatState.View is not null)
        {
            ChatState.View.Hide();
            return;
        }
        _chatWindow.Hide();
    }

    private void RequestChatHistory()
    {
        _chatHistoryLoading = true;
        CompanionTasks.LoadHistory(
            _client,
            SessionId,
            _lifetime.Token,
            historyJson => _mainThread.Enqueue(() =>
            {
                _chatHistoryLoading = false;
                _chatHistoryLoaded = true;
                IReadOnlyList<CompanionHistoryMessage> history = JsonSerializer
                    .Deserialize<List<CompanionHistoryMessage>>(historyJson)
                    ?? new List<CompanionHistoryMessage>();
                ChatState.Model.MergeHistory(history);
                ChatState.View?.SetMessages(ChatState.Model.Messages);
                LilithModPlugin.PluginLog.LogInfo($"Chat history loaded: {history.Count} messages");
            }),
            error => _mainThread.Enqueue(() =>
            {
                _chatHistoryLoading = false;
                _nextChatHistoryLoad = Time.unscaledTime + 2f;
                LilithModPlugin.PluginLog.LogDebug($"Chat history is not ready yet: {error}");
            }));
    }

    [HideFromIl2Cpp]
    private void AppendChatMessage(ChatSpeaker speaker, string text)
    {
        ChatState.Model.Add(speaker, text);
        var message = new ChatMessage(speaker, text.Trim());
        if (ChatState.View is not null)
        {
            ChatState.View.AddMessage(message);
            return;
        }
        _chatWindow.AppendMessage(SpeakerName(speaker), message.Text);
    }

    private void ClearChatMessages()
    {
        ChatState.Model.Clear();
        if (ChatState.View is not null)
        {
            ChatState.View.ClearMessages();
            return;
        }
        _chatWindow.ClearHistory();
    }

    private void SetChatBusy(bool busy, string status = "")
    {
        ChatState.Model.SetBusy(busy, status);
        ChatState.View?.SetBusy(busy, status);
    }

    [HideFromIl2Cpp]
    private static string SpeakerName(ChatSpeaker speaker)
    {
        return speaker switch
        {
            ChatSpeaker.Lilith => ModLocalization.T("common.lilith"),
            ChatSpeaker.User => ModLocalization.T("common.you"),
            _ => ModLocalization.T("common.system")
        };
    }

    internal void ShowChatWindow()
    {
        ShowChat(ChatOpenReason.Command);
    }

    internal bool IsChatPointerOver() => ChatState.View?.PointerOverPanel == true;

    internal void BeginChatFromLilithLine(string line)
    {
        line = line.Trim();
        if (line.Length == 0)
        {
            ShowChatWindow();
            return;
        }

        bool addedLocally = ChatState.Model.AddLilithIfNotLast(line);
        LilithModPlugin.PluginLog.LogInfo(
            $"Dialogue bubble inheritance: addedLocally={addedLocally}; chars={line.Length}");
        if (addedLocally)
        {
            ChatState.View?.AddMessage(new ChatMessage(ChatSpeaker.Lilith, line));
            if (ChatState.View is null)
            {
            _chatWindow.AppendMessage(ModLocalization.T("common.lilith"), line);
            }
        }
        ShowChat(ChatOpenReason.LilithBubble);
        ChatState.SeedQueue.Start();
            _status = ModLocalization.T("chat.status.seeded");
            SetChatBusy(true, ModLocalization.T("chat.status.seed_connecting"));
        CompanionTasks.SeedAssistant(
            _client,
            SessionId,
            line,
            _lifetime.Token,
            () => _mainThread.Enqueue(() => CompleteConversationSeed(null)),
            error => _mainThread.Enqueue(() => CompleteConversationSeed(error)));
    }

    private void CompleteConversationSeed(string? error)
    {
        string? pending = ChatState.SeedQueue.Complete();
        SetChatBusy(_requestPending, _requestPending ? ModLocalization.T("chat.status.thinking") : string.Empty);
        if (!string.IsNullOrWhiteSpace(error))
        {
            PluginLogWarning(error);
        }
        else
        {
            // Reconcile with the persisted session after every autonomous
            // opener. This covers rapid consecutive bubble clicks where the
            // local view and the async seed requests can complete out of order.
            RequestChatHistory();
        }
        if (!string.IsNullOrWhiteSpace(pending))
        {
            SubmitText(pending);
        }
    }

    private void OpenApiConfiguration()
    {
        TrayIntegration.OpenApiConfiguration();
    }

    private void ResetSession()
    {
        if (_requestPending || _memoryResetPending)
        {
            return;
        }
        _memoryResetPending = true;
        CompanionTasks.ResetSession(
            _client,
            SessionId,
            _lifetime.Token,
            () => _mainThread.Enqueue(() =>
            {
                ClearChatMessages();
                AppendChatMessage(ChatSpeaker.System, ModLocalization.T("chat.session_cleared"));
                _lastReply = ModLocalization.T("chat.session_clear_short");
                _replyUntil = Time.unscaledTime + 4f;
                _memoryResetPending = false;
            }),
            error => _mainThread.Enqueue(() =>
            {
                PluginLogWarning(error);
                AppendChatMessage(ChatSpeaker.System, ModLocalization.T("chat.session_clear_failed"));
                _memoryResetPending = false;
            }));
    }

    private void ForgetPlayer()
    {
        if (_requestPending || _memoryResetPending)
        {
            return;
        }
        _memoryResetPending = true;
        CompanionTasks.ForgetPlayer(
            _client,
            PlayerName(),
            _lifetime.Token,
            () => _mainThread.Enqueue(() =>
            {
                AppendChatMessage(ChatSpeaker.System, ModLocalization.T("chat.memory_cleared"));
                _lastReply = ModLocalization.T("chat.memory_clear_short");
                _replyUntil = Time.unscaledTime + 4f;
                _memoryResetPending = false;
            }),
            error => _mainThread.Enqueue(() =>
            {
                PluginLogWarning(error);
                AppendChatMessage(ChatSpeaker.System, ModLocalization.T("chat.memory_clear_failed"));
                _memoryResetPending = false;
            }));
    }

    internal void NotifyUser(string text)
    {
        _mainThread.Enqueue(() =>
        {
            _lastReply = text;
            _replyUntil = Time.unscaledTime + 8f;
            AppendChatMessage(ChatSpeaker.System, text);
        });
    }

    [HideFromIl2Cpp]
    private void BeginMediaPoll()
    {
        if (!_mediaPollCoordinator.TryBegin())
        {
            return;
        }

        _nextMediaPoll = float.PositiveInfinity;
        Task<ExternalMediaState> poll = CompanionTasks.PollMedia(_client, _lifetime.Token);
        poll.ContinueWith(
            completed => _mainThread.Enqueue(() => CompleteMediaPoll(completed)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    [HideFromIl2Cpp]
    private void CompleteMediaPoll(Task<ExternalMediaState> poll)
    {
        bool shouldApply = _mediaPollCoordinator.Complete();
        _nextMediaPoll = Time.unscaledTime + 0.5f;
        if (!shouldApply)
        {
            return;
        }

        if (poll.Status == TaskStatus.RanToCompletion)
        {
            ApplyMediaState(poll.Result);
            return;
        }

        if (poll.IsFaulted)
        {
            _ = poll.Exception;
        }
        ExternalMusicState.Update(ExternalMediaState.Unavailable);
    }

    [HideFromIl2Cpp]
    private void ApplyMediaState(ExternalMediaState state)
    {
        bool before = ExternalMusicState.Playing;
        ExternalMusicState.Update(state);
        bool after = ExternalMusicState.Playing;
        if (before != after)
        {
            LilithModPlugin.PluginLog.LogInfo(
                $"External music state changed: playing={after}; source={state.Source}; available={state.Available}");
        }
    }

    private void SyncMusicBehavior()
    {
        if (_musicExitPending)
        {
            return;
        }
        if (!ExternalMusicState.Playing || !MusicBehaviorEligibility.IsAllowed())
        {
            ReleaseForcedMusicBehavior();
            return;
        }

        try
        {
            CharacterController? controller = CharacterController.s_activeInstance;
            object? tree = controller?.BehaviorTreeComp;
            if (tree is null)
            {
                LogMusicSyncOnce("Music sync: BehaviorTreeComp is null");
                return;
            }
            object? source = tree?.GetType().GetMethod("GetBehaviorSource")?.Invoke(tree, null);
            if (source is null)
            {
                LogMusicSyncOnce("Music sync: BehaviorSource is null");
                return;
            }
            object? scheduler = FindScheduler(source);
            if (scheduler is null)
            {
                LogMusicSyncOnce("Music sync: EntryTask/RootTask could not cast to LilithIdleSchedulerRootTask");
                return;
            }

            ILilithBehavior? music = FindMusicCandidate(scheduler);
            if (music is null)
            {
                LogMusicSyncOnce("Music sync: LilithIdleScheduler has no MusicListeningBehavior candidate");
                return;
            }

            PropertyInfo? activeProperty = scheduler.GetType().GetProperty(
                "_active", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object? active = scheduler.GetType().GetProperty("ActiveBehavior")?.GetValue(scheduler);
            if (ReferenceEquals(active, music) || Equals(active, music))
            {
                _forcedMusicScheduler = scheduler;
                _forcedMusicBehavior = music;
                ExternalMusicState.MarkBehaviorConsumed(true);
                return;
            }

            if (active is not null)
            {
                // Do not interrupt a different idle action. The next eligible
                // idle scan will pick music after that action finishes.
                return;
            }

            if (activeProperty is null || !activeProperty.CanWrite)
            {
                throw new InvalidOperationException("LilithIdleScheduler._active setter is unavailable");
            }

            activeProperty.SetValue(scheduler, music);
            LilithBehaviorContext? context = scheduler.GetType().GetProperty(
                "_ctx", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(scheduler)
                as LilithBehaviorContext;
            MethodInfo? begin = music.GetType().GetMethod("Begin");
            if (context is null || begin is null)
            {
                activeProperty.SetValue(scheduler, null);
                throw new InvalidOperationException("MusicListeningBehavior.Begin context is unavailable");
            }
            begin.Invoke(music, new[] { context });
            ApplyOriginalMusicSway(music, context, true);
            _forcedMusicScheduler = scheduler;
            _forcedMusicBehavior = music;
            _forcedMusicContext = context;
            ExternalMusicState.MarkBehaviorConsumed(true);
            if (!_musicBehaviorSyncLogged)
            {
                _musicBehaviorSyncLogged = true;
                LilithModPlugin.PluginLog.LogInfo(
                    "Music sync: MusicListeningBehavior selected as the active idle behavior");
            }
        }
        catch (Exception error)
        {
            LogMusicSyncOnce($"Music sync failed: {error.GetType().Name}: {error.Message}");
        }
    }

    private void LogMusicSyncOnce(string message)
    {
        if (_musicBehaviorSyncLogged)
        {
            return;
        }
        _musicBehaviorSyncLogged = true;
        LilithModPlugin.PluginLog.LogWarning(message);
    }

    private static object? FindScheduler(object source)
    {
        Type sourceType = source.GetType();
        foreach (string propertyName in new[] { "EntryTask", "RootTask" })
        {
            object? task = sourceType.GetProperty(propertyName)?.GetValue(source);
            if (task is not Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase taskObject)
            {
                continue;
            }

            object? scheduler = FindSchedulerInTask(taskObject, propertyName, 0);
            if (scheduler is not null)
            {
                return scheduler;
            }
        }
        return null;
    }

    private static object? FindSchedulerInTask(
        Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase taskObject,
        string path,
        int depth)
    {
        if (depth > 12)
        {
            return null;
        }

        LilithIdleSchedulerRootTask? typedRoot = taskObject.TryCast<LilithIdleSchedulerRootTask>();
        if (typedRoot is not null)
        {
            return typeof(LilithIdleSchedulerRootTask).GetProperty(
                "_scheduler", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(typedRoot);
        }

        string nativeName = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(taskObject.ObjectClass)
            ?? string.Empty;
        if (string.Equals(nativeName, nameof(LilithIdleSchedulerRootTask), StringComparison.Ordinal))
        {
            LilithIdleSchedulerRootTask wrappedRoot = new(taskObject.Pointer);
            return typeof(LilithIdleSchedulerRootTask).GetProperty(
                "_scheduler", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(wrappedRoot);
        }

        BehaviorDesigner.Runtime.Tasks.ParentTask? parent =
            taskObject.TryCast<BehaviorDesigner.Runtime.Tasks.ParentTask>();
        object? children = parent?.Children;
        if (children is null)
        {
            return null;
        }

        Type listType = children.GetType();
        int count = listType.GetProperty("Count")?.GetValue(children) is int value ? value : 0;
        PropertyInfo? item = listType.GetProperty("Item");
        for (int index = 0; index < count && item is not null; index++)
        {
            object? child = item.GetValue(children, new object[] { index });
            if (child is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase childObject)
            {
                object? scheduler = FindSchedulerInTask(childObject, $"{path}.Children[{index}]", depth + 1);
                if (scheduler is not null)
                {
                    return scheduler;
                }
            }
        }

        return null;
    }

    private static ILilithBehavior? FindMusicCandidate(object scheduler)
    {
        Type schedulerType = scheduler.GetType();
        object? candidates = schedulerType.GetProperty(
            "_candidates", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(scheduler)
            ?? schedulerType.GetProperty("Candidates")?.GetValue(scheduler);
        if (candidates is null)
        {
            return null;
        }

        Type listType = candidates.GetType();
        object? countValue = listType.GetProperty("Count")?.GetValue(candidates);
        int count = countValue is int value ? value : 0;
        PropertyInfo? item = listType.GetProperty("Item");
        for (int index = 0; index < count && item is not null; index++)
        {
            object? candidate = item.GetValue(candidates, new object[] { index });
            if (candidate is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase candidateObject)
            {
                MusicListeningBehavior? music = candidateObject.TryCast<MusicListeningBehavior>();
                if (music is not null)
                {
                    // Keep the interface wrapper returned by List<ILilithBehavior>.
                    // Reflection will not coerce the derived IL2CPP wrapper back
                    // to ILilithBehavior when assigning LilithIdleScheduler._active.
                    return candidateObject.TryCast<ILilithBehavior>();
                }
            }
            else if (candidate is MusicListeningBehavior)
            {
                return candidate as ILilithBehavior;
            }
        }
        return null;
    }

    private void ReleaseForcedMusicBehavior()
    {
        if (_musicExitPending)
        {
            return;
        }
        if (_forcedMusicScheduler is null || _forcedMusicBehavior is null)
        {
            ExternalMusicState.MarkBehaviorConsumed(false);
            return;
        }

        bool exitStarted = false;
        try
        {
            bool ownedMusicAnimation = _musicAnimationStarted;
            bool canPlayExit = ownedMusicAnimation
                && !ExternalMusicState.Playing
                && MusicBehaviorEligibility.IsAllowed();
            LilithBehaviorContext? context = _forcedMusicContext
                ?? _forcedMusicScheduler.GetType().GetProperty(
                    "_ctx", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(_forcedMusicScheduler) as LilithBehaviorContext;
            if (context is not null)
            {
                ApplyOriginalMusicSway(_forcedMusicBehavior, context, false);
            }
            object? active = _forcedMusicScheduler.GetType().GetProperty("ActiveBehavior")
                ?.GetValue(_forcedMusicScheduler);
            bool musicIsActive = ReferenceEquals(active, _forcedMusicBehavior) || Equals(active, _forcedMusicBehavior);
            if (musicIsActive)
            {
                if (context is not null)
                {
                    _forcedMusicBehavior.GetType().GetMethod("End")
                        ?.Invoke(_forcedMusicBehavior, new[] { context });
                }
                PropertyInfo? activeProperty = _forcedMusicScheduler.GetType().GetProperty(
                    "_active", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                activeProperty?.SetValue(_forcedMusicScheduler, null);
            }
            if (canPlayExit)
            {
                exitStarted = TryBeginMusicExit();
            }
            if (!exitStarted && (musicIsActive || ownedMusicAnimation))
            {
                CancelOriginalIdleAnimation();
            }
            LilithModPlugin.PluginLog.LogInfo("Music sync: original music action lifecycle released");
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogWarning(
                $"Music sync release failed: {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            _forcedMusicScheduler = null;
            _forcedMusicBehavior = null;
            _forcedMusicContext = null;
            _musicBehaviorSyncLogged = false;
            _musicSwayLogged = false;
            if (!exitStarted)
            {
                _musicAnimationStarted = false;
            }
            ExternalMusicState.MarkBehaviorConsumed(false);
        }
    }

    private bool TryBeginMusicExit()
    {
        CharacterController? controller = CharacterController.s_activeInstance;
        object? entry = controller is null ? null : GetCurrentTrackEntry(controller, 0);
        string animationName = GetTrackAnimationName(entry);
        if (entry is null || !string.Equals(animationName, "stand_casual_music", StringComparison.Ordinal))
        {
            return false;
        }

        const float loopStart = 0.4666667f;
        const float loopEnd = 4.4666667f;
        const float fullDuration = 5.466667f;
        float animationTime = GetTrackFloat(entry, "AnimationTime", loopStart);
        animationTime = Math.Clamp(animationTime, loopStart, loopEnd);
        SetTrackValue(entry, "Loop", false);
        SetTrackValue(entry, "AnimationStart", loopStart);
        SetTrackValue(entry, "AnimationEnd", fullDuration);
        SetTrackValue(entry, "TrackTime", animationTime - loopStart);
        _musicExitPending = true;
        _musicExitDeadline = Time.unscaledTime + Math.Max(0.1f, fullDuration - animationTime) + 0.35f;
        LilithModPlugin.PluginLog.LogInfo(
            $"Music sync: casual exit armed at animationTime={animationTime:0.000}");
        return true;
    }

    private void CompleteMusicExit()
    {
        if (!_musicExitPending)
        {
            return;
        }
        _musicExitPending = false;
        _musicAnimationStarted = false;
        CancelOriginalIdleAnimation();
        LilithModPlugin.PluginLog.LogInfo("Music sync: casual exit completed");
    }

    private static void CancelOriginalIdleAnimation()
    {
        CharacterController? controller = CharacterController.s_activeInstance;
        if (controller is null)
        {
            return;
        }

        try
        {
            object? stateManager = typeof(CharacterController).GetProperty(
                "_lilithStateManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(controller);
            MethodInfo? returnIdle = stateManager?.GetType().GetMethod(
                "ReturnIdle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (stateManager is null || returnIdle is null || returnIdle.GetParameters().Length != 0)
            {
                LilithModPlugin.PluginLog.LogWarning(
                    "Music sync: LilithStateManager.ReturnIdle is unavailable");
                return;
            }
            returnIdle.Invoke(stateManager, null);
            LilithModPlugin.PluginLog.LogInfo("Music sync: LilithStateManager.ReturnIdle invoked");
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogWarning(
                $"Music sync cancel idle failed: {error.GetType().Name}: {error.Message}");
        }
    }

    private void ApplyOriginalMusicSway(
        ILilithBehavior behavior,
        LilithBehaviorContext context,
        bool enabled)
    {
        CharacterController? controller = CharacterController.s_activeInstance;
        if (controller is null)
        {
            throw new InvalidOperationException("CharacterController is unavailable for music animation");
        }

        if (behavior is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase behaviorObject)
        {
            MusicListeningBehavior? concrete = behaviorObject.TryCast<MusicListeningBehavior>();
            if (concrete is not null)
            {
                typeof(MusicListeningBehavior).GetProperty(
                    "_holdSway", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.SetValue(concrete, enabled);
            }
        }

        MethodInfo? setEmotion = typeof(MusicListeningBehavior).GetMethod(
            "SetEmotion", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (setEmotion is null)
        {
            throw new InvalidOperationException("MusicListeningBehavior.SetEmotion is unavailable");
        }
        setEmotion.Invoke(null, new object?[] { context, enabled });

        if (enabled && !_musicAnimationStarted)
        {
            _musicExitPending = false;
            string animationName = ResolveMusicAnimationName(controller);
            controller.PlayAnimation(animationName, true, 0);
            ConfigureMusicLoop(controller);
            _musicAnimationStarted = true;
        }
        if (enabled && !_musicSwayLogged)
        {
            _musicSwayLogged = true;
            LilithModPlugin.PluginLog.LogInfo("Music sync: original music action lifecycle started");
        }
    }

    private static string ResolveMusicAnimationName(CharacterController controller)
    {
        string current = GetTrackAnimationName(GetCurrentTrackEntry(controller, 0));
        if (current.Contains("pajamas", StringComparison.OrdinalIgnoreCase))
        {
            return "stand_pajamas_music";
        }
        if (current.Contains("casual", StringComparison.OrdinalIgnoreCase))
        {
            return "stand_casual_music";
        }

        object? stateManager = typeof(CharacterController).GetProperty(
            "_lilithStateManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(controller);
        string clothing = stateManager?.GetType().GetProperty(
            "ClothingState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(stateManager)?.ToString() ?? "";
        return clothing.Contains("Pajamas", StringComparison.OrdinalIgnoreCase)
            ? "stand_pajamas_music"
            : "stand_casual_music";
    }

    private static void MaintainOriginalMusicSway(ILilithBehavior behavior)
    {
        if (behavior is not Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase behaviorObject)
        {
            return;
        }

        MusicListeningBehavior? concrete = behaviorObject.TryCast<MusicListeningBehavior>();
        if (concrete is not null)
        {
            typeof(MusicListeningBehavior).GetProperty(
                "_holdSway", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.SetValue(concrete, true);
        }
    }

    private static void ConfigureMusicLoop(CharacterController controller)
    {
        try
        {
            object? entry = GetCurrentTrackEntry(controller, 0);
            string name = GetTrackAnimationName(entry);
            float loopStart;
            float loopEnd;
            if (string.Equals(name, "stand_casual_music", StringComparison.Ordinal))
            {
                loopStart = 0.4666667f;
                loopEnd = 4.4666667f;
            }
            else if (string.Equals(name, "stand_pajamas_music", StringComparison.Ordinal))
            {
                loopStart = 0f;
                loopEnd = 4f;
            }
            else
            {
                return;
            }

            SetTrackValue(entry!, "AnimationStart", loopStart);
            SetTrackValue(entry!, "AnimationEnd", loopEnd);
            SetTrackValue(entry!, "TrackTime", 0f);
            SetTrackValue(entry!, "Loop", true);
            LilithModPlugin.PluginLog.LogInfo(
                $"Music sync: {name} loop configured at {loopStart:0.000}-{loopEnd:0.000}s");
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogWarning(
                $"Music sync loop configuration failed: {error.GetType().Name}: {error.Message}");
        }
    }

    private static object? GetCurrentTrackEntry(CharacterController controller, int trackIndex)
    {
        object? skeleton = typeof(CharacterController).GetProperty(
            "SkeletonAnimationInternal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(controller);
        object? animationState = skeleton?.GetType().GetProperty(
            "AnimationState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(skeleton);
        MethodInfo? getCurrent = animationState?.GetType().GetMethod(
            "GetCurrent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(int) }, null);
        return animationState is null || getCurrent is null
            ? null
            : getCurrent.Invoke(animationState, new object[] { trackIndex });
    }

    private static string GetTrackAnimationName(object? entry)
    {
        object? animation = entry?.GetType().GetProperty("Animation")?.GetValue(entry);
        return animation?.GetType().GetProperty("Name")?.GetValue(animation)?.ToString() ?? "";
    }

    private static float GetTrackFloat(object entry, string propertyName, float fallback)
    {
        try
        {
            object? value = entry.GetType().GetProperty(propertyName)?.GetValue(entry);
            return value is null ? fallback : Convert.ToSingle(value);
        }
        catch
        {
            return fallback;
        }
    }

    private static void SetTrackValue(object entry, string propertyName, object value)
    {
        entry.GetType().GetProperty(propertyName)?.SetValue(entry, value);
    }

    private void SubmitText(string message)
    {
        message = message.Trim();
        if (message.Length == 0)
        {
            return;
        }
        if (ChatState.SeedQueue.TryQueue(message))
        {
            _status = ModLocalization.T("chat.status.seed_thinking");
            return;
        }
        if (_requestPending)
        {
            return;
        }

        _input = "";
        _requestPending = true;
        _status = ModLocalization.T("chat.status.thinking");
        SetChatBusy(true, ModLocalization.T("chat.status.thinking"));
        CompanionTasks.Send(
            _client,
            SessionId,
            PlayerName(),
            message,
            ModLocalization.CurrentLocale,
            _lifetime.Token,
            (text, emotion, durationMs, status) => _mainThread.Enqueue(() => ApplyReply(text, emotion, durationMs, status)),
            error =>
            {
                PluginLogWarning(error);
                _mainThread.Enqueue(() =>
                {
                    _status = ModLocalization.T("chat.status.offline");
                    AppendChatMessage(ChatSpeaker.System, ModLocalization.T("chat.service_failed"));
                    _requestPending = false;
                    SetChatBusy(false);
                });
            });
    }

    private void CaptureInput()
    {
        string typed = Input.inputString;
        foreach (char character in typed)
        {
            if (character == '\b')
            {
                if (_input.Length > 0)
                {
                    _input = _input.Substring(0, _input.Length - 1);
                }
            }
            else if (character != '\r' && character != '\n' && !char.IsControl(character))
            {
                if (_input.Length < 2000)
                {
                    _input += character;
                }
            }
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            Submit();
        }
    }

    private void ApplyReply(string text, string emotion, int durationMs, string status)
    {
        AppendChatMessage(ChatSpeaker.Lilith, text);
        string displayText = DialogueTextFormatter.WrapForLocale(text, ModLocalization.CurrentLocale);
        UserInterfacePatches.MarkCompanionDialogue(displayText, durationMs / 1000f);
        _status = status == "fallback"
            ? ModLocalization.T("chat.status.unconfigured")
            : ModLocalization.T("chat.status.listening");
        if (File.Exists(Path.Combine(Paths.PluginPath, "LilithMod.no-dialogue")))
        {
            LilithModPlugin.PluginLog.LogWarning("no-dialogue sentinel present; skipping ForceSay");
            ShowFallbackReply(displayText, durationMs);
            _requestPending = false;
            SetChatBusy(false);
            return;
        }

        try
        {
            DialogueManager? dialogue = DialogueManager.instance;
            if (dialogue is null)
            {
                LilithModPlugin.PluginLog.LogWarning("DialogueManager is unavailable; using character-anchored reply fallback");
                ShowFallbackReply(displayText, durationMs);
                return;
            }

            if (dialogue.IsBusyOrAwaitingResponse)
            {
                LilithModPlugin.PluginLog.LogWarning("Dialogue is busy; using character-anchored reply fallback");
                ShowFallbackReply(displayText, durationMs);
                return;
            }

            LilithModPlugin.PluginLog.LogInfo("Applying Companion reply through ForceSay");
            UserInterfacePatches.SetCompanionDialogueScope(true);
            bool accepted;
            try
            {
                accepted = dialogue.ForceSay(displayText, emotion, Math.Max(2f, durationMs / 1000f));
            }
            finally
            {
                UserInterfacePatches.SetCompanionDialogueScope(false);
            }
            LilithModPlugin.PluginLog.LogInfo($"ForceSay returned: {accepted}");
            if (accepted)
            {
                _lastReply = "";
                _replyUntil = 0f;
                return;
            }

            ShowFallbackReply(displayText, durationMs);
        }
        catch (Exception error)
        {
            PluginLogWarning(error);
            ShowFallbackReply(displayText, durationMs);
        }
        finally
        {
            _requestPending = false;
            SetChatBusy(false);
        }
    }

    private void ShowFallbackReply(string text, int durationMs)
    {
        _lastReply = text;
        _replyUntil = Time.unscaledTime + Math.Max(2000, durationMs) / 1000f;
    }

    private static Rect FallbackReplyRect()
    {
        const float width = 560f;
        const float height = 112f;
        float centerX = Screen.width * 0.5f;
        float top = Mathf.Max(24f, Screen.height * 0.5f - 220f);

        try
        {
            if (CharacterController.TryGetCharacterDesktopPoint(out int desktopX, out int desktopY))
            {
                int virtualLeft = GetSystemMetrics(SmXVirtualScreen);
                int virtualTop = GetSystemMetrics(SmYVirtualScreen);
                centerX = desktopX - virtualLeft;
                top = desktopY - virtualTop - height - 72f;
            }
        }
        catch (Exception error)
        {
            PluginLogWarning(error);
        }

        float actualWidth = Mathf.Min(width, Mathf.Max(320f, Screen.width - 32f));
        float left = Mathf.Clamp(centerX - actualWidth * 0.5f, 16f, Mathf.Max(16f, Screen.width - actualWidth - 16f));
        top = Mathf.Clamp(top, 16f, Mathf.Max(16f, Screen.height - height - 16f));
        return new Rect(left, top, actualWidth, height);
    }

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private static string PlayerName()
    {
        try
        {
            string value = Archive.Instance?.playerName ?? "";
            value = value.Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }
        catch (Exception error)
        {
            PluginLogWarning(error);
        }
        return ModLocalization.T("common.you");
    }

    private void EnsureStyles()
    {
        if (_replyStyle is null)
        {
            _replyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                clipping = TextClipping.Clip,
                fontSize = 24,
                wordWrap = true,
                richText = false,
                padding = new RectOffset(8, 8, 4, 4)
            };
            _replyStyle.normal.textColor = Color.white;
        }
        if (_statusStyle is null)
        {
            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.LowerLeft,
                fontSize = 14
            };
            _statusStyle.normal.textColor = Color.white;
        }
    }

    private static void PluginLogWarning(Exception error)
    {
        LilithModPlugin.PluginLog.LogWarning($"{error.GetType().Name}: {error.Message}");
    }

    private static void PluginLogWarning(string error)
    {
        LilithModPlugin.PluginLog.LogWarning(error);
    }
}
