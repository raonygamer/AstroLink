using Astro.Managers;
using Astro.Models;
using Astro.Utils;
using Newtonsoft.Json;

namespace Astro.Core;

public class AstroLink
{
    #region Singleton
    private static AstroLink? _instance;
    public static AstroLink Instance => _instance ??= new AstroLink();

    private static async Task<int> Main()
    {
        var result = -1;
        try
        {
            AppDomain.CurrentDomain.ProcessExit += async void (_, __) =>
            {
                try
                {
                    await Instance.ExitAsync();
                }
                catch
                {
                    // Ignore
                }
            };
            result = await Instance.StartAsync();
        }
        catch (Exception ex)
        {
            Log.ErrorLine($"Unhandled exception: \n{ex}");
            await Task.Delay(2000);
            return result;
        }

        return result;
    }
    #endregion

    public VariableModel Variables { get; private set; } = new();
    public static async Task<VariableModel?> GetVariables()
    {
        string? variableJsonText = null;
        if (File.Exists("./variables.json"))
            variableJsonText = await File.ReadAllTextAsync("./variables.json");
        else if (Environment.GetEnvironmentVariable("ASTRO_LINK_VARS") is {} variable && File.Exists(variable))
            variableJsonText = await File.ReadAllTextAsync(variable);
        else
        {
            Log.ErrorLine($"Could not find variables file on working directory or environment variable 'ASTRO_LINK_VARS'.");
            return null;
        }
        
        return JsonConvert.DeserializeObject<VariableModel>(variableJsonText);
    }

    public Updater? Updater { get; private set; }
    public DatabaseManager DatabaseManager { get; private set; } = null!;
    public GameManager GameManager { get; private set; } = null!;
    public UptimeManager UptimeManager { get; private set; } = null!;

    private async Task<int> StartAsync()
    {
        if (await GetVariables() is not {} variables)
        {
            Log.ErrorLine($"Variables are not valid:\n    {JsonConvert.SerializeObject(Variables, Formatting.Indented)}");
            return 1;
        }
        Variables = variables;

        Updater = new Updater(100);
        Updater.Start();

        DatabaseManager = new DatabaseManager(Variables.DatabaseString);
        GameManager = new GameManager(Variables.GameId, Variables.GameEmail, Variables.GamePassword);
        UptimeManager = new UptimeManager();
        Updater.Tick += UptimeManager.TickAsync;
        UptimeManager.OnServerOnline += async t => Log.SuccessLine($"Server is online: {new DateTime().Add(TimeSpan.FromMilliseconds(t)):HH:mm:ss dd:MM:yyyy}");
        UptimeManager.OnServerOffline += async () => Log.SuccessLine("Server is offline!");
        while (true)
        {
            try
            {
                if (!GameManager.IsConnected)
                    await GameManager.ConnectAsync();
                if (!GameManager.IsOnServiceRoom)
                    await GameManager.ConnectToServiceRoomAsync();
                if (!GameManager.IsOnGameRoom)
                    await GameManager.ConnectToGameRoomAsync();
                break;
            }
            catch (Exception ex)
            {
                Log.ErrorLine($"Failed to connect to game!");
            }
            await Task.Delay(5000);
        }
        
        await Task.Delay(-1);
        return 0;
    }

    public async Task ExitAsync()
    {
        DatabaseManager.Dispose();
        GameManager.Dispose();
    }
}