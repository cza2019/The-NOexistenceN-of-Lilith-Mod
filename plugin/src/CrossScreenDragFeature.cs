using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using UnityEngine;

namespace LilithMod;

internal static class CrossScreenDragFeature
{
    private const float PreferencePollSeconds = 1f;
    private const float MonitorRefreshSeconds = 2f;

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
    private static bool _groundActive;
    private static bool _stateLogged;
    private static int _activeMonitorIndex = -1;
    private static int _lastDragMonitorIndex = -1;
    private static float _groundTopWorldY;
    private static string _monitorSignature = string.Empty;
    private static BoxCollider2D? _bottomCollider;
    private static IntPtr _unityWindow;
    private static bool _bottomColliderLogged;

    internal static bool DesiredEnabled => _desiredEnabled;

    internal static bool TryGetGroundTopWorldY(out float groundTopWorldY)
    {
        groundTopWorldY = _groundTopWorldY;
        return _effective && _groundActive;
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
        _effective = true;
        ActiveWindowCollider? active = ActiveWindowCollider.instance;
        if (active is null)
        {
            return;
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
        UpdateDragTarget();
        ActiveWindowCollider? active = ActiveWindowCollider.instance;
        if (active is not null)
        {
            DisableGround(active);
        }
    }

    internal static void OnDragEnding()
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
        _lastDragMonitorIndex = CrossScreenGroundGeometry.FindMonitor(Monitors, point.X, point.Y);
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

    private static void ApplyGround(ActiveWindowCollider active, int monitorIndex)
    {
        if (!_effective || monitorIndex < 0 || monitorIndex >= Monitors.Count)
        {
            return;
        }

        BoxCollider2D? bottom = FindBottomCollider(active);
        Camera? camera = active._cam ?? Camera.main;
        if (bottom is null || camera is null)
        {
            _groundActive = false;
            return;
        }

        DesktopMonitorArea monitor = Monitors[monitorIndex];
        int floorY = monitor.FloorDesktopY;
        if (!TryDesktopToUnityScreen(monitor.Bounds.Left - 2, floorY, out UnityScreenPoint left)
            || !TryDesktopToUnityScreen(monitor.Bounds.Right + 2, floorY, out UnityScreenPoint right))
        {
            _groundActive = false;
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
            _groundActive = false;
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

        _activeMonitorIndex = monitorIndex;
        _groundTopWorldY = leftWorld.y;
        _groundActive = true;
    }

    private static void DisableGround(ActiveWindowCollider active)
    {
        BoxCollider2D? bottom = FindBottomCollider(active);
        if (bottom is not null)
        {
            bottom.enabled = false;
        }
        _groundActive = false;
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
        _groundActive = false;
        _dragSignal = false;
        _wasDragging = false;
        _activeMonitorIndex = -1;
        _lastDragMonitorIndex = -1;
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
        _bottomColliderLogged = false;
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
        harmony.Patch(
            AccessTools.Method(typeof(CharacterInteractionHandler), nameof(CharacterInteractionHandler.BeginDrag)),
            prefix: new HarmonyMethod(typeof(CrossScreenDragPatches), nameof(BeginDragPrefix)));
        harmony.Patch(
            AccessTools.Method(typeof(CharacterInteractionHandler), nameof(CharacterInteractionHandler.EndDrag)),
            prefix: new HarmonyMethod(typeof(CrossScreenDragPatches), nameof(EndDragPrefix)));
        harmony.Patch(
            AccessTools.Method(typeof(ActiveWindowCollider), nameof(ActiveWindowCollider.UpdateScreenEdgeColliders)),
            postfix: new HarmonyMethod(typeof(CrossScreenDragPatches), nameof(UpdateGroundPostfix)));
        harmony.Patch(
            AccessTools.Method(typeof(ActiveWindowCollider), nameof(ActiveWindowCollider.GetGroundTopWorldY)),
            postfix: new HarmonyMethod(typeof(CrossScreenDragPatches), nameof(GroundTopPostfix)));
    }

    private static void BeginDragPrefix() => CrossScreenDragFeature.OnDragStarted();

    private static void EndDragPrefix() => CrossScreenDragFeature.OnDragEnding();

    private static void UpdateGroundPostfix(ActiveWindowCollider __instance) =>
        CrossScreenDragFeature.ReapplyAfterOriginalGroundUpdate(__instance);

    private static void GroundTopPostfix(ref float __result)
    {
        if (CrossScreenDragFeature.TryGetGroundTopWorldY(out float groundTop))
        {
            __result = groundTop;
        }
    }
}
