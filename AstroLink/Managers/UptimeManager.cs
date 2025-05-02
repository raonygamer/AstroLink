using Astro.Core;
using Astro.Utils;
using Discord;
using Discord.Rest;
using Discord.WebSocket;

namespace Astro.Managers;

public class UptimeManager
{
    public const double TickPingCooldown = 5 * 1000;
    
    public AstroLink Main => AstroLink.Instance;
    public GameManager Game => Main.GameManager;
    public DiscordManager Discord => Main.DiscordManager;
    
    public bool FirstPing { get; private set; } = true;
    public double NextTickPing { get; private set; } = 0;
    public bool IsOnline { get; private set; } = false;
    public bool WasOnline { get; private set; } = false;

    public event Func<double, Task>? OnServerOnline;
    public event Func<Task>? OnServerOffline;

    public bool ForceNextCheck { get; private set; } = false;
    
    public UptimeManager()
    {
        
    }

    public async Task TickAsync(Updater updater)
    {
        if (ForceNextCheck || FirstPing || updater.Time <= NextTickPing)
        {
            FirstPing = false;
            NextTickPing = updater.Time + TickPingCooldown;
            try
            {
                var serverTime = await Game.GetServerTimeAsync();
                IsOnline = serverTime != 0;
                if (ForceNextCheck)
                {
                    WasOnline = !IsOnline;
                }

                ForceNextCheck = false;
                switch (IsOnline)
                {
                    case true when !WasOnline:
                        OnServerOnline?.Invoke(serverTime);
                        if (UptimeMessage is not null)
                        {
                            await UptimeMessage.ModifyAsync(props =>
                            {
                                props.Embed = new EmbedBuilder()
                                    .WithColor(Color.Green)
                                    .WithTitle("Server is online")
                                    .WithDescription(
                                        $"Server is online since the last check at {new DateTime().Add(TimeSpan.FromMilliseconds(serverTime)):HH:mm:ss dd/MM/yyyy} UTC")
                                    .Build();
                            });
                        }

                        break;
                    case false when WasOnline:
                        OnServerOffline?.Invoke();
                        if (UptimeMessage is not null)
                        {
                            await UptimeMessage.ModifyAsync(props =>
                            {
                                props.Embed = new EmbedBuilder()
                                    .WithColor(Color.Red)
                                    .WithTitle("Server is offline")
                                    .WithDescription(
                                        $"Server is offline since the last check at {DateTime.UtcNow:HH:mm:ss dd/MM/yyyy} UTC")
                                    .Build();
                            });
                        }

                        break;
                }
            }
            catch
            {
                IsOnline = false;
                if (ForceNextCheck)
                {
                    WasOnline = !IsOnline;
                }

                ForceNextCheck = false;
                if (WasOnline != IsOnline)
                {
                    OnServerOffline?.Invoke();
                    if (UptimeMessage is not null)
                    {
                        await UptimeMessage.ModifyAsync(props =>
                        {
                            props.Embed = new EmbedBuilder()
                                .WithColor(Color.Red)
                                .WithTitle("Server is offline")
                                .WithDescription(
                                    $"Server is offline since the last check at {DateTime.UtcNow:HH:mm:ss dd/MM/yyyy} UTC")
                                .Build();
                        });
                    }
                }
            }
            
            WasOnline = IsOnline;
        }
    }

    public RestUserMessage? UptimeMessage { get; private set; }
    
    public async Task FirstUptimeMessageAsync(SocketTextChannel channel, bool forceNextCheck = false)
    {
        var botMessages = (await channel.GetMessagesAsync().FlattenAsync()).Where(m => m.Author.Id == Discord.Client.CurrentUser.Id);
        foreach (var message in botMessages)
        {
            await message.DeleteAsync();
        }
        
        UptimeMessage = await channel.SendMessageAsync(embed: new EmbedBuilder()
            .WithColor(Color.Default)
            .WithTitle("Fetching server uptime...")
            .Build());
        if (forceNextCheck)
            ShouldForceNextCheck();
    }
    
    public async Task OnUpdatedUptimeChannelAsync(SocketTextChannel? oldChannel, SocketTextChannel? channel)
    {
        if (oldChannel is not null)
        {
            var botMessages = (await oldChannel.GetMessagesAsync().FlattenAsync()).Where(m => m.Author.Id == Discord.Client.CurrentUser.Id);
            foreach (var message in botMessages)
            {
                await message.DeleteAsync();
            }
        }
        
        if (channel is not null)
        {
            await FirstUptimeMessageAsync(channel, true);
        }
    }

    public void ShouldForceNextCheck() => ForceNextCheck = true;
}