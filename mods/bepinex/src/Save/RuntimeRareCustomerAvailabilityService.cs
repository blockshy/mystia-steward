using System.Collections;

namespace MystiaStewardCompanion.Save;

internal static class RuntimeRareCustomerAvailabilityService
{
    private const string RuntimeAlbumTypeName = "GameData.RunTime.Common.RunTimeAlbum";

    public static HashSet<int> ReadAvailableRareCustomerIds()
    {
        var result = new HashSet<int>();
        var albumType = RuntimeReflectionUtility.FindType(RuntimeAlbumTypeName);
        if (albumType == null) return result;

        AddIds(result, RuntimeReflectionUtility.InvokeStaticMethod(albumType, "GetAllRecordedSpecialGuests"));
        if (result.Count > 0) return result;

        AddDictionaryKeys(result, RuntimeReflectionUtility.GetStaticMemberValue(albumType, "RecordedSpecialNPCs"));
        return result;
    }

    private static void AddIds(HashSet<int> target, object? values)
    {
        foreach (var value in RuntimeReflectionUtility.EnumerateObjects(values))
        {
            var id = RuntimeReflectionUtility.ToInt(value, -1);
            if (id >= 0) target.Add(id);
        }
    }

    private static void AddDictionaryKeys(HashSet<int> target, object? dictionary)
    {
        if (dictionary == null) return;

        if (dictionary is IDictionary managedDictionary)
        {
            foreach (var key in managedDictionary.Keys)
            {
                var id = RuntimeReflectionUtility.ToInt(key, -1);
                if (id >= 0) target.Add(id);
            }

            return;
        }

        var keys = RuntimeReflectionUtility.GetMemberValue(dictionary, "Keys");
        AddIds(target, keys);
    }
}
