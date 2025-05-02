using Astro.Managers;
using Astro.Models;
using Astro.Models.Database;
using Astro.Utils;
using Discord;
using Discord.WebSocket;
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
            AppDomain.CurrentDomain.ProcessExit += void (_, __) =>
            {
                try
                {
                    Instance.ExitAsync().GetAwaiter().GetResult();
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
    public DiscordManager DiscordManager { get; private set; } = null!;

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
        
        Task OnServerOnline(double time)
        {
            Log.SuccessLine($"Server is online: {new DateTime().Add(TimeSpan.FromMilliseconds(time)):HH:mm:ss dd/MM/yyyy}");
            return Task.CompletedTask;
        }

        Task OnServerOffline()
        {
            Log.WarnLine($"Server is offline!");
            return Task.CompletedTask;
        }
        
        UptimeManager.OnServerOnline += OnServerOnline;
        UptimeManager.OnServerOffline += OnServerOffline;
        
        DiscordManager = new DiscordManager();
        await DiscordManager.ConnectAsync(TokenType.Bot, Variables.DiscordToken);
        
        Updater.Tick += UptimeManager.TickAsync;
        var settings = await DatabaseManager.LoadSettingsAsync() ?? new SettingsModel();
        
        foreach (var (guildId, channelId) in settings.UptimeChannelsForGuilds)
        {
            if (DiscordManager.Client.GetGuild(guildId)?.GetChannel(channelId) is SocketTextChannel textChannel)
            {
                await UptimeManager.FirstUptimeMessageAsync(textChannel);
            }
        }

        void EachAttempt()
        {
            UptimeManager.ShouldForceNextCheck();
        }
        
        await GameManager.TryConnectAsync(-1, EachAttempt);
        await GameManager.TryConnectToServiceRoomAsync(-1, EachAttempt);
        await GameManager.TryConnectToGameRoomAsync(-1, EachAttempt);
        UptimeManager.ShouldForceNextCheck();
        await Task.Delay(-1);
        return 0;
    }

    public async Task ExitAsync()
    {
        DatabaseManager.Dispose();
        GameManager.Dispose();
        if (UptimeManager.UptimeMessage is not null)
        {
            await UptimeManager.UptimeMessage.ModifyAsync(p =>
            {
                p.Embed = new EmbedBuilder()
                    .WithColor(Color.Default)
                    .WithTitle("Offline")
                    .WithDescription("The bot is offline!")
                    .Build();
            });
        }
        await DiscordManager.DisposeAsync();
    }
}