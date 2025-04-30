using System.Reflection;

namespace Astro.Utils;

public abstract class ArgumentModel;

public class Argument
{
    public static Dictionary<string, string> ParseAllToDictionary(params string[] args)
    {
        var argumentDict = new Dictionary<string, string>();
        // Iterate over all arguments
        foreach (var arg in args)
        {
            if (!arg.StartsWith('/'))
            {
                Log.ErrorLine($"Failed to parse argument '{arg}', it doesn't start with '/', skipping.", true);
                continue;
            }

            var trimmedArg = arg.TrimStart('/');
            var indexOfEq = trimmedArg.IndexOf('=');
            var isBoolean = indexOfEq == -1;
            var argKey = trimmedArg.Substring(0, indexOfEq == -1 ? trimmedArg.Length : indexOfEq);
            var argValue = isBoolean ? "true" : trimmedArg.Substring(indexOfEq + 1);
            
            // Add argument to the dictionary
            argumentDict[argKey] = argValue;
        }
        return argumentDict;
    }
    
    public static T ParseAllTo<T>(params string[] args) 
        where T : ArgumentModel, new()
    {
        var argumentDict = ParseAllToDictionary(args);
        var instance = new T();
        var properties = typeof(T).GetProperties().Where(p => p.CanWrite).ToDictionary(p => p.Name, p => p);

        // Iterate over the parsed argument dictionary
        foreach (var (argName, argValue) in argumentDict)
        {
            if (!properties.TryGetValue(argName, out var property))
            {
                Log.ErrorLine($"Property '{argName}' not found on type '{typeof(T).Name}'.", true);
                continue;
            }

            try
            {
                var value = Convert.ChangeType(argValue, property.PropertyType);
                property.SetValue(instance, value);
            }
            catch (Exception e)
            {
                Log.ErrorLine($"Failed to convert argument '{argName}' to type '{property.PropertyType}': \n{e.Message}", true);
            }
        }
        
        return instance;
    }
}