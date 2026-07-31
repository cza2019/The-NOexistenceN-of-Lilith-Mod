using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LilithMod;

internal static class PlayerLineOptionsSignature
{
    internal static MethodInfo? Select(IEnumerable<MethodInfo> candidates)
    {
        return candidates
            .Where(method => method.Name == "ShowOptions")
            .Where(method => method.ReturnType == typeof(void))
            .Where(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length is not (2 or 3))
                {
                    return false;
                }
                return parameters[0].ParameterType.Name.Contains("IReadOnlyList", StringComparison.Ordinal)
                    && parameters[1].ParameterType.Name.Contains("Action", StringComparison.Ordinal);
            })
            .OrderByDescending(method => method.GetParameters().Length)
            .FirstOrDefault();
    }
}
