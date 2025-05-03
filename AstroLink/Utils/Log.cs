using System.Diagnostics;
using System.Reflection;

namespace Astro.Utils;

public static class Log
{
#if DEBUG
    public const bool IsDebug = true;
#else
    public const bool IsDebug = false;
#endif
    
    public static void Write(object? obj, ConsoleColor fgColor = ConsoleColor.Gray, ConsoleColor bgColor = ConsoleColor.Black)
    {
        var oldFgColor = Console.ForegroundColor;
        var oldBgColor = Console.BackgroundColor;
        Console.ForegroundColor = fgColor;
        Console.BackgroundColor = bgColor;
        Console.Write($"[{DateTime.Now:HH:mm:ss}] {obj}");
        Console.ForegroundColor = oldFgColor;
        Console.BackgroundColor = oldBgColor;
    }
    
    public static void WriteLine(object? obj, ConsoleColor fgColor = ConsoleColor.Gray, ConsoleColor bgColor = ConsoleColor.Black) => 
        Write((obj?.ToString() ?? "") + '\n', fgColor, bgColor);

    public static void SuccessLine(object? text, bool debug = false)
    {
        if (debug && !IsDebug)
            return;
        WriteLine(text, ConsoleColor.DarkCyan);
    }
    
    public static void TraceLine(object? text, bool debug = false)
    {
        if (debug && !IsDebug)
            return;
        WriteLine(text, ConsoleColor.White);
    }
    
    public static void WarnLine(object? text, bool debug = false) 
    {
        if (debug && !IsDebug)
            return;
        WriteLine(text, ConsoleColor.Yellow);
    }
    
    public static void ErrorLine(object? text, bool debug = false)
    {
        if (debug && !IsDebug)
            return;
        WriteLine(text, ConsoleColor.Red);
    }
}