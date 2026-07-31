using System;
using System.Reflection;

namespace LilithMod;

internal static class PlayerLineOptionsCompat
{
    private static readonly object Sync = new();
    private static MethodInfo? _showOptions;
    private static bool _resolved;
    private static bool _failureLogged;

    internal static bool TryShow(
        PlayerLineController controller,
        Il2CppSystem.Collections.Generic.IReadOnlyList<string> options,
        Il2CppSystem.Action<int> onSelected)
    {
        MethodInfo? method = Resolve();
        if (method is null)
        {
            LogFailure("no compatible ShowOptions overload found");
            return false;
        }

        try
        {
            ParameterInfo[] parameters = method.GetParameters();
            object?[] arguments = parameters.Length == 3
                ? new object?[] { options, onSelected, null }
                : new object?[] { options, onSelected };
            method.Invoke(controller, arguments);
            return true;
        }
        catch (Exception error)
        {
            LogFailure($"ShowOptions invocation failed: {error.GetType().Name}: {error.Message}");
            return false;
        }
    }

    private static MethodInfo? Resolve()
    {
        if (_resolved)
        {
            return _showOptions;
        }

        lock (Sync)
        {
            if (_resolved)
            {
                return _showOptions;
            }

            _showOptions = PlayerLineOptionsSignature.Select(
                typeof(PlayerLineController).GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            _resolved = true;
            if (_showOptions is not null)
            {
                LilithModPlugin.PluginLog.LogInfo(
                    $"PlayerLineController.ShowOptions compatibility selected: parameters={_showOptions.GetParameters().Length}");
            }
            return _showOptions;
        }
    }

    private static void LogFailure(string message)
    {
        if (_failureLogged)
        {
            return;
        }
        _failureLogged = true;
        LilithModPlugin.PluginLog.LogWarning($"PlayerLineController.ShowOptions compatibility unavailable: {message}");
    }
}
