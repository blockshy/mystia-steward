using MystiaStewardCompanion.LocalApi;
using System.Reflection;

namespace MystiaStewardCompanion.Save;

internal static class RuntimeOrderPreparationService
{
    private const string DataBaseCoreTypeName = "GameData.Core.Collections.DataBaseCore";
    private const string IzakayaTrayTypeName = "GameData.RunTime.NightSceneUtility.IzakayaTray";
    private const string RuntimeStorageTypeName = "GameData.RunTime.Common.RunTimeStorage";

    public static OrderPreparationResult Prepare(OrderPreparationRequest request)
    {
        var result = new OrderPreparationResult
        {
            Order = new OrderPreparationOrder
            {
                DeskCode = request.DeskCode,
                GuestId = request.GuestId,
                GuestName = request.GuestName,
                FoodTag = request.FoodTag,
                BeverageTag = request.BeverageTag,
            },
            RecipeId = request.RecipeId,
            RecipeName = request.RecipeName,
            BeverageId = request.BeverageId,
            BeverageName = request.BeverageName,
        };

        if (request.FavoritesOnly)
        {
            if (request.AutoStartCooking && !request.RecipeFavorite)
            {
                return Fail(result, "收藏限定已开启，但当前订单没有匹配的收藏料理。");
            }

            if (request.AutoTakeBeverage && !request.BeverageFavorite)
            {
                return Fail(result, "收藏限定已开启，但当前订单没有匹配的收藏酒水。");
            }
        }

        result.Steps.Add(new OrderPreparationStep
        {
            Name = "选择订单",
            Ok = true,
            Message = $"桌 {request.DeskCode + 1} · {request.GuestName} · 料理 {request.FoodTag} · 酒水 {request.BeverageTag}",
        });

        if (request.AutoTakeBeverage)
        {
            if (request.BeverageId < 0)
            {
                AddFailure(result, "自动取酒", "没有可用的推荐酒水。");
                if (request.StopOnError) return Finish(result);
            }
            else
            {
                var beverageResult = TryTakeBeverageToTray(request.BeverageId, request.BeverageName);
                if (beverageResult.Ok)
                {
                    result.Steps.Add(new OrderPreparationStep
                    {
                        Name = "自动取酒",
                        Ok = true,
                        Message = beverageResult.Message,
                    });
                }
                else
                {
                    AddFailure(result, "自动取酒", beverageResult.Message);
                    if (request.StopOnError) return Finish(result);
                }
            }
        }
        else
        {
            AddSkipped(result, "自动取酒", "设置已关闭。");
        }

        if (request.AutoStartCooking)
        {
            if (request.RecipeId < 0)
            {
                AddFailure(result, "自动开始料理", "没有可用的推荐料理。");
                if (request.StopOnError) return Finish(result);
            }
            else
            {
                var extras = request.ExtraIngredientIds.Count == 0
                    ? "不加料"
                    : string.Join(",", request.ExtraIngredientIds);
                AddFailure(
                    result,
                    "自动开始料理",
                    $"已选择 {request.RecipeName}（加料：{extras}），但尚未找到稳定的厨具启动入口。");
                if (request.StopOnError) return Finish(result);
            }
        }
        else
        {
            AddSkipped(result, "自动开始料理", "设置已关闭。");
        }

        if (request.AutoCollectCooking)
        {
            AddFailure(result, "自动收取料理", "料理启动入口尚未接入，无法安全收取到送餐盘。");
            if (request.StopOnError) return Finish(result);
        }
        else
        {
            AddSkipped(result, "自动收取料理", "设置已关闭。");
        }

        return Finish(result);
    }

    private static (bool Ok, string Message) TryTakeBeverageToTray(int beverageId, string beverageName)
    {
        var currentQuantity = GetBeverageQuantity(beverageId);
        if (currentQuantity == 0)
        {
            return (false, $"{beverageName} 当前库存为 0，无法放入送餐盘。");
        }

        var sellable = InvokeStatic(DataBaseCoreTypeName, "AsNewBeverage", new object?[] { beverageId });
        if (sellable == null)
        {
            return (false, $"无法从游戏数据库创建酒水对象：{beverageName} #{beverageId}。");
        }

        var tray = GetSingletonInstance(IzakayaTrayTypeName);
        if (tray == null)
        {
            return (false, "当前送餐盘对象不可用，请确认已进入夜晚经营页面。");
        }

        var isFull = InvokeInstance(tray, "get_IsTrayFull", Array.Empty<object?>());
        if (isFull is bool isTrayFull && isTrayFull)
        {
            return (false, "送餐盘已满，无法继续取酒。");
        }

        InvokeInstance(tray, "Receive", new[] { sellable });
        if (currentQuantity > 0)
        {
            InvokeRuntimeStorageOut("BeverageOut", beverageId);
        }

        var quantityText = currentQuantity < 0 ? "无限库存" : $"剩余 {Math.Max(0, currentQuantity - 1)}";
        return (true, $"{beverageName} 已放入送餐盘（{quantityText}）。");
    }

