using System.Collections;
using System.Runtime.CompilerServices;
using System.Reflection;
using MystiaStewardCompanion.Core;

namespace MystiaStewardCompanion.Save;

internal static class RuntimeCookerSnapshotService
{
    private const string CookSystemManagerTypeName = "NightScene.CookingUtility.CookSystemManager";
    private const string RunTimeStorageTypeName = "GameData.RunTime.Common.RunTimeStorage";

    private static readonly object SyncRoot = new();

    private static readonly Dictionary<int, string> CookerTypeNames = new()
    {
        [1] = "煮锅",
        [2] = "烧烤架",
        [3] = "油锅",
        [4] = "蒸锅",
        [5] = "料理台",
    };

    public static string Status
    {
        get
        {
            lock (SyncRoot)
            {
                return _status;
            }
        }
    }

    private static string _status = "not read";

    public static void ApplyTo(RecommendationState state)
    {
        var cookers = ReadPlacedCookers();
        state.PlacedCookerStatus = Status;
        foreach (var cooker in cookers)
        {
            state.PlacedCookers.Add(cooker);
            foreach (var typeId in cooker.TypeIds)
            {
                state.PlacedCookerTypeIds.Add(typeId);
            }
        }
    }

    private static List<PlacedCookerInfo> ReadPlacedCookers()
    {
        var managerResult = ReadCookSystemCookers(out var managerStatus);
        if (managerResult.Count > 0)
        {
            SetStatus(managerStatus);
            return managerResult;
        }

        var storageResult = ReadStorageCookers(out var storageStatus);
        SetStatus(storageResult.Count > 0
            ? $"{storageStatus}; fallback={managerStatus}"
            : $"{managerStatus}; {storageStatus}");
        return storageResult;
    }

    private static List<PlacedCookerInfo> ReadCookSystemCookers(out string status)
    {
        var result = new List<PlacedCookerInfo>();
        status = "manager not read";

        object? cookSystem;
        try
        {
            cookSystem = GetSingletonInstance(CookSystemManagerTypeName);
        }
        catch (Exception ex)
        {
            status = $"manager error: {ex.Message}";
            return result;
        }

        if (cookSystem == null)
        {
            status = "manager missing";
            return result;
        }

        var allCookers = ReadMember(cookSystem, "AllCookers");
        if (allCookers == null)
        {
            status = "manager cookers not initialized";
            return result;
        }

        var controllerItems = ReadCookerControllers(allCookers).ToList();
        var index = 0;
        foreach (var controller in controllerItems)
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

            var typeIds = ReadCookerTypeIds(cooker).Where(id => id > 0).Distinct().OrderBy(id => id).ToList();
            if (typeIds.Count == 0) continue;

            var typeNames = typeIds.Select(ResolveCookerTypeName).Where(name => name.Length > 0).Distinct().ToList();
            result.Add(new PlacedCookerInfo
            {
                ControllerIndex = controllerIndex,
                TypeIds = typeIds,
                TypeNames = typeNames,
                Name = typeNames.Count > 0 ? string.Join("/", typeNames) : cooker.GetType().Name,
                IsOpen = isOpen,
                Source = "CookSystemManager",
            });
        }

