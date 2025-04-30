using Astro.Core;
using Astro.Models;
using Astro.Models.Database;
using Astro.Utils;
using Discord;
using Discord.WebSocket;

namespace Astro.Managers;

public class DiscordManager : IDisposable, IAsyncDisposable
{
    public readonly AstroLink Main;
    public readonly DiscordSocketClient Client;
    public readonly TaskCompletionSource OnReadyCompletion;
    private readonly Dictionary<ulong, Dictionary<ulong, SocketGuildUser>> UsersCache = [];
    
    private DiscordManager(AstroLink main, DiscordSocketClient client)
    {
        Main = main;
        Client = client;
        Client.Log += OnLog;
        Client.Ready += OnReady;
        Client.SlashCommandExecuted += OnSlashCommand;
        OnReadyCompletion = new TaskCompletionSource();
    }

    private async Task OnSlashCommand(SocketSlashCommand command)
    {
        switch (command.CommandName)
        {
            case "link":
            {
                var responseEmbed  = new EmbedBuilder();
                var database = Main.DatabaseManager;
                var registry = Main.LinkingRegistry;

                if (await database.GetUserLinkByDiscordUserIdAsync(command.User.Id) is {} link)
                {
                    responseEmbed.WithColor(Color.Red);
                    responseEmbed.WithTitle("This account is already linked");
                    responseEmbed.WithDescription($"Linked player id: {link.GamePlayerId}\n" +
                                                  $"To re-link it again please use /unlink first.");
                    await command.RespondAsync(embed: responseEmbed.Build(), ephemeral: true);
                    break;
                }
                
                if (registry.GetLinkingRequest(command.User.Id) is {} linkingRequest)
                {
                    responseEmbed.WithColor(Color.Orange);
                    responseEmbed.WithTitle("Linking request already exists");
                    responseEmbed.WithDescription($"You already issued a linking request.\n" +
                                                  $"```/w AstroLink {linkingRequest.RequestToken}```\n" +
                                                  $"This request will expire in {(linkingRequest.ExpirationDate - DateTime.UtcNow).Minutes} minutes.");
                    await command.RespondAsync(embed: responseEmbed.Build(), ephemeral: true);
                    break;
                }
                
                linkingRequest = registry.CreateLinkingRequest(command.User.Id, command.User.Username);
                responseEmbed.WithColor(Color.Green);
                responseEmbed.WithTitle("Issued a new linking request");
                responseEmbed.WithDescription($"Please send this on the game chat to finish linking your account\n" +
                                              $"```/w AstroLink {linkingRequest.RequestToken}```\n" +
                                              $"This request will expire in {LinkingRequestModel.DefaultExpirationMinutes} minutes.");
                await command.RespondAsync(embed: responseEmbed.Build(), ephemeral: true);
                Log.SuccessLine($"Created linking request for '{command.User.Id}' with id '{linkingRequest.RequestToken}'.");
                break;
            }
            case "unlink":
            {
                var responseEmbed  = new EmbedBuilder();
                var database = Main.DatabaseManager;
                var registry = Main.LinkingRegistry;
                
                if (await database.GetUserLinkByDiscordUserIdAsync(command.User.Id) is not {} link)
                {
                    responseEmbed.WithColor(Color.Red);
                    responseEmbed.WithTitle("Not linked yet");
                    responseEmbed.WithDescription($"This account was not linked to a game id\n" +
                                                  $"To link it please use /link.");
                    await command.RespondAsync(embed: responseEmbed.Build(), ephemeral: true);
                    break;
                }

                await registry.UnlinkUser(link.DiscordUserId);
                responseEmbed.WithColor(Color.Green);
                responseEmbed.WithTitle("Unlinked account");
                responseEmbed.WithDescription($"Your discord user was unlinked from '{link.GamePlayerId}'.");
                await command.RespondAsync(embed: responseEmbed.Build(), ephemeral: true);
                Log.SuccessLine($"Unlinked account '{command.User.Id}' from game id '{link.GamePlayerId}'.");
                break;
            }
            case "set-supporter-role":
            {
                var embed = new EmbedBuilder();
                var database = Main.DatabaseManager;
                
                if (command.User.Id != Main.Variables.BotOwnerId && (command.User as SocketGuildUser)?.GuildPermissions.Administrator == false)
                {
                    embed
                        .WithColor(Color.Red)
                        .WithTitle("Not Authorized")
                        .WithDescription("You do not have permission to use this command.");
                    
                    await command.RespondAsync(embed: embed.Build(), ephemeral: true);
                    break;
                }
                
                if (command.GuildId is null)
                {
                    embed.WithColor(Color.Red);
                    embed.WithTitle("Not on guild context");
                    embed.WithDescription("Failed to execute command on non-guild context.");
                    await command.RespondAsync(embed: embed.Build(), ephemeral: true);
                    break;
                }

                var settings = await database.LoadSettingsAsync() ?? new SettingsModel();
                settings.SupporterRolesForGuilds[command.GuildId.Value] = (command.Data.Options.FirstOrDefault(f => f.Name == "role")!.Value as SocketRole)!.Id;
                await database.SaveSettingsAsync(settings);
                embed.WithColor(Color.Green);
                embed.WithTitle("Success");
                embed.WithDescription("Supporter role updated successfully.");
                await command.RespondAsync(embed: embed.Build(), ephemeral: true);
                Log.SuccessLine($"Guild '{command.GuildId.Value}' updated Supporter role id to '{settings.SupporterRolesForGuilds[command.GuildId.Value]}'.");
                break;
            }
            case "check-supporter":
            {
                var embed = new EmbedBuilder();
                var database = Main.DatabaseManager;
                var registry = Main.LinkingRegistry;
                
                if (await database.GetUserLinkByDiscordUserIdAsync(command.User.Id) is not {} link)
                {
                    embed
                        .WithColor(Color.Red)
                        .WithTitle("Not linked yet")
                        .WithDescription("This account was not linked to a game id, to link it please use /link.");
                    
                    await command.RespondAsync(embed: embed.Build(), ephemeral: true);
                    break;
                }

                if (registry.IsSynchronizedSupporter(command.User.Id))
                {
                    embed
                        .WithColor(Color.Orange)
                        .WithTitle("Already synchronized")
                        .WithDescription("You are already synchronized as a supporter");
                    
                    await command.RespondAsync(embed: embed.Build(), ephemeral: true);
                    break;
                }
                
                if (await registry.CreateSupporterAsync(command.User.Id) is not {} supporter)
                {
                    embed
                        .WithColor(Color.Red)
                        .WithTitle("Not a supporter")
                        .WithDescription("Your game account doesn't have in-game supporter, please buy it first then re-check.");
                    
                    await command.RespondAsync(embed: embed.Build(), ephemeral: true);
                    break;
                }
                
                embed
                    .WithColor(Color.Green)
                    .WithTitle("Success")
                    .WithDescription($"You received the supporter role, {
                        (supporter.IsMod ? "it will never expire." : $"please keep in mind it will expire in {supporter.ExpirationDate:dd/MM/yyyy HH:mm:ss}")
                    }");

                await command.RespondAsync(embed: embed.Build(), ephemeral: true);
                break;
            }
        }
    }

