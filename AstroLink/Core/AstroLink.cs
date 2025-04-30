using Astro.Managers;
using Astro.Models;
using Astro.Registries;
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
    public LinkingRegistry LinkingRegistry { get; private set; } = null!;
    public DatabaseManager DatabaseManager { get; private set; } = null!;
    public DiscordManager DiscordManager { get; private set; } = null!;
    public GameManager GameManager { get; private set; } = null!;

    private async Task<int> StartAsync()
    {
        if (await GetVariables() is not {} variables)
        {
            Log.ErrorLine($"Variables are not valid: \n{JsonConvert.SerializeObject(Variables, Formatting.Indented)}");
            return 1;
        }
        Variables = variables;

        Updater = new Updater(100);
        Updater.Start();

        Log.TraceLine("Creating linking request registry...");
        
        DatabaseManager = await DatabaseManager.CreateAsync(this, Variables.DatabaseString);
        DiscordManager = await DiscordManager.CreateAsync(this, Variables.DiscordToken);
        GameManager = await GameManager.CreateAsync(this, Variables.GameId, Variables.GameEmail, Variables.GamePassword);
        LinkingRegistry = new LinkingRegistry(this, DatabaseManager, GameManager, DiscordManager);
        Updater.Tick += async () =>
        {
            LinkingRegistry.CheckLinkingRequests();
            await Task.CompletedTask;
        };
        
        await Task.Delay(-1);
        return 0;
    }

    public async Task ExitAsync()
    {
        DatabaseManager.Dispose();
        await DiscordManager.DisposeAsync();
        GameManager.Dispose();
    }
}