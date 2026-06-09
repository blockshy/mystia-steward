using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace MystiaStewardCompanion.Save;

internal static class RuntimeUiPinningService
{
    private const string CookingSelectionPanelTypeName = "NightScene.UI.CookingUtility.WorkSceneCookingSelectionPannel";
    private const string StoragePanelTypeName = "NightScene.UI.CookingUtility.WorkSceneStoragePannel";

    private static readonly object SyncRoot = new();
    private static readonly HashSet<string> PatchedMethods = new(StringComparer.Ordinal);
    private static readonly int[] EmptyIngredientIds = Array.Empty<int>();

    private static Harmony? _harmony;
    private static ManualLogSource? _log;
    private static bool _enabled;
    private static int _recipeId = -1;
    private static int _beverageId = -1;
    private static int[] _ingredientIds = EmptyIngredientIds;
    private static string _recipeName = "";
    private static string _beverageName = "";
    private static string _status = "not attached";
    private static string _lastAction = "";

    public static string Status
    {
        get
        {
            lock (SyncRoot)
            {
                return $"{_status}; target=recipe:{_recipeId}/{_recipeName}, beverage:{_beverageId}/{_beverageName}, ingredients:{string.Join(",", _ingredientIds)}; last={_lastAction}";
            }
        }
    }

    public static void Attach(ManualLogSource log)
    {
        _log = log;
        try
        {
            _harmony ??= new Harmony("com.tyukki.mystia-steward-companion.runtime-ui-pinning");
            var patchedNow = new List<string>();
            var missing = new List<string>();

            PatchMethod(_harmony, CookingSelectionPanelTypeName, "UpdateRecipeField", 0, nameof(OnRecipeFieldUpdated), patchedNow, missing);
            PatchMethod(_harmony, CookingSelectionPanelTypeName, "UpdateIngField", 0, nameof(OnIngredientFieldUpdated), patchedNow, missing);
            PatchMethod(_harmony, StoragePanelTypeName, "UpdateBevField", 0, nameof(OnBeverageFieldUpdated), patchedNow, missing);

            lock (SyncRoot)
            {
                _status = PatchedMethods.Count == 0
                    ? $"waiting: {string.Join(", ", missing.Take(4))}"
                    : $"patched={PatchedMethods.Count}";
            }

            if (patchedNow.Count > 0)
            {
                log.LogInfo($"Runtime UI pinning patched: {string.Join(", ", patchedNow)}.");
            }
            else if (PatchedMethods.Count == 0)
            {
                log.LogWarning($"Runtime UI pinning waiting for game types: {string.Join(", ", missing.Take(4))}.");
            }
        }
        catch (Exception ex)
        {
            lock (SyncRoot)
            {
                _status = $"error: {ex.Message}";
            }

            log.LogWarning($"Runtime UI pinning attach failed: {ex.Message}");
        }
    }

    public static string UpdateTarget(bool enabled, int recipeId, int beverageId, IEnumerable<int> ingredientIds, string recipeName, string beverageName)
    {
        lock (SyncRoot)
        {
            _enabled = enabled;
            _recipeId = enabled ? recipeId : -1;
            _beverageId = enabled ? beverageId : -1;
            _ingredientIds = enabled
                ? ingredientIds.Where(id => id >= 0).Distinct().Take(12).ToArray()
                : EmptyIngredientIds;
            _recipeName = enabled ? recipeName.Trim() : "";
            _beverageName = enabled ? beverageName.Trim() : "";
            _lastAction = enabled ? "target updated" : "disabled";
            return Status;
        }
    }

    private static void PatchMethod(
        Harmony harmony,
        string typeName,
        string methodName,
        int parameterCount,
        string postfixName,
        ICollection<string> patchedNow,
        ICollection<string> missing)
    {
        var key = $"{typeName}.{methodName}/{parameterCount}";
        lock (SyncRoot)
        {
            if (PatchedMethods.Contains(key)) return;
        }

        var type = FindType(typeName);
        var target = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == methodName && method.GetParameters().Length == parameterCount);
        var postfix = typeof(RuntimeUiPinningService).GetMethod(postfixName, BindingFlags.NonPublic | BindingFlags.Static);
        if (target == null || postfix == null)
        {
            missing.Add(key);
            return;
        }

        harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        lock (SyncRoot)
        {
            PatchedMethods.Add(key);
        }

