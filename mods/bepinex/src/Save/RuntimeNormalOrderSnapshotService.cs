using MystiaStewardCompanion.Core;

namespace MystiaStewardCompanion.Save;

public sealed class RuntimeNormalOrderSnapshotService
{
    private const string OrderControllerTypeName = "Night.UI.HUD.Ordering.OrderController";

    private readonly DataRepository _repository;

    public RuntimeNormalOrderSnapshotService(DataRepository repository)
    {
        _repository = repository;
    }

    public NormalBusinessContext Load()
    {
        var orders = new List<NormalBusinessOrder>();
        var errors = new List<string>();
        var source = new List<string>();

        var orderControllerType = RuntimeReflectionUtility.FindType(OrderControllerTypeName);
        if (orderControllerType == null)
        {
            return new NormalBusinessContext
            {
                Source = "OrderController=type-missing",
                Error = "普通客订单控制器未加载，可能不在经营场景。",
            };
        }

        try
        {
            var rawOrders = RuntimeReflectionUtility
                .EnumerateObjects(RuntimeReflectionUtility.InvokeStaticMethod(orderControllerType, "GetShowInUIOrders"))
                .ToList();
            source.Add($"OrderController={rawOrders.Count}");

            foreach (var rawOrder in rawOrders)
            {
                if (!IsNormalOrder(rawOrder)) continue;
                var order = ReadNormalOrder(rawOrder, "OrderController");
                if (order != null) orders.Add(order);
            }
        }
        catch (Exception ex)
        {
            source.Add("OrderController=err");
            errors.Add(ex.Message);
        }

        var deduplicated = orders
            .GroupBy(order => $"{order.DeskCode}|{order.GuestName}|{order.FoodId}|{order.BeverageId}", StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(order => order.DeskCode)
            .ThenBy(order => order.GuestName, StringComparer.Ordinal)
            .ToList();
        source.Add($"normalOrders={deduplicated.Count}");

        return new NormalBusinessContext
        {
            Orders = deduplicated,
            Source = string.Join("; ", source),
            Error = errors.Count == 0 ? null : string.Join("; ", errors),
        };
    }

    private NormalBusinessOrder? ReadNormalOrder(object? order, string source)
    {
        if (order == null) return null;

        var requestFood = SafeGet(order, "RequestFood");
        var requestBeverage = SafeGet(order, "RequestBeverage");
        var foodId = ReadSellableId(requestFood, SafeGet(order, "foodRequest"));
        var beverageId = ReadSellableId(requestBeverage, SafeGet(order, "beverageRequest"));
        var recipe = _repository.Recipes.FirstOrDefault(item => item.RecipeId == foodId || item.Id == foodId);
        var beverage = _repository.Beverages.FirstOrDefault(item => item.Id == beverageId);

        return new NormalBusinessOrder
        {
            DeskCode = RuntimeReflectionUtility.ToInt(SafeGet(order, "DeskCode"), -1),
            GuestName = ReadTextLikeValue(SafeGet(order, "Guest")),
            FoodId = foodId,
            FoodName = recipe?.Name ?? ReadTextLikeValue(requestFood),
            BeverageId = beverageId,
            BeverageName = beverage?.Name ?? ReadTextLikeValue(requestBeverage),
            HasServedFood = SafeGet(order, "ServFood") != null || SafeGet(order, "ServedFoodInAir") != null,
            HasServedBeverage = SafeGet(order, "ServBeverage") != null || SafeGet(order, "ServedBeverageInAir") != null,
            IsFulfilled = RuntimeReflectionUtility.ToBool(SafeGet(order, "IsFullfilled")),
            Source = source,
        };
    }

    private static bool IsNormalOrder(object? order)
    {
        if (order == null) return false;
        var typeName = order.GetType().Name;
        if (typeName.IndexOf("NormalOrder", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        var orderType = SafeGet(order, "Type")?.ToString();
        return string.Equals(orderType, "Normal", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadSellableId(object? sellable, object? fallback)
    {
        foreach (var member in new[] { "Id", "ID", "id" })
        {
            var value = SafeGet(sellable, member);
            var parsed = RuntimeReflectionUtility.ToInt(value, int.MinValue);
            if (parsed != int.MinValue) return parsed;
        }

        return RuntimeReflectionUtility.ToInt(fallback, -1);
    }

    private static object? SafeGet(object? value, string member)
    {
        try
        {
            return RuntimeReflectionUtility.GetMemberValue(value, member);
        }
        catch
        {
            return null;
        }
    }

    private static string ReadTextLikeValue(object? value)
    {
        if (value == null) return "";

        foreach (var member in new[] { "Name", "DisplayName", "Text", "Value" })
        {
            var memberValue = SafeGet(value, member);
            var text = memberValue?.ToString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        try
        {
            var text = value.ToString();
            if (!string.IsNullOrWhiteSpace(text) && !text.StartsWith(value.GetType().FullName ?? value.GetType().Name, StringComparison.Ordinal))
            {
                return text;
            }
        }
        catch
        {
            // Ignore conversion failures.
        }

        return "";
    }
}