    private static int GetBeverageQuantity(int beverageId)
    {
        var value = InvokeStatic(RuntimeStorageTypeName, "GetBeverageCountById", new object?[] { beverageId });
        return ToInt(value);
    }

    private static void InvokeRuntimeStorageOut(string methodName, int itemId)
    {
        var type = FindType(RuntimeStorageTypeName)
            ?? throw new InvalidOperationException("RunTimeStorage type is not loaded.");
        var method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal)) return false;
                var parameters = candidate.GetParameters();
                return parameters.Length >= 1
                    && parameters[0].ParameterType == typeof(int);
            })
            ?? throw new MissingMethodException(RuntimeStorageTypeName, methodName);
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        args[0] = itemId;
        for (var i = 1; i < parameters.Length; i++)
        {
            args[i] = GetDefaultValue(parameters[i].ParameterType);
        }

        method.Invoke(null, args);
    }

    private static object? GetSingletonInstance(string typeName)
    {
        var type = FindType(typeName)
            ?? throw new InvalidOperationException($"{typeName} type is not loaded.");
        var property = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (property != null) return property.GetValue(null);

        var method = type.GetMethod("get_Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        return method?.Invoke(null, Array.Empty<object?>());
    }

    private static object? InvokeStatic(string typeName, string methodName, object?[] args)
    {
        var type = FindType(typeName)
            ?? throw new InvalidOperationException($"{typeName} type is not loaded.");
        var method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                && CanUseParameters(candidate.GetParameters(), args))
            ?? throw new MissingMethodException(typeName, methodName);
        return method.Invoke(null, args);
    }

    private static object? InvokeInstance(object target, string methodName, object?[] args)
    {
        var method = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                && CanUseParameters(candidate.GetParameters(), args))
            ?? throw new MissingMethodException(target.GetType().FullName, methodName);
        return method.Invoke(target, args);
    }

    private static Type? FindType(string fullName)
    {
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

    private static bool CanUseParameters(ParameterInfo[] parameters, object?[] args)
    {
        if (parameters.Length != args.Length) return false;
        for (var i = 0; i < parameters.Length; i++)
        {
            var arg = args[i];
            if (arg == null) continue;
            if (!parameters[i].ParameterType.IsInstanceOfType(arg)) return false;
        }

        return true;
    }

    private static object? GetDefaultValue(Type type)
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private static int ToInt(object? value)
    {
        if (value == null) return 0;
        if (value is int number) return number;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    }

    private static OrderPreparationResult Fail(OrderPreparationResult result, string error)
    {
        result.Error = error;
        result.Ok = false;
        result.Prepared = false;
        result.Steps.Add(new OrderPreparationStep
        {
            Name = "准备校验",
            Ok = false,
            Message = error,
        });
        return result;
    }

    private static OrderPreparationResult Finish(OrderPreparationResult result)
    {
        result.Prepared = result.Steps.Any(step => step.Ok && !step.Skipped && step.Name != "选择订单");
        result.Ok = result.Error == null && result.Steps.All(step => step.Ok || step.Skipped);
        if (!result.Ok && result.Error == null)
        {
            result.Error = result.Steps.FirstOrDefault(step => !step.Ok && !step.Skipped)?.Message;
        }

        return result;
    }

    private static void AddFailure(OrderPreparationResult result, string name, string message)
    {
        result.Steps.Add(new OrderPreparationStep
        {
            Name = name,
            Ok = false,
            Message = message,
        });
    }

    private static void AddSkipped(OrderPreparationResult result, string name, string message)
    {
        result.Steps.Add(new OrderPreparationStep
        {
            Name = name,
            Ok = true,
            Skipped = true,
            Message = message,
        });
    }
}
