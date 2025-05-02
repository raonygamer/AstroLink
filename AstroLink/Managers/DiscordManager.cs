using Astro.Core;
using Astro.Models.Database;
using Astro.Utils;
using Discord;
using Discord.WebSocket;

namespace Astro.Managers;

public class DiscordManager : IDisposable, IAsyncDisposable
{
    public AstroLink Main => AstroLink.Instance;
    public UptimeManager Uptime => Main.UptimeManager;
    public DatabaseManager Db => Main.DatabaseManager;
    
    public readonly DiscordSocketClient Client;
    public readonly TaskCompletionSource OnReadyTask = new TaskCompletionSource();
    
    public DiscordManager()
    {
        Client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.All
        });
        Client.Log += OnLog;
        Client.Ready += OnReady;
        Client.SlashCommandExecuted += OnSlashCommand;
    }

    private async Task OnSlashCommand(SocketSlashCommand command)
    {
        switch (command.CommandName)
        {
            case "set-uptime-channel":
            {
                if (command.Data.Options.FirstOrDefault(d => d.Name == "channel")?.Value is not SocketTextChannel channel)
                    break;
                await OnSetUptimeChannelCommand(command, channel);
                break;
            }
            default:
                await command.RespondAsync(embed: new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("Invalid command")
                    .WithDescription("This command is not valid!")
                    .Build(), ephemeral: true);
                break;
        }
    }

    public async Task OnSetUptimeChannelCommand(SocketSlashCommand command, SocketTextChannel channel)
    {
        if (!channel.Guild.CurrentUser.GetPermissions(channel).SendMessages)
        {
            await command.RespondAsync(embed: new EmbedBuilder()
                .WithColor(Color.Red)
                .WithTitle("Invalid channel")
                .WithDescription(@"I can't send messages to this channel, please enable ""Send Messages"" permission or try another channel!")
                .Build(), ephemeral: true);
            return;
        }

        if (command.GuildId is null)
        {
            await command.RespondAsync(embed: new EmbedBuilder()
                .WithColor(Color.Red)
                .WithTitle("Invalid context")
                .WithDescription("This command cannot be executed outside of a guild context!")
                .Build(), ephemeral: true);
            return;
        }

        await command.RespondAsync(embed: new EmbedBuilder()
            .WithColor(Color.Green)
            .WithTitle("Success")
            .WithDescription($"The uptime channel has been set successfully to #{channel.Name}.")
            .Build(), ephemeral: true);
        
        var settings = await Db.LoadSettingsAsync() ?? new SettingsModel();
        settings.UptimeChannelsForGuilds.TryGetValue(command.GuildId!.Value, out var oldUptimeChannelId);
        var oldUptimeChannel = Client.GetGuild(command.GuildId!.Value).GetChannel(oldUptimeChannelId) as SocketTextChannel;
        settings.UptimeChannelsForGuilds[command.GuildId!.Value] = channel.Id;
        await Db.SaveSettingsAsync(settings);
        await Uptime.OnUpdatedUptimeChannelAsync(oldUptimeChannel, channel);
    }

    public async Task ConnectAsync(TokenType tokenType, string token)
    {
        await Client.LoginAsync(tokenType, token);
        await Client.StartAsync();
        await OnReadyTask.Task;
    }

    private async Task OnReady()
    {
        foreach (var guild in Client.Guilds)
        {
            var commands = await guild.GetApplicationCommandsAsync();
            foreach (var command in commands)
            {
                await command.DeleteAsync();
            }
            
            await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
                .WithName("set-uptime-channel")
                .WithDescription("Sets the channel to post all the server uptime updates.")
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("channel")
                    .WithDescription("The channel to post all the server uptime updates.")
                    .WithType(ApplicationCommandOptionType.Channel))
                .Build());
        }
        
        OnReadyTask.TrySetResult();
        await Task.CompletedTask;
    }

    private static async Task OnLog(LogMessage msg)
    {
        switch (msg.Severity)
        {
            case LogSeverity.Critical:
            case LogSeverity.Error:
                Log.ErrorLine(msg.ToString(), true);
                break;
            case LogSeverity.Warning:
                Log.WarnLine(msg.ToString(), true);
                break;
            case LogSeverity.Verbose:
            case LogSeverity.Debug:
                Log.TraceLine(msg.ToString(), true);
                break;
            case LogSeverity.Info:
                Log.SuccessLine(msg.ToString());
                break;
        }
    }

    public void Dispose()
    {
        Client.LogoutAsync().GetAwaiter().GetResult();
        Client.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await Client.LogoutAsync();
        await Client.DisposeAsync();
    }
}