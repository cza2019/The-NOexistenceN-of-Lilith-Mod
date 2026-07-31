using System;
using System.Collections.Generic;

namespace LilithMod;

internal readonly record struct DesktopBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);

    public int Height => Math.Max(0, Bottom - Top);

    public bool Contains(int x, int y) =>
        x >= Left && x < Right && y >= Top && y < Bottom;

    public double DistanceSquaredTo(int x, int y)
    {
        long dx = x < Left ? (long)Left - x : x >= Right ? (long)x - Right + 1 : 0;
        long dy = y < Top ? (long)Top - y : y >= Bottom ? (long)y - Bottom + 1 : 0;
        return (double)dx * dx + (double)dy * dy;
    }
}

internal readonly record struct DesktopMonitorArea(
    DesktopBounds Bounds,
    DesktopBounds WorkArea,
    bool IsPrimary)
{
    public int FloorDesktopY
    {
        get
        {
            int candidate = WorkArea.Height > 0 ? WorkArea.Bottom : Bounds.Bottom;
            return Math.Clamp(candidate, Bounds.Top, Bounds.Bottom);
        }
    }
}

internal readonly record struct UnityScreenPoint(float X, float Y);

internal static class CrossScreenGroundGeometry
{
    public static int SelectReleaseMonitor(
        IReadOnlyList<DesktopMonitorArea> monitors,
        int desktopX,
        int desktopY) => FindMonitor(monitors, desktopX, desktopY);

    public static bool IsReleaseLockActive(int targetMonitorIndex, float now, float lockUntil) =>
        targetMonitorIndex >= 0 && now < lockUntil;

    public static bool IsSegmentedGroundLayoutValid(int groundColliderCount, int totalColliderCount) =>
        groundColliderCount > 0 && groundColliderCount <= totalColliderCount;

    public static bool ShouldEnableSegmentedGroundCollider(
        int colliderIndex,
        int groundColliderCount,
        bool dragging) =>
        colliderIndex >= groundColliderCount || (!dragging && colliderIndex == 0);

    public static int FindMonitor(
        IReadOnlyList<DesktopMonitorArea> monitors,
        int desktopX,
        int desktopY)
    {
        if (monitors.Count == 0)
        {
            return -1;
        }

        for (int index = 0; index < monitors.Count; index++)
        {
            if (monitors[index].Bounds.Contains(desktopX, desktopY))
            {
                return index;
            }
        }

        int nearest = 0;
        double nearestDistance = monitors[0].Bounds.DistanceSquaredTo(desktopX, desktopY);
        for (int index = 1; index < monitors.Count; index++)
        {
            double distance = monitors[index].Bounds.DistanceSquaredTo(desktopX, desktopY);
            if (distance < nearestDistance)
            {
                nearest = index;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    public static UnityScreenPoint ClientToUnityScreen(
        int clientX,
        int clientY,
        int clientWidth,
        int clientHeight,
        int unityWidth,
        int unityHeight)
    {
        float scaleX = Math.Max(1, unityWidth) / (float)Math.Max(1, clientWidth);
        float scaleY = Math.Max(1, unityHeight) / (float)Math.Max(1, clientHeight);
        return new UnityScreenPoint(
            clientX * scaleX,
            (Math.Max(1, clientHeight) - clientY) * scaleY);
    }
}
