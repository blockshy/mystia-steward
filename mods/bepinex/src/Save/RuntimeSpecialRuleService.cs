using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using MystiaStewardCompanion.Core;

namespace MystiaStewardCompanion.Save;

internal static class RuntimeSpecialRuleService
{
    private const string NightSceneDirectorTypeName = "NightScene.NightSceneDirector";
    private const string IncomeControllerKoishiTypeName = "NightScene.UI.HUDUtility.IncomeControllerKoishi";
    private const string IncomeControllerYuumaTypeName = "NightScene.UI.HUDUtility.IncomeControllerYuuma";
    private const string WackyChallengeMode = "Story_WackyCookingCompetition";
    private const string YuumaChallengeMode = "Story_BloodPondHell";

    private static readonly object SyncRoot = new();
    private static readonly HashSet<string> PatchedMethods = new(StringComparer.Ordinal);

    private static Harmony? _harmony;
    private static ManualLogSource? _log;
    private static string _koishiTag = "";
    private static DateTime? _koishiTagUpdatedAtUtc;
    private static string _yuumaTag1 = "";
    private static string _yuumaTag2 = "";
    private static DateTime? _yuumaTagUpdatedAtUtc;
    private static string _status = "not attached";

    public static string Status
    {
        get
        {
            lock (SyncRoot)
            {
                return $"{_status}; koishi={FormatCachedTags(_koishiTag)}; yuuma={FormatCachedTags(_yuumaTag1, _yuumaTag2)}";
            }
        }
    }

    public static void Attach(ManualLogSource log)
    {
        _log = log;
        try
        {
            _harmony ??= new Harmony("com.tyukki.mystia-steward-companion.runtime-special-rules");
            var patchedNow = new List<string>();
            var missing = new List<string>();

            PatchMethod(_harmony, IncomeControllerKoishiTypeName, "SetTargetTag", 2, nameof(OnKoishiTargetTag), patchedNow, missing);
            PatchMethod(_harmony, IncomeControllerYuumaTypeName, "SetTargetTag", 3, nameof(OnYuumaTargetTag), patchedNow, missing);

            lock (SyncRoot)
            {
                _status = PatchedMethods.Count == 0
                    ? $"waiting: {string.Join(", ", missing.Take(4))}"
                    : $"patched={PatchedMethods.Count}";
            }

            if (patchedNow.Count > 0)
            {
                log.LogInfo($"Runtime special rules patched: {string.Join(", ", patchedNow)}.");
            }
            else if (PatchedMethods.Count == 0)
            {
                log.LogWarning($"Runtime special rules waiting for game types: {string.Join(", ", missing.Take(4))}.");
            }
        }
        catch (Exception ex)
        {
            lock (SyncRoot)
            {
                _status = $"error: {ex.Message}";
            }

            log.LogWarning($"Runtime special rules attach failed: {ex.Message}");
        }
    }

    public static void ApplyTo(RecommendationState state, DataRepository repository)
    {
        var challengeMode = ReadChallengeMode();
        var rules = ReadRules(challengeMode, repository);

        state.SpecialRules.Clear();
        foreach (var rule in rules)
        {
            state.SpecialRules.Add(rule);
        }

        state.SpecialRuleStatus = $"challenge={challengeMode}; {Status}";
    }

    private static List<RuntimeSpecialRuleContext> ReadRules(string challengeMode, DataRepository repository)
    {
        var rules = new List<RuntimeSpecialRuleContext>();
        var koishiHudTags = IsChallenge(challengeMode, WackyChallengeMode)
            ? ReadHudTags(IncomeControllerKoishiTypeName, "tag1").ToList()
            : new List<string>();
        var yuumaHudTags = IsChallenge(challengeMode, YuumaChallengeMode)
            ? ReadHudTags(IncomeControllerYuumaTypeName, "tag1", "tag2").ToList()
            : new List<string>();

        var koishiTags = koishiHudTags.Count > 0
            ? koishiHudTags
            : IsChallenge(challengeMode, WackyChallengeMode)
                ? ReadCachedKoishiTags()
                : new List<string>();
        var yuumaTags = yuumaHudTags.Count > 0
            ? yuumaHudTags
            : IsChallenge(challengeMode, YuumaChallengeMode)
                ? ReadCachedYuumaTags()
                : new List<string>();

        if (IsChallenge(challengeMode, WackyChallengeMode))
        {
            rules.Add(BuildRule(
                "koishi-wacky-cooking",
                challengeMode,
                "怪诞料理大赛",
                "当前料理推荐会优先满足游戏刷新出的怪诞料理 Tag。",
                "all",
                koishiTags,
                koishiHudTags.Count > 0 ? "IncomeControllerKoishi HUD" : "IncomeControllerKoishi.SetTargetTag",
                repository));
        }

        if (IsChallenge(challengeMode, YuumaChallengeMode))
        {
            rules.Add(BuildRule(
                "yuuma-target-tags",
                challengeMode,
                "饕餮尤魔挑战",
                "当前料理推荐会优先满足饕餮尤魔指定的任意一个目标 Tag。",
                "any",
                yuumaTags,
                yuumaHudTags.Count > 0 ? "IncomeControllerYuuma HUD" : "IncomeControllerYuuma.SetTargetTag",
                repository));
        }

        return rules;
    }

