using MystiaStewardCompanion.LocalApi;

namespace MystiaStewardCompanion.Save;

internal static class RuntimeOrderPreparationService
{
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
                AddFailure(
                    result,
                    "自动取酒",
                    $"已选择 {request.BeverageName}，但尚未找到稳定的送餐盘写入入口；本次未修改库存。");
                if (request.StopOnError) return Finish(result);
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