        patchedNow.Add(key);
    }

    private static void OnRecipeFieldUpdated(object __instance)
    {
        if (!ReadTarget(out var recipeId, out _, out _, out _, out _)) return;
        if (recipeId < 0) return;
        TryMoveFirst(__instance, "RecipeInstances", item => ReadObjectId(item) == recipeId, "recipe");
    }

    private static void OnIngredientFieldUpdated(object __instance)
    {
        if (!ReadTarget(out _, out _, out var ingredientIds, out _, out _)) return;
        if (ingredientIds.Length == 0) return;

        foreach (var fieldName in new[]
        {
            "Ingredient_MeatInstances",
            "Ingredient_OtherInstances",
            "Ingredient_SeaFoodInstances",
            "Ingredient_VeggiesInsatance",
        })
        {
            TrySortPinnedIngredients(__instance, fieldName, ingredientIds);
        }
    }

    private static void OnBeverageFieldUpdated(object __instance)
    {
        if (!ReadTarget(out _, out var beverageId, out _, out _, out _)) return;
        if (beverageId < 0) return;
        TryMoveFirst(__instance, "Beverages", item => ReadObjectId(ReadPairKey(item) ?? item) == beverageId, "beverage");
    }

    private static bool ReadTarget(
        out int recipeId,
        out int beverageId,
        out int[] ingredientIds,
        out string recipeName,
        out string beverageName)
    {
        lock (SyncRoot)
        {
            recipeId = _recipeId;
            beverageId = _beverageId;
            ingredientIds = _ingredientIds.ToArray();
            recipeName = _recipeName;
            beverageName = _beverageName;
            return _enabled;
        }
    }

    private static void TrySortPinnedIngredients(object target, string fieldName, int[] ingredientIds)
    {
        var list = ReadMember(target, fieldName);
        if (list == null) return;

        var priority = ingredientIds.Select((id, index) => new { id, index })
            .ToDictionary(item => item.id, item => item.index);
        ReorderList(list, item =>
        {
            var key = ReadPairKey(item) ?? item;
            var id = ReadObjectId(key);
            return priority.TryGetValue(id, out var index) ? index : int.MaxValue;
        }, $"ingredient:{fieldName}");
    }

    private static void TryMoveFirst(object target, string fieldName, Func<object, bool> match, string label)
    {
        var list = ReadMember(target, fieldName);
        if (list == null)
        {
            NoteAction($"{label} list missing");
            return;
        }

        ReorderList(list, item => match(item) ? 0 : 1, label);
    }

    private static void ReorderList(object list, Func<object, int> priority, string label)
    {
        try
        {
            var count = GetCount(list);
            if (count <= 1) return;

            var items = new List<object>();
            for (var index = 0; index < count; index++)
            {
                var item = GetItem(list, index);
                if (item != null) items.Add(item);
            }

            var reordered = items
                .Select((item, index) => new { item, index, priority = priority(item) })
                .OrderBy(item => item.priority)
                .ThenBy(item => item.index)
                .Select(item => item.item)
                .ToList();

            if (reordered.SequenceEqual(items)) return;
            if (!ClearList(list)) return;
            foreach (var item in reordered)
            {
                if (!AddListItem(list, item)) return;
            }

            NoteAction($"{label} reordered");
        }
        catch (Exception ex)
        {
            NoteAction($"{label} reorder failed: {ex.Message}");
        }
    }

    private static int GetCount(object list)
    {
        if (list is ICollection collection) return collection.Count;
        return ToInt(TryInvokeInstanceValue(list, "get_Count") ?? ReadMember(list, "Count"));
    }

    private static object? GetItem(object list, int index)
    {
        if (list is IList ilist) return ilist[index];
        return TryInvokeInstanceValue(list, "get_Item", new object?[] { index });
    }

    private static bool ClearList(object list)
    {
        if (list is IList ilist)
        {
            ilist.Clear();
            return true;
        }

        return TryInvokeInstance(list, "Clear", Array.Empty<object?>());
    }

    private static bool AddListItem(object list, object item)
    {
        if (list is IList ilist)
        {
            ilist.Add(item);
            return true;
        }

        return TryInvokeInstance(list, "Add", new object?[] { item });
    }

    private static object? ReadPairKey(object item)
    {
        return ReadMember(item, "Key") ?? ReadMember(item, "key");
    }

    private static int ReadObjectId(object? value)
    {
        if (value == null) return -1;
        return ToInt(TryInvokeInstanceValue(value, "get_id")
            ?? TryInvokeInstanceValue(value, "get_Id")
            ?? TryInvokeInstanceValue(value, "get_foodID")
            ?? ReadMember(value, "id")
            ?? ReadMember(value, "Id")
            ?? ReadMember(value, "foodID"));
    }

    private static void NoteAction(string action)
    {
        lock (SyncRoot)
        {
            _lastAction = action;
        }
    }

    private static object? TryInvokeInstanceValue(object target, string methodName)
    {
        return TryInvokeInstanceValue(target, methodName, Array.Empty<object?>());
    }

    private static object? TryInvokeInstanceValue(object target, string methodName, object?[] args)
    {
        try
        {
            var value = InvokeInstance(target, methodName, args);
            return value == Missing.Value ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryInvokeInstance(object target, string methodName, object?[] args)
    {
        try
        {
            var method = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                    && CanUseParameters(candidate.GetParameters(), args));
            if (method == null) return false;
            method.Invoke(target, args);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? InvokeInstance(object target, string methodName, object?[] args)
    {
        var method = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                && CanUseParameters(candidate.GetParameters(), args));
        if (method == null) return Missing.Value;
        var result = method.Invoke(target, args);
        return method.ReturnType == typeof(void) ? Missing.Value : result;
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
        }

        return null;
    }

    private static IEnumerable<string> BuildFieldNameCandidates(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) yield break;

        yield return name;
        yield return $"m_{name}";
        yield return $"_{name}";
        yield return $"<{name}>k__BackingField";

        var camelName = char.ToLowerInvariant(name[0]) + name[1..];
        if (!string.Equals(camelName, name, StringComparison.Ordinal))
        {
            yield return camelName;
            yield return $"m_{camelName}";
            yield return $"_{camelName}";
            yield return $"<{camelName}>k__BackingField";
        }
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

    private static int ToInt(object? value)
    {
        if (value == null || value == Missing.Value) return -1;
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
