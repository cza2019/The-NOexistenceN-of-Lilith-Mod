using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LilithMod;

internal interface ICharacterScreenLocator
{
    bool TryGetHorizontalPosition(out float screenX);
}

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
        public bool TryGetHorizontalPosition(out float screenX)
        {
            screenX = 0f;
            return false;
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsCharacterScreenLocator : ICharacterScreenLocator
{
    private const int SmXVirtualScreen = 76;

    public bool TryGetHorizontalPosition(out float screenX)
    {
        screenX = 0f;
        try
        {
            if (!CharacterController.TryGetCharacterDesktopPoint(out int desktopX, out _))
            {
                return false;
            }
            screenX = desktopX - GetSystemMetrics(SmXVirtualScreen);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
