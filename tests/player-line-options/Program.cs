using System;
using System.Collections.Generic;
using System.Reflection;
using LilithMod;

MethodInfo? preferred = PlayerLineOptionsSignature.Select(typeof(NewSignature).GetMethods());
if (preferred?.GetParameters().Length != 3)
{
    throw new InvalidOperationException("The three-parameter ShowOptions overload must be preferred.");
}

MethodInfo? fallback = PlayerLineOptionsSignature.Select(typeof(OldSignature).GetMethods());
if (fallback?.GetParameters().Length != 2)
{
    throw new InvalidOperationException("The legacy two-parameter ShowOptions overload must remain supported.");
}

if (PlayerLineOptionsSignature.Select(typeof(IncompatibleSignature).GetMethods()) is not null)
{
    throw new InvalidOperationException("An incompatible ShowOptions overload was accepted.");
}

Console.WriteLine("PlayerLine ShowOptions signature tests passed.");

internal sealed class NewSignature
{
    public void ShowOptions(IReadOnlyList<string> options, Action<int> selected, Func<bool>? validWhile)
    {
    }
}

internal sealed class OldSignature
{
    public void ShowOptions(IReadOnlyList<string> options, Action<int> selected)
    {
    }
}

internal sealed class IncompatibleSignature
{
    public void ShowOptions(IReadOnlyList<string> options, string selected)
    {
    }
}
