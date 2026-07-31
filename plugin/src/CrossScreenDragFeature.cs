using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using UnityEngine;

namespace LilithMod;

internal static class CrossScreenDragFeature
{
    private const float PreferencePollSeconds = 1f;
    private const float MonitorRefreshSeconds = 2f;
    private const float ReleaseSettleSeconds = 0.75f;

    private static readonly string PreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LilithAI",
        "ui.json");
    private static readonly MonitorEnumProc MonitorCallback = OnMonitor;
    private static readonly List<DesktopMonitorArea> Monitors = new();

    private static DateTime _preferencesWriteTimeUtc = DateTime.MinValue;
    private static float _nextPreferencePoll;
    private static float _nextMonitorRefresh;
    private static bool _desiredEnabled;
    private static bool _effective;
    private static bool _dragSignal;
    private static bool _wasDragging;
    private static bool _groundTopOverrideActive;
    private static bool _stateLogged;
    private static int _activeMonitorIndex = -1;
    private static int _lastDragMonitorIndex = -1;
    private static float _groundTopWorldY;
    private static string _monitorSignature = string.Empty;
    private static BoxCollider2D? _bottomCollider;
    private static IntPtr _unityWindow;
    private static bool _bottomColliderLogged;
    private static bool _segmentedGroundLogged;
    private static bool _invalidGroundLayoutLogged;
    private static float _releaseLockUntil;
    private static int _releaseMonitorIndex = -1;
    private static int _lastCursorX;
    private static int _lastCursorY;
    private static bool _hasLastCursorPoint;
    private static BoxCollider2D? _leasedGroundCollider;
    private static PropertyInfo? _groundColliderCountProperty;
    private static bool _groundColliderCountPropertyResolved;

    private enum GroundMode
    {
        Legacy,
        Segmented,
        Invalid
    }

    private static GroundMode _groundMode = GroundMode.Legacy;

    internal static bool DesiredEnabled => _desiredEnabled;

    internal static bool TryGetGroundTopWorldY(out float groundTopWorldY)
    {
        groundTopWorldY = _groundTopWorldY;
        return _effective && _groundTopOverrideActive;
    }

    internal static void Tick()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        float now = Time.unscaledTime;
        RefreshPreference(now);
        if (!_desiredEnabled)
        {
            Deactivate();
            return;
        }

        if (now >= _nextMonitorRefresh)
        {
            _nextMonitorRefresh = now + MonitorRefreshSeconds;
            RefreshMonitors();
        }

        bool originalCrossScreenEnabled;
        try
        {
            originalCrossScreenEnabled = WindowsPlatformService.IsCrossScreenDragEnabled();
        }
        catch
        {
            originalCrossScreenEnabled = false;
        }

        if (!originalCrossScreenEnabled || Monitors.Count < 2)
        {
            Deactivate();
            if (!_stateLogged)
            {
                _stateLogged = true;
                LilithModPlugin.PluginLog.LogWarning(
                    originalCrossScreenEnabled
                        ? "Enhanced cross-screen drag is waiting for at least two monitors"
                        : "Enhanced cross-screen drag is enabled in Mod settings, but the game's Cross-Screen Drag setting is off");
            }
            return;
        }

        _stateLogged = false;
        ActiveWindowCollider? active = ActiveWindowCollider.instance;
        if (active is null)
        {
            return;
        }

        _groundMode = DetectGroundMode(active);
        if (_groundMode == GroundMode.Invalid)
        {
            Deactivate();
            if (!_invalidGroundLayoutLogged)
            {
                _invalidGroundLayoutLogged = true;
                LilithModPlugin.PluginLog.LogWarning(
                    "Enhanced cross-screen drag found an invalid native ground collider layout; waiting for the game to rebuild it");
            }
            return;
        }
        _invalidGroundLayoutLogged = false;
        _effective = true;
        if (_groundMode == GroundMode.Segmented && !_segmentedGroundLogged)
        {
            _segmentedGroundLogged = true;
            int groundCount = GetGroundColliderCount(active);
            int totalCount = active._screenEdgeColliders?.Count ?? 0;
            LilithModPlugin.PluginLog.LogInfo(
                $"Enhanced cross-screen drag using segmented native ground: groundColliders={groundCount}, totalColliders={totalCount}");
        }

        bool dragging = _dragSignal || IsCharacterDragging();
        if (dragging)
        {
            _wasDragging = true;
            UpdateDragTarget();
            DisableGround(active);
            return;
        }

        if (_wasDragging)
        {
            _wasDragging = false;
            _dragSignal = false;
            if (_lastDragMonitorIndex >= 0)
            {
                _activeMonitorIndex = _lastDragMonitorIndex;
            }
            BeginReleaseLock(_activeMonitorIndex);
        }
        else if (IsReleaseLocked(now))
        {
            _activeMonitorIndex = _releaseMonitorIndex;
            if (IsCharacterOnMonitor(_releaseMonitorIndex))
            {
                ClearReleaseLock("character entered target monitor");
            }
        }
        else
        {
            UpdateRestingMonitor();
        }

        ApplyGround(active, _activeMonitorIndex);
    }

    internal static void OnDragStarted()
    {
        if (!_effective)
        {
            return;
        }
        _dragSignal = true;
        _wasDragging = true;
        ClearReleaseLock("new drag started");
        UpdateDragTarget();
        ActiveWindowCollider? active = ActiveWindowCollider.instance;
        if (active is not null)
        {
            DisableGround(active);
        }
    }

    internal static void OnDragEndingPrefix()
    {
        if (!_effective)
        {
            return;
        }

        UpdateDragTarget();
        ActiveWindowCollider? active = ActiveWindowCollider.instance;
        if (active is not null)
        {
            DisableGround(active);
        }
    }

    internal static void OnDragEnded()
    {
        if (!_effective)
        {
            return;
        }

        UpdateDragTarget();
        _dragSignal = false;
        _wasDragging = false;
        if (_lastDragMonitorIndex >= 0)
        {
            _activeMonitorIndex = _lastDragMonitorIndex;
        }
        BeginReleaseLock(_activeMonitorIndex);
        RememberNativeMonitorForRelease();
        ActiveWindowCollider? active = ActiveWindowCollider.instance;
        if (active is not null)
        {
            ApplyGround(active, _activeMonitorIndex);
        }
    }

    internal static void ReapplyAfterOriginalGroundUpdate(ActiveWindowCollider active)
    {
        if (!_effective)
        {
            return;
        }

        if (_dragSignal || IsCharacterDragging())
        {
            DisableGround(active);
            return;
        }
        if (IsReleaseLocked(Time.unscaledTime))
        {
            ApplyGround(active, _releaseMonitorIndex);
            return;
        }
        ApplyGround(active, _activeMonitorIndex);
    }

    internal static void Shutdown()
    {
        _desiredEnabled = false;
        Deactivate();
        Monitors.Clear();
    }

    private static void RefreshPreference(float now)
    {
        if (now < _nextPreferencePoll)
        {
            return;
        }
        _nextPreferencePoll = now + PreferencePollSeconds;

        try
        {
            DateTime writeTime = File.Exists(PreferencesPath)
                ? File.GetLastWriteTimeUtc(PreferencesPath)
                : DateTime.MinValue;
            if (writeTime == _preferencesWriteTimeUtc)
            {
                return;
            }

            _preferencesWriteTimeUtc = writeTime;
            bool enabled = false;
            if (writeTime != DateTime.MinValue)
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(PreferencesPath));
                if (document.RootElement.TryGetProperty("EnhancedCrossScreenDrag", out JsonElement value)
                    && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
                {
                    enabled = value.GetBoolean();
                }
            }

            if (_desiredEnabled != enabled)
            {
                _desiredEnabled = enabled;
                _nextMonitorRefresh = 0f;
                _stateLogged = false;
                LilithModPlugin.PluginLog.LogInfo(
                    $"Enhanced cross-screen drag preference changed: enabled={enabled}");
            }
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogDebug(
                $"Enhanced cross-screen drag preference read failed: {error.GetType().Name}: {error.Message}");
        }
    }

    private static void RefreshMonitors()
    {
        Monitors.Clear();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorCallback, IntPtr.Zero);
            if (_activeMonitorIndex >= Monitors.Count)
            {
                _activeMonitorIndex = -1;
            }
            if (_lastDragMonitorIndex >= Monitors.Count)
            {
                _lastDragMonitorIndex = -1;
            }

            if (_desiredEnabled)
            {
                string[] descriptions = new string[Monitors.Count];
                for (int index = 0; index < Monitors.Count; index++)
                {
                    DesktopMonitorArea monitor = Monitors[index];
                    descriptions[index] =
                        $"#{index} bounds={Describe(monitor.Bounds)} work={Describe(monitor.WorkArea)} floorY={monitor.FloorDesktopY}";
                }
                string signature = string.Join("; ", descriptions);
                if (!string.Equals(signature, _monitorSignature, StringComparison.Ordinal))
                {
                    _monitorSignature = signature;
                    LilithModPlugin.PluginLog.LogInfo(
                        $"Cross-screen monitors changed: {signature}");
                }
            }
        }
        catch (Exception error)
        {
            Monitors.Clear();
            _monitorSignature = string.Empty;
            LilithModPlugin.PluginLog.LogWarning(
                $"Cross-screen monitor enumeration failed: {error.GetType().Name}: {error.Message}");
        }
    }

    private static bool OnMonitor(IntPtr monitorHandle, IntPtr monitorDc, ref NativeRect monitorRect, IntPtr data)
    {
        var info = new NativeMonitorInfo
        {
            Size = Marshal.SizeOf<NativeMonitorInfo>()
        };
        if (!GetMonitorInfo(monitorHandle, ref info))
        {
            return true;
        }

        DesktopBounds bounds = ToBounds(info.Monitor);
        DesktopBounds work = ToBounds(info.Work);
        Monitors.Add(new DesktopMonitorArea(bounds, work, (info.Flags & 1u) != 0));
        return true;
    }

    private static void UpdateDragTarget()
    {
        if (Monitors.Count == 0 || !GetCursorPos(out NativePoint point))
        {
            return;
        }
        _lastCursorX = point.X;
        _lastCursorY = point.Y;
        _hasLastCursorPoint = true;
        _lastDragMonitorIndex = CrossScreenGroundGeometry.SelectReleaseMonitor(Monitors, point.X, point.Y);
    }

    private static void BeginReleaseLock(int monitorIndex)
    {
        if (monitorIndex < 0 || monitorIndex >= Monitors.Count)
        {
            return;
        }

        _releaseMonitorIndex = monitorIndex;
        _releaseLockUntil = Time.unscaledTime + ReleaseSettleSeconds;
        LilithModPlugin.PluginLog.LogInfo(
            $"Enhanced cross-screen drag release lock: monitor={monitorIndex}, duration={ReleaseSettleSeconds:0.00}s");
    }

    private static bool IsReleaseLocked(float now)
    {
        if (CrossScreenGroundGeometry.IsReleaseLockActive(
            _releaseMonitorIndex,
            now,
            _releaseLockUntil))
        {
            return true;
        }

        if (_releaseMonitorIndex >= 0)
        {
            ClearReleaseLock("timeout");
        }
        return false;
    }

    private static void ClearReleaseLock(string reason)
    {
        if (_releaseMonitorIndex >= 0)
        {
            LilithModPlugin.PluginLog.LogInfo(
                $"Enhanced cross-screen drag release lock cleared: reason={reason}");
        }
        _releaseMonitorIndex = -1;
        _releaseLockUntil = 0f;
    }

    private static bool IsCharacterOnMonitor(int monitorIndex)
    {
        if (monitorIndex < 0 || monitorIndex >= Monitors.Count)
        {
            return false;
        }

        try
        {
            if (!CharacterController.TryGetCharacterDesktopPoint(out int desktopX, out int desktopY))
            {
                return false;
            }
            return CrossScreenGroundGeometry.FindMonitor(Monitors, desktopX, desktopY) == monitorIndex;
        }
        catch
        {
            return false;
        }
    }

    private static void RememberNativeMonitorForRelease()
    {
        if (!_hasLastCursorPoint)
        {
            return;
        }

        try
        {
            MethodInfo? method = typeof(WindowsPlatformService).GetMethod(
                "RememberMonitorForDesktopPoint",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(int), typeof(int) },
                modifiers: null);
            method?.Invoke(null, new object[] { _lastCursorX, _lastCursorY });
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogDebug(
                $"Native monitor remember call failed: {error.GetType().Name}: {error.Message}");
        }
    }

    private static void UpdateRestingMonitor()
    {
        if (Monitors.Count == 0)
        {
            _activeMonitorIndex = -1;
            return;
        }

        try
        {
            if (CharacterController.TryGetCharacterDesktopPoint(out int desktopX, out int desktopY))
            {
                _activeMonitorIndex = CrossScreenGroundGeometry.FindMonitor(
                    Monitors,
                    desktopX,
                    desktopY);
                return;
            }
        }
        catch
        {
            // Fall back to the cursor or the previous active monitor.
        }

        if (_activeMonitorIndex < 0 && GetCursorPos(out NativePoint point))
        {
            _activeMonitorIndex = CrossScreenGroundGeometry.FindMonitor(Monitors, point.X, point.Y);
        }
    }

    private static bool IsCharacterDragging()
    {
        try
        {
            CharacterController? controller = CharacterController.instance;
            return controller is not null && controller.IsDrag;
        }
        catch
        {
            return false;
        }
    }

    private static GroundMode DetectGroundMode(ActiveWindowCollider active)
    {
        PropertyInfo? property = ResolveGroundColliderCountProperty();
        if (property is null)
        {
            return GroundMode.Legacy;
        }

        try
        {
            Il2CppSystem.Collections.Generic.List<BoxCollider2D>? colliders = active._screenEdgeColliders;
            if (colliders is null)
            {
                return GroundMode.Invalid;
            }
            object? value = property.GetValue(active);
            if (value is not int groundCount
                || !CrossScreenGroundGeometry.IsSegmentedGroundLayoutValid(groundCount, colliders.Count))
            {
                return GroundMode.Invalid;
            }
            return GroundMode.Segmented;
        }
        catch
        {
            return GroundMode.Invalid;
        }
    }

    private static PropertyInfo? ResolveGroundColliderCountProperty()
    {
        if (_groundColliderCountPropertyResolved)
        {
            return _groundColliderCountProperty;
        }

        _groundColliderCountProperty = typeof(ActiveWindowCollider).GetProperty(
            "_groundColliderCount",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _groundColliderCountPropertyResolved = true;
        return _groundColliderCountProperty;
    }

    private static int GetGroundColliderCount(ActiveWindowCollider active)
    {
        try
        {
            return ResolveGroundColliderCountProperty()?.GetValue(active) is int count ? count : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool TryGetSegmentedGroundColliders(
        ActiveWindowCollider active,
        out Il2CppSystem.Collections.Generic.List<BoxCollider2D> colliders,
        out int groundCount)
    {
        colliders = null!;
        groundCount = GetGroundColliderCount(active);
        try
        {
            Il2CppSystem.Collections.Generic.List<BoxCollider2D>? candidates = active._screenEdgeColliders;
            if (candidates is null
                || !CrossScreenGroundGeometry.IsSegmentedGroundLayoutValid(groundCount, candidates.Count))
            {
                return false;
            }
            colliders = candidates;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyGround(ActiveWindowCollider active, int monitorIndex)
    {
        if (!_effective || monitorIndex < 0 || monitorIndex >= Monitors.Count)
        {
            _groundTopOverrideActive = false;
            return;
        }

        BoxCollider2D? bottom = _groundMode == GroundMode.Segmented
            ? FindLeasedGroundCollider(active)
            : FindBottomCollider(active);
        Camera? camera = active._cam ?? Camera.main;
        if (bottom is null || camera is null)
        {
            _groundTopOverrideActive = false;
            return;
        }

        DesktopMonitorArea monitor = Monitors[monitorIndex];
        int floorY = monitor.FloorDesktopY;
        if (!TryDesktopToUnityScreen(monitor.Bounds.Left - 2, floorY, out UnityScreenPoint left)
            || !TryDesktopToUnityScreen(monitor.Bounds.Right + 2, floorY, out UnityScreenPoint right))
        {
            _groundTopOverrideActive = false;
            return;
        }

        float characterZ = 0f;
        try
        {
            CharacterController? controller = CharacterController.instance;
            if (controller is not null)
            {
                characterZ = controller.transform.position.z;
            }
        }
        catch
        {
            // The character can be unavailable for a few frames during startup.
        }
        float depth = Mathf.Abs(characterZ - camera.transform.position.z);
        Vector3 leftWorld = camera.ScreenToWorldPoint(new Vector3(left.X, left.Y, depth));
        Vector3 rightWorld = camera.ScreenToWorldPoint(new Vector3(right.X, right.Y, depth));
        float targetWidthWorld = Mathf.Abs(rightWorld.x - leftWorld.x);
        if (targetWidthWorld <= 0.001f)
        {
            _groundTopOverrideActive = false;
            return;
        }

        bottom.enabled = true;
        float scaleX = Mathf.Abs(bottom.transform.lossyScale.x);
        if (scaleX <= 0.0001f)
        {
            scaleX = 1f;
        }
        Vector2 size = bottom.size;
        size.x = targetWidthWorld / scaleX;
        bottom.size = size;

        Physics2D.SyncTransforms();
        Bounds currentBounds = bottom.bounds;
        float targetCenterX = (leftWorld.x + rightWorld.x) * 0.5f;
        float targetCenterY = leftWorld.y - currentBounds.extents.y;
        Vector3 position = bottom.transform.position;
        position.x += targetCenterX - currentBounds.center.x;
        position.y += targetCenterY - currentBounds.center.y;
        bottom.transform.position = position;
        Physics2D.SyncTransforms();

        if (_groundMode == GroundMode.Segmented)
        {
            DisableOtherSegmentedGrounds(active);
        }

        _activeMonitorIndex = monitorIndex;
        _groundTopWorldY = leftWorld.y;
        _groundTopOverrideActive = true;
    }

    private static void DisableGround(ActiveWindowCollider active)
    {
        if (_groundMode == GroundMode.Segmented)
        {
            DisableAllSegmentedGrounds(active);
            _groundTopOverrideActive = TrySetGroundTopForMonitor(
                active,
                _lastDragMonitorIndex >= 0 ? _lastDragMonitorIndex : _activeMonitorIndex);
            return;
        }

        BoxCollider2D? bottom = FindBottomCollider(active);
        if (bottom is not null)
        {
            bottom.enabled = false;
        }
        _groundTopOverrideActive = TrySetGroundTopForMonitor(
            active,
            _lastDragMonitorIndex >= 0 ? _lastDragMonitorIndex : _activeMonitorIndex);
    }

    private static bool TrySetGroundTopForMonitor(ActiveWindowCollider active, int monitorIndex)
    {
        if (monitorIndex < 0 || monitorIndex >= Monitors.Count)
        {
            return false;
        }

        Camera? camera = active._cam ?? Camera.main;
        if (camera is null)
        {
            return false;
        }

        DesktopMonitorArea monitor = Monitors[monitorIndex];
        if (!TryDesktopToUnityScreen(monitor.Bounds.Left, monitor.FloorDesktopY, out UnityScreenPoint left))
        {
            return false;
        }

        float characterZ = 0f;
        try
        {
            CharacterController? controller = CharacterController.instance;
            if (controller is not null)
            {
                characterZ = controller.transform.position.z;
            }
        }
        catch
        {
            // Use the camera plane while the character is being recreated.
        }

        float depth = Mathf.Abs(characterZ - camera.transform.position.z);
        _groundTopWorldY = camera.ScreenToWorldPoint(new Vector3(left.X, left.Y, depth)).y;
        return true;
    }

    private static BoxCollider2D? FindLeasedGroundCollider(ActiveWindowCollider active)
    {
        if (!TryGetSegmentedGroundColliders(active, out var colliders, out _))
        {
            return null;
        }

        BoxCollider2D? leased = colliders[0];
        if (leased is null)
        {
            return null;
        }

        if (_leasedGroundCollider is null || _leasedGroundCollider != leased)
        {
            _leasedGroundCollider = leased;
            LilithModPlugin.PluginLog.LogInfo("Enhanced cross-screen drag leased native ground collider index 0");
        }
        return leased;
    }

    private static void DisableAllSegmentedGrounds(ActiveWindowCollider active)
    {
        if (!TryGetSegmentedGroundColliders(active, out var colliders, out int groundCount))
        {
            return;
        }

        for (int index = 0; index < groundCount; index++)
        {
            BoxCollider2D? collider = colliders[index];
            if (collider is not null
                && !CrossScreenGroundGeometry.ShouldEnableSegmentedGroundCollider(
                    index,
                    groundCount,
                    dragging: true))
            {
                collider.enabled = false;
            }
        }
    }

    private static void DisableOtherSegmentedGrounds(ActiveWindowCollider active)
    {
        if (!TryGetSegmentedGroundColliders(active, out var colliders, out int groundCount))
        {
            return;
        }

        for (int index = 0; index < groundCount; index++)
        {
            BoxCollider2D? collider = colliders[index];
            if (collider is not null
                && !CrossScreenGroundGeometry.ShouldEnableSegmentedGroundCollider(
                    index,
                    groundCount,
                    dragging: false))
            {
                collider.enabled = false;
            }
        }
    }

    private static BoxCollider2D? FindBottomCollider(ActiveWindowCollider active)
    {
        try
        {
            Il2CppSystem.Collections.Generic.List<BoxCollider2D>? colliders = active._screenEdgeColliders;
            if (colliders is null || colliders.Count == 0)
            {
                return null;
            }

            if (_bottomCollider is not null)
            {
                for (int index = 0; index < colliders.Count; index++)
                {
                    if (colliders[index] == _bottomCollider)
                    {
                        return _bottomCollider;
                    }
                }
                _bottomCollider = null;
                _bottomColliderLogged = false;
            }

            BoxCollider2D? bottom = null;
            float bottomCenter = float.PositiveInfinity;
            for (int index = 0; index < colliders.Count; index++)
            {
                BoxCollider2D candidate = colliders[index];
                if (candidate is null || !candidate.enabled)
                {
                    continue;
                }
                Bounds bounds = candidate.bounds;
                if (bounds.size.x <= bounds.size.y || bounds.size.x <= 0.001f || bounds.center.y >= bottomCenter)
                {
                    continue;
                }
                bottom = candidate;
                bottomCenter = bounds.center.y;
            }
            _bottomCollider = bottom;
            if (bottom is not null && !_bottomColliderLogged)
            {
                _bottomColliderLogged = true;
                Bounds bounds = bottom.bounds;
                LilithModPlugin.PluginLog.LogInfo(
                    $"Enhanced cross-screen drag locked original bottom collider: center=({bounds.center.x:F3},{bounds.center.y:F3}), size=({bounds.size.x:F3},{bounds.size.y:F3})");
            }
            return _bottomCollider;
        }
        catch
        {
            return null;
        }
    }

    private static void Deactivate()
    {
        if (!_effective)
        {
            return;
        }

        _effective = false;
        _groundTopOverrideActive = false;
        _dragSignal = false;
        _wasDragging = false;
        _activeMonitorIndex = -1;
        _lastDragMonitorIndex = -1;
        _releaseMonitorIndex = -1;
        _releaseLockUntil = 0f;
        _hasLastCursorPoint = false;
        _groundMode = GroundMode.Legacy;
        try
        {
            ActiveWindowCollider? active = ActiveWindowCollider.instance;
            active?.UpdateScreenEdgeColliders();
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogDebug(
                $"Original screen ground restore failed: {error.GetType().Name}: {error.Message}");
        }
        _bottomCollider = null;
        _leasedGroundCollider = null;
        _bottomColliderLogged = false;
        _segmentedGroundLogged = false;
        _unityWindow = IntPtr.Zero;
    }

    private static bool TryDesktopToUnityScreen(int desktopX, int desktopY, out UnityScreenPoint result)
    {
        result = default;
        IntPtr window = GetUnityWindow();
        if (window == IntPtr.Zero)
        {
            return false;
        }

        var point = new NativePoint { X = desktopX, Y = desktopY };
        if (!ScreenToClient(window, ref point))
        {
            _unityWindow = IntPtr.Zero;
            return false;
        }

        if (!GetClientRect(window, out NativeRect clientRect))
        {
            _unityWindow = IntPtr.Zero;
            return false;
        }
        result = CrossScreenGroundGeometry.ClientToUnityScreen(
            point.X,
            point.Y,
            clientRect.Right - clientRect.Left,
            clientRect.Bottom - clientRect.Top,
            Screen.width,
            Screen.height);
        return true;
    }

    private static IntPtr GetUnityWindow()
    {
        if (_unityWindow != IntPtr.Zero && IsWindow(_unityWindow))
        {
            return _unityWindow;
        }

        _unityWindow = IntPtr.Zero;
        int processId = Process.GetCurrentProcess().Id;
        long largestArea = -1;
        bool foundUnityWindow = false;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint ownerProcessId);
            if (ownerProcessId != (uint)processId || !IsWindowVisible(window))
            {
                return true;
            }

            var className = new StringBuilder(128);
            GetClassName(window, className, className.Capacity);
            bool unityClass = className.ToString().IndexOf("UnityWndClass", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!GetClientRect(window, out NativeRect client))
            {
                return true;
            }
            long area = Math.Max(0, client.Right - client.Left) * (long)Math.Max(0, client.Bottom - client.Top);
            if (unityClass && area > 0)
            {
                _unityWindow = window;
                largestArea = area;
                foundUnityWindow = true;
                return false;
            }
            if (!foundUnityWindow && area > largestArea)
            {
                _unityWindow = window;
                largestArea = area;
            }
            return true;
        }, IntPtr.Zero);
        return _unityWindow;
    }

    private static string Describe(DesktopBounds bounds) =>
        $"({bounds.Left},{bounds.Top})-({bounds.Right},{bounds.Bottom})";

    private static DesktopBounds ToBounds(NativeRect rect) =>
        new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private delegate bool MonitorEnumProc(
        IntPtr monitorHandle,
        IntPtr monitorDc,
        ref NativeRect monitorRect,
        IntPtr data);

    private delegate bool WindowEnumProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr clipRect,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref NativeMonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(WindowEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int capacity);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect clientRect);
}