    private async Task OnReady()
    {
        var linkCommandBuilder = new SlashCommandBuilder();
        linkCommandBuilder.WithName("link");
        linkCommandBuilder.WithDescription("Links a discord user to a in-game account.");
        await Client.CreateGlobalApplicationCommandAsync(linkCommandBuilder.Build());
        
        var unlinkCommandBuilder = new SlashCommandBuilder();
        unlinkCommandBuilder.WithName("unlink");
        unlinkCommandBuilder.WithDescription("Unlinks a discord user from a in-game account.");
        await Client.CreateGlobalApplicationCommandAsync(unlinkCommandBuilder.Build());
        
        var setSupporterRoleCommandBuilder = new SlashCommandBuilder();
        setSupporterRoleCommandBuilder.WithName("set-supporter-role");
        setSupporterRoleCommandBuilder.WithDescription("Sets the supporter role for this discord server.");
        setSupporterRoleCommandBuilder.AddOption("role", ApplicationCommandOptionType.Role, "The role to set as the supporter role.", true);
        await Client.CreateGlobalApplicationCommandAsync(setSupporterRoleCommandBuilder.Build());
        
        var checkSupporterCommandBuilder = new SlashCommandBuilder();
        checkSupporterCommandBuilder.WithName("check-supporter");
        checkSupporterCommandBuilder.WithDescription("Checks the supporter from your in-game account.");
        await Client.CreateGlobalApplicationCommandAsync(checkSupporterCommandBuilder.Build());
        
        OnReadyCompletion.SetResult();
    }

    private async Task OnLog(LogMessage msg)
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
            case LogSeverity.Info:
            case LogSeverity.Verbose:
            case LogSeverity.Debug:
                Log.TraceLine(msg.ToString(), true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(msg.Severity));
        }
        await Task.CompletedTask;
    }
    
    public static async Task<DiscordManager> CreateAsync(AstroLink main, string token)
    {
        Log.TraceLine($"Creating discord manager...");
        var client = new DiscordSocketClient(new DiscordSocketConfig()
        {
            AlwaysDownloadUsers = true,
            GatewayIntents = GatewayIntents.All
        });
        var manager = new DiscordManager(main, client);
        await client.LoginAsync(TokenType.Bot, token);
        await client.StartAsync();
        await manager.OnReadyCompletion.Task;
        Log.SuccessLine("Discord manager created successfully.");
        return manager;
    }

    public void Dispose()
    {
        Client.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
    }
}