    private static RuntimeSpecialRuleContext BuildRule(
        string ruleId,
        string challengeMode,
        string displayName,
        string description,
        string matchMode,
        IReadOnlyList<string> rawTags,
        string source,
        DataRepository repository)
    {
        var targets = rawTags
            .Select(raw => ResolveFoodTagTarget(raw, source, repository))
            .Where(target => !string.IsNullOrWhiteSpace(target.Name))
            .GroupBy(target => target.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        return new RuntimeSpecialRuleContext
        {
            RuleId = ruleId,
            ChallengeMode = challengeMode,
            DisplayName = displayName,
            Description = description,
            MatchMode = matchMode,
            FoodTagTargets = targets,
            Source = source,
            Status = targets.Count > 0
                ? $"active; foodTags={string.Join(",", targets.Select(target => target.Name))}"
                : "active; waiting target tag",
        };
    }

    private static RuntimeSpecialFoodTagTarget ResolveFoodTagTarget(string rawName, string source, DataRepository repository)
    {
        var normalized = NormalizeTag(rawName);
        int? id = null;
        foreach (var pair in repository.FoodTagIdMap)
        {
            if (!string.Equals(pair.Value, normalized, StringComparison.Ordinal)) continue;
            if (int.TryParse(pair.Key, out var parsed)) id = parsed;
            break;
        }

        return new RuntimeSpecialFoodTagTarget
        {
            Id = id,
            Name = normalized,
            RawName = rawName.Trim(),
            Source = source,
        };
    }

    private static IEnumerable<string> ReadHudTags(string controllerTypeName, params string[] memberNames)
    {
        var type = RuntimeReflectionUtility.FindType(controllerTypeName);
        if (type == null) yield break;

        foreach (var controller in RuntimeReflectionUtility.FindUnityObjects(type).Take(4))
        {
            foreach (var memberName in memberNames)
            {
                var label = RuntimeReflectionUtility.GetMemberValue(controller, memberName);
                var text = RuntimeReflectionUtility.GetMemberValue(label, "text")?.ToString()
                    ?? RuntimeReflectionUtility.GetMemberValue(label, "Text")?.ToString();
                var normalized = NormalizeTag(text);
                if (!string.IsNullOrWhiteSpace(normalized)) yield return normalized;
            }
        }
    }

    private static List<string> ReadCachedKoishiTags()
    {
        lock (SyncRoot)
        {
            return string.IsNullOrWhiteSpace(_koishiTag) ? new List<string>() : new List<string> { _koishiTag };
        }
    }

    private static List<string> ReadCachedYuumaTags()
    {
        lock (SyncRoot)
        {
            return new[] { _yuumaTag1, _yuumaTag2 }
                .Select(NormalizeTag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
    }

    private static string ReadChallengeMode()
    {
        var type = RuntimeReflectionUtility.FindType(NightSceneDirectorTypeName);
        if (type == null) return "";

        var value = RuntimeReflectionUtility.GetStaticMemberValue(type, "ChallengeMode");
        return value?.ToString() ?? "";
    }

    private static bool IsChallenge(string challengeMode, string expected)
    {
        return string.Equals(challengeMode, expected, StringComparison.Ordinal)
            || challengeMode.Contains(expected, StringComparison.Ordinal);
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

        var type = RuntimeReflectionUtility.FindType(typeName);
        var target = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == methodName && method.GetParameters().Length == parameterCount);
        var postfix = typeof(RuntimeSpecialRuleService).GetMethod(postfixName, BindingFlags.NonPublic | BindingFlags.Static);
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

    private static void OnKoishiTargetTag(object __0)
    {
        var tag = NormalizeTag(__0?.ToString());
        lock (SyncRoot)
        {
            _koishiTag = tag;
            _koishiTagUpdatedAtUtc = DateTime.UtcNow;
        }

        if (!string.IsNullOrWhiteSpace(tag)) _log?.LogInfo($"Special rule target updated: Koishi wacky tag={tag}.");
    }

    private static void OnYuumaTargetTag(object __0, object __1)
    {
        var tag1 = NormalizeTag(__0?.ToString());
        var tag2 = NormalizeTag(__1?.ToString());
        lock (SyncRoot)
        {
            _yuumaTag1 = tag1;
            _yuumaTag2 = tag2;
            _yuumaTagUpdatedAtUtc = DateTime.UtcNow;
        }

        var tags = string.Join(",", new[] { tag1, tag2 }.Where(tag => !string.IsNullOrWhiteSpace(tag)));
        if (!string.IsNullOrWhiteSpace(tags)) _log?.LogInfo($"Special rule target updated: Yuuma target tags={tags}.");
    }

    private static string NormalizeTag(string? value)
    {
        var trimmed = value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed)) return "";
        return FoodTags.NormalizeName(trimmed) ?? trimmed;
    }

    private static string FormatCachedTags(params string[] tags)
    {
        var values = tags.Select(NormalizeTag).Where(tag => !string.IsNullOrWhiteSpace(tag)).ToList();
        return values.Count == 0 ? "" : string.Join(",", values);
    }
}
