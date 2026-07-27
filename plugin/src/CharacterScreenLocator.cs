using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using UnityEngine;

namespace LilithMod;

internal interface ICharacterScreenLocator
{
    bool TryGetPlacementArea(out CharacterScreenPlacement placement);

    bool IsScreenPointVisible(float screenX, float screenY);
}

internal readonly record struct CharacterScreenPlacement(
    float CharacterX,
    float Left,
    float Bottom,
    float Right,
    float Top);

internal static class CharacterScreenLocator
{
    public static ICharacterScreenLocator Create()
    {
        return OperatingSystem.IsWindows()
            ? new WindowsCharacterScreenLocator()
            : new UnavailableCharacterScreenLocator();
    }

    private sealed class UnavailableCharacterScreenLocator : ICharacterScreenLocator
    {
        public bool TryGetPlacementArea(out CharacterScreenPlacement placement)
        {
            placement = default;
            return false;
        }

        public bool IsScreenPointVisible(float screenX, float screenY)
        {
            return false;
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsCharacterScreenLocator : ICharacterScreenLocator
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint MonitorDefaultToNull = 0;
    private const uint MonitorDefaultToNearest = 2;

    public bool TryGetPlacementArea(out CharacterScreenPlacement placement)
    {
        placement = default;
        try
        {
            if (!CharacterController.TryGetCharacterDesktopPoint(out int desktopX, out int desktopY)
                || !TryGetVirtualDesktop(out NativeRect desktop, out float scaleX, out float scaleY))
            {
                return false;
            }

            IntPtr monitor = MonitorFromPoint(
                new NativePoint { X = desktopX, Y = desktopY },
                MonitorDefaultToNearest);
            var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            {
                return false;
            }

            NativeRect work = info.Work;
            float virtualBottom = desktop.Bottom;
            placement = new CharacterScreenPlacement(
                (desktopX - desktop.Left) * scaleX,
                (work.Left - desktop.Left) * scaleX,
                (virtualBottom - work.Bottom) * scaleY,
                (work.Right - desktop.Left) * scaleX,
                (virtualBottom - work.Top) * scaleY);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool IsScreenPointVisible(float screenX, float screenY)
    {
        try
        {
            if (!TryGetVirtualDesktop(out NativeRect desktop, out float scaleX, out float scaleY))
            {
                return false;
            }
            int desktopX = desktop.Left + (int)Math.Round(screenX / scaleX);
            int desktopY = desktop.Bottom - (int)Math.Round(screenY / scaleY);
            return MonitorFromPoint(
                new NativePoint { X = desktopX, Y = desktopY },
                MonitorDefaultToNull) != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetVirtualDesktop(
        out NativeRect desktop,
        out float scaleX,
        out float scaleY)
    {
        int left = GetSystemMetrics(SmXVirtualScreen);
        int top = GetSystemMetrics(SmYVirtualScreen);
        int width = GetSystemMetrics(SmCxVirtualScreen);
        int height = GetSystemMetrics(SmCyVirtualScreen);
        desktop = new NativeRect
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height
        };
        scaleX = width > 0 ? Screen.width / (float)width : 0f;
        scaleY = height > 0 ? Screen.height / (float)height : 0f;
        return width > 0 && height > 0 && scaleX > 0f && scaleY > 0f;
    }

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

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);
}
