using System.Collections;
using System.Reflection;
namespace MystiaStewardCompanion.Save;

internal static class RuntimeReflectionUtility
{
    public static Type? FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(fullName, throwOnError: false);
            if (type != null) return type;
        }

        return null;
    }

    public static object? GetSingletonInstance(Type type)
    {
        return GetStaticMemberValue(type, "Instance")
            ?? GetStaticMemberValue(type, "Main")
            ?? GetStaticMemberValue(type, "Singleton")
            ?? GetStaticMemberValue(type, "Current");
    }

    public static object? InvokeStaticMethod(Type type, string methodName, params object?[] args)
    {
        var method = FindMethod(type, methodName, args.Length, isStatic: true);
        return method?.Invoke(null, args);
    }

    public static object? InvokeMethod(object? instance, string methodName, params object?[] args)
    {
        if (instance == null) return null;
        var method = FindMethod(instance.GetType(), methodName, args.Length, isStatic: false);
        return method?.Invoke(instance, args);
    }

    public static object? GetStaticMemberValue(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var property = type.GetProperty(name, flags);
        if (property != null) return property.GetValue(null);

        var field = type.GetField(name, flags);
        return field?.GetValue(null);
    }

    public static object? GetMemberValue(object? instance, string name)
    {
        if (instance == null) return null;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        for (var type = instance.GetType(); type != null; type = type.BaseType)
        {
            var property = type.GetProperty(name, flags);
            if (property != null) return property.GetValue(instance);

            var field = type.GetField(name, flags);
            if (field != null) return field.GetValue(instance);

            var method = type.GetMethod(name, flags, binder: null, Type.EmptyTypes, modifiers: null);
            if (method != null) return method.Invoke(instance, null);
        }

        return null;
    }

    public static IEnumerable<object?> EnumerateObjects(object? value)
    {
        if (value == null || value is string) yield break;
        if (value is IEnumerable enumerable)
        {
            IEnumerator enumerator;
            try
            {
                enumerator = enumerable.GetEnumerator();
            }
            catch
            {
                yield break;
            }

            while (true)
            {
                object? current;
                try
                {
                    if (!enumerator.MoveNext()) yield break;
                    current = enumerator.Current;
                }
                catch
                {
                    yield break;
                }

                yield return current;
            }
        }
    }

    public static int ToInt(object? value, int fallback = 0)
    {
        if (value == null) return fallback;
        if (value is int intValue) return intValue;
        if (value is long longValue) return (int)longValue;
        if (value is short shortValue) return shortValue;
        if (value is byte byteValue) return byteValue;
        if (value is Enum enumValue) return Convert.ToInt32(enumValue);
        return int.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    public static bool ToBool(object? value)
    {
        if (value == null) return false;
        if (value is bool boolValue) return boolValue;
        return bool.TryParse(value.ToString(), out var parsed) && parsed;
    }

    public static string Trim(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        return value[..maxLength] + "...";
    }

    private static MethodInfo? FindMethod(Type type, string methodName, int argCount, bool isStatic)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        return type
            .GetMethods(flags)
            .FirstOrDefault(method => method.Name == methodName && method.GetParameters().Length == argCount);
    }
}
