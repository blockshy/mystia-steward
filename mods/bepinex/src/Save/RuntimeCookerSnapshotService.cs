using System.Collections;
using System.Reflection;
using MystiaStewardCompanion.Core;

namespace MystiaStewardCompanion.Save;

internal static class RuntimeCookerSnapshotService
{
    private const string CookSystemManagerTypeName = "NightScene.CookingUtility.CookSystemManager";

    private static readonly Dictionary<int, string> CookerTypeNames = new()
    {
        [1] = "煮锅",
        [2] = "烧烤架",
        [3] = "油锅",
        [4] = "蒸锅",
        [5] = "料理台",
    };

    public static void ApplyTo(RecommendationState state)
    {
        foreach (var cooker in ReadPlacedCookers())
        {
            state.PlacedCookers.Add(cooker);
            foreach (var typeId in cooker.TypeIds)
            {
                state.PlacedCookerTypeIds.Add(typeId);
            }
        }
    }

    private static IEnumerable<PlacedCookerInfo> ReadPlacedCookers()
    {
        object? cookSystem;
        try
        {
            cookSystem = GetSingletonInstance(CookSystemManagerTypeName);
        }
        catch
        {
            yield break;
        }

        if (cookSystem == null) yield break;

        object? controllers;
        try
        {
            controllers = InvokeInstance(cookSystem, "get_AllCookerControllers", Array.Empty<object?>());
        }
        catch
        {
            yield break;
        }

        var index = 0;
        foreach (var controller in ReadObjectEnumerable(controllers))
        {
            var controllerIndex = index++;
            object? cooker = null;
            var isOpen = false;

            try
            {
                cooker = InvokeInstance(controller, "get_Cooker", Array.Empty<object?>());
                isOpen = ReadBool(TryInvokeInstanceValue(controller, "get_CouldCookerOpen"));
            }
            catch
            {
                // Keep scanning other controllers; a single stale controller should not hide all cookers.
            }

            if (cooker == null) continue;

            var typeIds = ReadCookerTypeIds(cooker).Distinct().OrderBy(id => id).ToList();
            if (typeIds.Count == 0) continue;

            var typeNames = typeIds.Select(ResolveCookerTypeName).Where(name => name.Length > 0).Distinct().ToList();
            yield return new PlacedCookerInfo
            {
                ControllerIndex = controllerIndex,
                TypeIds = typeIds,
                TypeNames = typeNames,
                Name = typeNames.Count > 0 ? string.Join("/", typeNames) : cooker.GetType().Name,
                IsOpen = isOpen,
                Source = "CookSystemManager",
            };
        }
    }

    private static List<int> ReadCookerTypeIds(object cooker)
    {
        try
        {
            var cookerTypes = InvokeInstance(cooker, "get_AllAvailableCookerType", Array.Empty<object?>());
            return ReadIntEnumerable(cookerTypes).Where(id => id >= 0).ToList();
        }
        catch
        {
            return new List<int>();
        }
    }

    private static string ResolveCookerTypeName(int typeId)
    {
        return CookerTypeNames.TryGetValue(typeId, out var name) ? name : $"#{typeId}";
    }

    private static object? TryInvokeInstanceValue(object target, string methodName)
    {
        try
        {
            return InvokeInstance(target, methodName, Array.Empty<object?>());
        }
        catch
        {
            return null;
        }
    }

    private static object? GetSingletonInstance(string typeName)
    {
        var type = FindType(typeName);
        if (type == null) return null;

        var property = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (property != null) return property.GetValue(null);

        var method = type.GetMethod("get_Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        return method?.Invoke(null, Array.Empty<object?>());
    }

    private static object? InvokeInstance(object target, string methodName, object?[] args)
    {
        var method = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                && CanUseParameters(candidate.GetParameters(), args));
        return method == null ? null : method.Invoke(target, args);
    }

    private static bool CanUseParameters(ParameterInfo[] parameters, object?[] args)
    {
        if (parameters.Length != args.Length) return false;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (args[i] == null) continue;
            if (!parameters[i].ParameterType.IsInstanceOfType(args[i])) return false;
        }

        return true;
    }

    private static Type? FindType(string fullName)
    {
        var direct = Type.GetType(fullName, false);
        if (direct != null) return direct;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type;
            try
            {
                type = assembly.GetType(fullName, false);
            }
            catch
            {
                continue;
            }

            if (type != null) return type;
        }

        return null;
    }

    private static IEnumerable<object> ReadObjectEnumerable(object? value)
    {
        if (value == null || value is string) yield break;
        if (value is not IEnumerable enumerable) yield break;

        foreach (var item in enumerable)
        {
            if (item != null) yield return item;
        }
    }

    private static IEnumerable<int> ReadIntEnumerable(object? value)
    {
        if (value == null || value is string) yield break;
        if (value is not IEnumerable enumerable) yield break;

        foreach (var item in enumerable)
        {
            yield return ToInt(item);
        }
    }

    private static bool ReadBool(object? value)
    {
        if (value is bool boolean) return boolean;
        if (value is IConvertible convertible)
        {
            try
            {
                return convertible.ToInt32(null) != 0;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static int ToInt(object? value)
    {
        if (value == null) return -1;
        if (value is int number) return number;
        if (value is Enum) return Convert.ToInt32(value);
        if (value is IConvertible convertible)
        {
            try
            {
                return convertible.ToInt32(null);
            }
            catch
            {
                return -1;
            }
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : -1;
    }
}