        var typeSummary = result
            .SelectMany(cooker => cooker.TypeNames)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .ToArray();
        status = result.Count == 0
            ? $"manager empty; controllers={controllerItems.Count}; allCookersType={allCookers.GetType().FullName}"
            : $"ok; controllers={controllerItems.Count}; cookers={result.Count}; types={string.Join("/", typeSummary)}";
        return result;
    }

    private static List<PlacedCookerInfo> ReadStorageCookers(out string status)
    {
        var result = new List<PlacedCookerInfo>();
        status = "storage not read";

        object? cookerPairs;
        try
        {
            cookerPairs = InvokeStatic(RunTimeStorageTypeName, "GetAllCookers", Array.Empty<object?>());
        }
        catch (Exception ex)
        {
            status = $"storage error: {ex.InnerException?.Message ?? ex.Message}";
            return result;
        }

        var index = 0;
        foreach (var pair in ReadObjectEnumerable(cookerPairs))
        {
            var cooker = ReadMember(pair, "Key")
                ?? ReadMember(pair, "key")
                ?? ReadMember(pair, "Item1");
            var count = ToInt(ReadMember(pair, "Value")
                ?? ReadMember(pair, "value")
                ?? ReadMember(pair, "Item2"));
            if (cooker == null || count <= 0) continue;

            var typeIds = ReadCookerTypeIds(cooker).Where(id => id > 0).Distinct().OrderBy(id => id).ToList();
            if (typeIds.Count == 0) continue;

            var typeNames = typeIds.Select(ResolveCookerTypeName).Where(name => name.Length > 0).Distinct().ToList();
            result.Add(new PlacedCookerInfo
            {
                ControllerIndex = index++,
                TypeIds = typeIds,
                TypeNames = typeNames,
                Name = typeNames.Count > 0 ? string.Join("/", typeNames) : cooker.GetType().Name,
                IsOpen = true,
                Source = "RunTimeStorage",
            });
        }

        var typeSummary = result
            .SelectMany(cooker => cooker.TypeNames)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .ToArray();
        status = result.Count == 0
            ? $"storage empty; sourceType={cookerPairs?.GetType().FullName ?? "null"}"
            : $"storage ok; cookers={result.Count}; types={string.Join("/", typeSummary)}";
        return result;
    }

    private static IEnumerable<object> ReadCookerControllers(object? allCookers)
    {
        var dictionaryValues = ReadDictionaryValues(allCookers).Where(value => value != null).Cast<object>().ToList();
        var seen = new HashSet<nint>();
        foreach (var controller in dictionaryValues)
        {
            if (controller == null) continue;
            if (!seen.Add(ReadObjectPointer(controller))) continue;
            yield return controller;
        }
    }

    private static List<int> ReadCookerTypeIds(object cooker)
    {
        try
        {
            var directType = ToInt(ReadMember(cooker, "type"));
            if (directType > 0) return new List<int> { directType };
        }
        catch
        {
            // Fall back to the public enumerable below only if the direct serialized type is unavailable.
        }

        var cookerTypes = TryInvokeInstanceValue(cooker, "get_AllAvailableCookerType");
        return ReadIntEnumerable(cookerTypes).Where(id => id > 0).ToList();
    }

    private static string ResolveCookerTypeName(int typeId)
    {
        return CookerTypeNames.TryGetValue(typeId, out var name) ? name : $"#{typeId}";
    }

    private static object? TryInvokeInstanceValue(object target, string methodName)
    {
        return TryInvokeInstanceValue(target, methodName, Array.Empty<object?>());
    }

    private static object? TryInvokeInstanceValue(object target, string methodName, object?[] args)
    {
        try
        {
            return InvokeInstance(target, methodName, args);
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

    private static object? InvokeStatic(string typeName, string methodName, object?[] args)
    {
        var type = FindType(typeName);
        if (type == null) return null;

        var method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                && CanUseParameters(candidate.GetParameters(), args));
        return method == null ? null : method.Invoke(null, args);
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

        var seen = new HashSet<nint>();
        foreach (var item in EnumerateManaged(value).Concat(EnumerateByIndexer(value)))
        {
            if (item == null) continue;
            if (!seen.Add(ReadObjectPointer(item))) continue;
            yield return NormalizeDictionaryItem(item) ?? item;
        }
    }

    private static IEnumerable<int> ReadIntEnumerable(object? value)
    {
        if (value == null || value is string) yield break;

        foreach (var item in EnumerateManaged(value).Concat(EnumerateByIndexer(value)))
        {
            yield return ToInt(item);
        }
    }

    private static IEnumerable<object?> ReadDictionaryValues(object? dictionary)
    {
        if (dictionary == null || dictionary is string) yield break;

        if (dictionary is IDictionary managedDictionary)
        {
            foreach (DictionaryEntry entry in managedDictionary)
            {
                yield return entry.Value;
            }

            yield break;
        }

        var entries = ReadMember(dictionary, "entries")
            ?? ReadMember(dictionary, "_entries")
            ?? ReadMember(dictionary, "m_Entries");
        var count = ToInt(ReadMember(dictionary, "count")
            ?? ReadMember(dictionary, "_count")
            ?? ReadMember(dictionary, "Count"));
        if (entries == null || count <= 0) yield break;

        var entryIndex = 0;
        foreach (var entry in EnumerateByIndexer(entries))
        {
            if (entryIndex++ >= Math.Min(count, 256)) yield break;
            if (entry == null) continue;

            var hashCode = ToInt(ReadMember(entry, "hashCode") ?? ReadMember(entry, "_hashCode"));
            if (hashCode < 0) continue;

            var value = ReadMember(entry, "value")
                ?? ReadMember(entry, "Value")
                ?? ReadMember(entry, "_value");
            if (value != null) yield return value;
        }

        foreach (var item in ReadObjectEnumerable(dictionary))
        {
            var value = NormalizeDictionaryItem(item);
            if (value != null) yield return value;
        }
    }

    private static IEnumerable<object?> EnumerateManaged(object value)
    {
        if (LooksLikeIl2CppObject(value)) yield break;
        if (value is not IEnumerable enumerable) yield break;

        foreach (var item in enumerable)
        {
            yield return item;
        }
    }

    private static IEnumerable<object?> EnumerateByIndexer(object value)
    {
        var count = ToInt(TryInvokeInstanceValue(value, "get_Count")
            ?? ReadMember(value, "Count")
            ?? ReadMember(value, "Length")
            ?? ReadMember(value, "_size"));
        if (count <= 0) yield break;

        for (var index = 0; index < Math.Min(count, 128); index++)
        {
            yield return TryInvokeInstanceValue(value, "get_Item", new object?[] { index });
        }
    }

    private static bool LooksLikeIl2CppObject(object value)
    {
        var type = value.GetType();
        var fullName = type.FullName ?? "";
        if (fullName.StartsWith("Il2Cpp", StringComparison.Ordinal)) return true;
        if (fullName.StartsWith("NightScene.", StringComparison.Ordinal)) return true;
        if (fullName.StartsWith("GameData.", StringComparison.Ordinal)) return true;
        return type.Assembly.GetName().Name?.Contains("Il2Cpp", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static object? NormalizeDictionaryItem(object item)
    {
        return ReadMember(item, "Value") ?? ReadMember(item, "value");
    }

    private static object? ReadMember(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            foreach (var fieldName in BuildFieldNameCandidates(name))
            {
                var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(target);
            }

            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (property != null) return property.GetValue(target);

            var pascalName = char.ToUpperInvariant(name[0]) + name[1..];
            if (!string.Equals(pascalName, name, StringComparison.Ordinal))
            {
                property = type.GetProperty(pascalName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property != null) return property.GetValue(target);
            }
        }

        return null;
    }

    private static IEnumerable<string> BuildFieldNameCandidates(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) yield break;

        yield return name;
        yield return $"<{name}>k__BackingField";
        yield return $"m_{name}";
        yield return $"_{name}";

        var camelName = char.ToLowerInvariant(name[0]) + name[1..];
        if (!string.Equals(camelName, name, StringComparison.Ordinal))
        {
            yield return camelName;
            yield return $"<{camelName}>k__BackingField";
            yield return $"m_{camelName}";
            yield return $"_{camelName}";
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

    private static nint ReadObjectPointer(object target)
    {
        var pointer = ReadMember(target, "Pointer") ?? ReadMember(target, "NativePointer") ?? ReadMember(target, "m_CachedPtr");
        if (pointer is IntPtr intPtr) return intPtr;
        if (pointer is nint native) return native;
        if (pointer is IConvertible convertible)
        {
            try
            {
                return new IntPtr(convertible.ToInt64(null));
            }
            catch
            {
                return new IntPtr(RuntimeHelpers.GetHashCode(target));
            }
        }

        return new IntPtr(RuntimeHelpers.GetHashCode(target));
    }

    private static void SetStatus(string status)
    {
        lock (SyncRoot)
        {
            _status = status;
        }
    }
}
