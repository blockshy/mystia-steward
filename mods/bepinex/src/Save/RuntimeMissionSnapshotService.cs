using MystiaStewardCompanion.Core;

namespace MystiaStewardCompanion.Save;

public static class RuntimeMissionSnapshotService
{
    private const string DataBaseDayTypeName = "GameData.Core.Collections.DaySceneUtility.DataBaseDay";
    private const string RunTimeSchedulerTypeName = "GameData.RunTime.Common.RunTimeScheduler";
    private const string DataBaseLanguageTypeName = "GameData.CoreLanguage.Collections.DataBaseLanguage";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(3);
    private static RuntimeMissionContext? _cachedContext;
    private static DateTime _cachedAtUtc;

    public static RuntimeMissionContext Load()
    {
        var now = DateTime.UtcNow;
        if (_cachedContext != null && now - _cachedAtUtc < CacheDuration) return _cachedContext;

        _cachedContext = LoadUncached();
        _cachedAtUtc = now;
        return _cachedContext;
    }

    private static RuntimeMissionContext LoadUncached()
    {
        var missions = new List<RuntimeMissionInfo>();
        var errors = new List<string>();
        var source = new List<string>();

        var dataBaseDayType = RuntimeReflectionUtility.FindType(DataBaseDayTypeName);
        var schedulerType = RuntimeReflectionUtility.FindType(RunTimeSchedulerTypeName);
        if (dataBaseDayType == null || schedulerType == null)
        {
            return new RuntimeMissionContext
            {
                Source = $"DataBaseDay={(dataBaseDayType == null ? "missing" : "ok")}; Scheduler={(schedulerType == null ? "missing" : "ok")}",
                Error = "任务运行时类型未加载，可能尚未读取存档。",
            };
        }

        var npcKeys = RuntimeReflectionUtility
            .EnumerateObjects(RuntimeReflectionUtility.InvokeStaticMethod(dataBaseDayType, "GetAllNPCKeys"))
            .Select(value => value?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        source.Add($"npcKeys={npcKeys.Count}");

        foreach (var npcKey in npcKeys)
        {
            try
            {
                var availableLabels = RuntimeReflectionUtility
                    .EnumerateObjects(RuntimeReflectionUtility.InvokeStaticMethod(schedulerType, "GetAvailableInteractMissionForCharacter", npcKey))
                    .Select(value => value?.ToString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>()
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                foreach (var label in availableLabels)
                {
                    var started = RuntimeReflectionUtility.ToBool(RuntimeReflectionUtility.InvokeStaticMethod(schedulerType, "HaveMissionStarted", label));
                    var finished = RuntimeReflectionUtility.ToBool(RuntimeReflectionUtility.InvokeStaticMethod(schedulerType, "HaveMissionFinished", label));
                    if (started || finished) continue;

                    missions.Add(new RuntimeMissionInfo
                    {
                        Label = label,
                        Title = ResolveMissionTitle(label),
                        CharacterLabel = npcKey,
                        CharacterName = ResolveNpcName(dataBaseDayType, npcKey),
                        Source = "InteractMission",
                        Started = started,
                        Finished = finished,
                    });
                }
            }
            catch (Exception ex)
            {
                if (errors.Count < 8) errors.Add($"{npcKey}: {ex.Message}");
            }
        }

        var deduplicated = missions
            .GroupBy(mission => $"{mission.Label}|{mission.CharacterLabel}", StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(mission => mission.CharacterName, StringComparer.Ordinal)
            .ThenBy(mission => mission.Title, StringComparer.Ordinal)
            .ToList();
        source.Add($"available={deduplicated.Count}");

        return new RuntimeMissionContext
        {
            AvailableMissions = deduplicated,
            Source = string.Join("; ", source),
            Error = errors.Count == 0 ? null : string.Join("; ", errors),
        };
    }

    private static string ResolveMissionTitle(string label)
    {
        var languageType = RuntimeReflectionUtility.FindType(DataBaseLanguageTypeName);
        var language = languageType == null
            ? null
            : RuntimeReflectionUtility.InvokeStaticMethod(languageType, "GetMissionLanguage", label);
        var text = ReadTextLikeValue(language);
        return string.IsNullOrWhiteSpace(text) ? label : text;
    }

    private static string ResolveNpcName(Type dataBaseDayType, string npcKey)
    {
        var npc = RuntimeReflectionUtility.InvokeStaticMethod(dataBaseDayType, "RefNPC", npcKey);
        var text = ReadTextLikeValue(npc);
        return string.IsNullOrWhiteSpace(text) ? npcKey : text;
    }

    private static string ReadTextLikeValue(object? value)
    {
        if (value == null) return "";

        foreach (var member in new[] { "Name", "name", "Title", "title", "Text", "text", "Value", "value", "Description", "description", "Chinese", "Zh", "zh" })
        {
            try
            {
                var memberValue = RuntimeReflectionUtility.GetMemberValue(value, member)?.ToString();
                if (!string.IsNullOrWhiteSpace(memberValue)) return memberValue;
            }
            catch
            {
                // Try the next common text member.
            }
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
