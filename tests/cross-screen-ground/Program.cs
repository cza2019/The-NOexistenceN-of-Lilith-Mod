using System;
using LilithMod;

var horizontal = new[]
{
    new DesktopMonitorArea(
        new DesktopBounds(0, 0, 1920, 1080),
        new DesktopBounds(0, 0, 1920, 1040),
        true),
    new DesktopMonitorArea(
        new DesktopBounds(1920, 0, 3840, 1080),
        new DesktopBounds(1920, 0, 3840, 1080),
        false)
};

Equal(0, CrossScreenGroundGeometry.FindMonitor(horizontal, 400, 500), "primary monitor");
Equal(1, CrossScreenGroundGeometry.FindMonitor(horizontal, 2500, 500), "secondary monitor");
Equal(1040, horizontal[0].FloorDesktopY, "primary taskbar floor");
Equal(1080, horizontal[1].FloorDesktopY, "taskbar-free floor");

var verticalWithGap = new[]
{
    new DesktopMonitorArea(
        new DesktopBounds(0, -1200, 1920, 0),
        new DesktopBounds(0, -1200, 1920, 0),
        false),
    new DesktopMonitorArea(
        new DesktopBounds(200, 400, 2120, 1480),
        new DesktopBounds(200, 400, 2120, 1440),
        true)
};

Equal(0, CrossScreenGroundGeometry.FindMonitor(verticalWithGap, 800, -200), "upper monitor");
Equal(1, CrossScreenGroundGeometry.FindMonitor(verticalWithGap, 800, 900), "lower monitor");
Equal(0, CrossScreenGroundGeometry.FindMonitor(verticalWithGap, 800, 120), "nearest monitor across gap");
Equal(1, CrossScreenGroundGeometry.FindMonitor(verticalWithGap, 800, 300), "nearest lower monitor across gap");

var verticallyStacked = new[]
{
    new DesktopMonitorArea(
        new DesktopBounds(0, 0, 3840, 2160),
        new DesktopBounds(0, 0, 3840, 2160),
        true),
    new DesktopMonitorArea(
        new DesktopBounds(1023, 2160, 3583, 3760),
        new DesktopBounds(1023, 2160, 3583, 3760),
        false)
};

Equal(1, CrossScreenGroundGeometry.SelectReleaseMonitor(verticallyStacked, 1800, 2300), "lower release target");
if (!CrossScreenGroundGeometry.IsReleaseLockActive(1, 10f, 10.75f)
    || CrossScreenGroundGeometry.IsReleaseLockActive(1, 10.75f, 10.75f)
    || CrossScreenGroundGeometry.IsReleaseLockActive(-1, 10f, 10.75f))
{
    throw new InvalidOperationException("Release monitor lock timing is inconsistent.");
}

if (!CrossScreenGroundGeometry.IsSegmentedGroundLayoutValid(3, 5)
    || CrossScreenGroundGeometry.IsSegmentedGroundLayoutValid(0, 5)
    || CrossScreenGroundGeometry.IsSegmentedGroundLayoutValid(6, 5)
    || CrossScreenGroundGeometry.ShouldEnableSegmentedGroundCollider(0, 3, dragging: true)
    || CrossScreenGroundGeometry.ShouldEnableSegmentedGroundCollider(1, 3, dragging: false)
    || !CrossScreenGroundGeometry.ShouldEnableSegmentedGroundCollider(0, 3, dragging: false))
{
    throw new InvalidOperationException("Segmented ground collider policy is inconsistent.");
}

UnityScreenPoint topLeft = CrossScreenGroundGeometry.ClientToUnityScreen(0, 0, 3000, 3000, 3000, 3000);
Near(0f, topLeft.X, "client left");
Near(3000f, topLeft.Y, "Unity top");

UnityScreenPoint bottomRight = CrossScreenGroundGeometry.ClientToUnityScreen(1500, 1500, 1500, 1500, 1500, 1500);
Near(1500f, bottomRight.X, "client right");
Near(0f, bottomRight.Y, "Unity bottom");

UnityScreenPoint dpiScaled = CrossScreenGroundGeometry.ClientToUnityScreen(1200, 675, 2400, 1350, 1920, 1080);
Near(960f, dpiScaled.X, "DPI-scaled horizontal center");
Near(540f, dpiScaled.Y, "DPI-scaled vertical center");

Console.WriteLine("Cross-screen ground geometry tests passed.");

static void Equal(int expected, int actual, string label)
{
    if (expected != actual)
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}

static void Near(float expected, float actual, string label)
{
    if (Math.Abs(expected - actual) > 0.01f)
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}