internal static class CrossScreenDragPatches
{
    internal static void Apply(Harmony harmony)
    {
        MethodInfo? beginDrag = AccessTools.Method(
            typeof(CharacterInteractionHandler),
            nameof(CharacterInteractionHandler.BeginDrag),
            Type.EmptyTypes);
        MethodInfo? endDrag = AccessTools.Method(
            typeof(CharacterInteractionHandler),
            nameof(CharacterInteractionHandler.EndDrag),
            Type.EmptyTypes);
        MethodInfo? updateGround = AccessTools.Method(
            typeof(ActiveWindowCollider),
            nameof(ActiveWindowCollider.UpdateScreenEdgeColliders),
            Type.EmptyTypes);
        MethodInfo? groundTop = AccessTools.Method(
            typeof(ActiveWindowCollider),
            nameof(ActiveWindowCollider.GetGroundTopWorldY),
            Type.EmptyTypes);
        MethodInfo? groundTopAtX = AccessTools.Method(
            typeof(ActiveWindowCollider),
            nameof(ActiveWindowCollider.GetGroundTopWorldY),
            new[] { typeof(float) });

        if (beginDrag is not null)
        {
            harmony.Patch(
                beginDrag,
                prefix: new HarmonyMethod(typeof(CrossScreenDragPatches), nameof(BeginDragPrefix)));
        }
        if (endDrag is not null)
        {
            harmony.Patch(
                endDrag,
                prefix: new HarmonyMethod(typeof(CrossScreenDragPatches), nameof(EndDragPrefix)),
                postfix: new HarmonyMethod(typeof(CrossScreenDragPatches), nameof(EndDragPostfix)));
        }
        if (updateGround is not null)
        {
            harmony.Patch(
                updateGround,
                postfix: new HarmonyMethod(typeof(CrossScreenDragPatches), nameof(UpdateGroundPostfix)));
        }
        if (groundTop is not null)
        {
            harmony.Patch(
                groundTop,
                postfix: new HarmonyMethod(typeof(CrossScreenDragPatches), nameof(GroundTopPostfix)));
        }
        if (groundTopAtX is not null)
        {
            harmony.Patch(
                groundTopAtX,
                postfix: new HarmonyMethod(typeof(CrossScreenDragPatches), nameof(GroundTopAtXPostfix)));
        }
    }

    private static void BeginDragPrefix() => CrossScreenDragFeature.OnDragStarted();

    private static void EndDragPrefix() => CrossScreenDragFeature.OnDragEndingPrefix();

    private static void EndDragPostfix() => CrossScreenDragFeature.OnDragEnded();

    private static void UpdateGroundPostfix(ActiveWindowCollider __instance) =>
        CrossScreenDragFeature.ReapplyAfterOriginalGroundUpdate(__instance);

    private static void GroundTopPostfix(ref float __result)
    {
        if (CrossScreenDragFeature.TryGetGroundTopWorldY(out float groundTop))
        {
            __result = groundTop;
        }
    }

    private static void GroundTopAtXPostfix(ref float __result)
    {
        if (CrossScreenDragFeature.TryGetGroundTopWorldY(out float groundTop))
        {
            __result = groundTop;
        }
    }
}
