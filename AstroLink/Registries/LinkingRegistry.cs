using System.Security.Claims;
using Astro.Core;
using Astro.Managers;
using Astro.Models;
using Astro.Models.Database;
using Astro.Models.Game;
using Astro.Utils;
using PlayerIOClient;

namespace Astro.Registries;

public class LinkingRegistry
{
    public readonly AstroLink Main;
    public readonly DatabaseManager Database;
    public readonly GameManager Game;
    public readonly DiscordManager Discord;
    private readonly List<LinkingRequestModel> LinkingRequests = [];
    private readonly List<(UserLinkModel UserLink, DatabaseObject PlayerObj)> LinkedUsers;
    private readonly Dictionary<string, SupporterUserModel> SupporterUsers = [];

    public LinkingRegistry(AstroLink main, DatabaseManager database, GameManager game, DiscordManager discord)
    {
        Main = main;
        Database = database;
        Game = game;
        Discord = discord;
        var loadedPlayerObjects = game.GetPlayerObjects(database.GetUserLinks().Select(ul => ul.GamePlayerId).ToArray());
        LinkedUsers = database.GetUserLinks()
            .Where(userLink => loadedPlayerObjects.Any(o => o.Key == userLink.GamePlayerId))
            .Select(userLink => (UserLink: userLink, PlayerObj: loadedPlayerObjects.First(o => o.Key == userLink.GamePlayerId)))
            .ToList();

        LinkedUsers.ForEach(link => CreateSupporter(link.UserLink.DiscordUserId));
    }
    
    public void CheckLinkingRequests()
    {
        for (var i = LinkingRequests.Count - 1; i >= 0; i--)
        {
            var linkingRequest = LinkingRequests[i];
            if (linkingRequest.IsValid) 
                continue;
            
            Log.WarnLine($"Linking request token '{linkingRequest.RequestToken}' expired.");
            InvalidateLinkingRequest(linkingRequest.RequestToken);
        }
    }

    public async Task CheckSupporters()
    {
        for (var i = SupporterUsers.Keys.Count - 1; i >= 0; i--)
        {
            var playerId = SupporterUsers.Keys.ElementAt(i);
            var supporter = SupporterUsers[playerId];
            if (!supporter.IsExpired)
                continue;
            await InvalidateSupporter(supporter);
        }
    }

    public async Task InvalidateSupporter(SupporterUserModel supporter)
    {
        if (!SupporterUsers.Remove(supporter.UserLink.GamePlayerId))
            return;
        var settings = await Database.LoadSettingsAsync() ?? new SettingsModel();
        foreach (var (guildId, roleId) in settings.SupporterRolesForGuilds)
        {
            if (Discord.Client.GetGuild(guildId) is not {} guild ||
                guild.GetUser(supporter.UserLink.DiscordUserId) is not {} user)
                continue;

            await user.RemoveRoleAsync(roleId);
        }
    }

    public LinkingRequestModel? GetLinkingRequest(string requestToken)
    {
        return LinkingRequests.FirstOrDefault(x => x.RequestToken == requestToken);
    }
    
    public LinkingRequestModel? GetLinkingRequest(ulong userId)
    {
        return LinkingRequests.FirstOrDefault(x => x.DiscordUserId == userId);
    }

    public LinkingRequestModel CreateLinkingRequest(ulong userId, string username)
    {
        var linkingRequest =
            new LinkingRequestModel(userId, username, TimeSpan.FromMinutes(LinkingRequestModel.DefaultExpirationMinutes));
        LinkingRequests.Add(linkingRequest);
        return linkingRequest;
    }

    private LinkingRequestModel? InvalidateLinkingRequest(string requestToken)
    {
        var request = LinkingRequests.FirstOrDefault(x => x.RequestToken == requestToken);
        if (request is null)
            return null;
        LinkingRequests.Remove(request);
        return request;
    }

    public async Task<LinkingRequestModel?> AcceptLinkingRequest(string requestToken, string senderId)
    {
        if (InvalidateLinkingRequest(requestToken) is not {} linkingRequest)
            return null;
        
        var userLink = new UserLinkModel
        {
            DiscordUserId = linkingRequest.DiscordUserId,
            DiscordUsername = linkingRequest.DiscordUsername,
            GamePlayerId = senderId
        };
        
        await Database.AddLinkAsync(userLink);
        var playerObj = await Game.GetPlayerObjectAsync(senderId);
        LinkedUsers.Add((UserLink: userLink, PlayerObj: playerObj));
        await CreateSupporterAsync(userLink.DiscordUserId);
        return linkingRequest;
    }

    public async Task<UserLinkModel?> UnlinkUser(ulong userId)
    {
        if (await Database.GetUserLinkByDiscordUserIdAsync(userId) is not {} userLink)
            return null;
        await Database.RemoveLinkAsync(userLink);
        LinkedUsers.Remove(LinkedUsers.FirstOrDefault(t => t.UserLink == userLink));
        if (SupporterUsers.Values.FirstOrDefault(s => s.UserLink == userLink) is {} supporter)
            await InvalidateSupporter(supporter);
        return userLink;
    }

    public (UserLinkModel UserLink, DatabaseObject PlayerObj)? GetUserLink(ulong userId)
    {
        return LinkedUsers.FirstOrDefault(t => t.UserLink.DiscordUserId == userId);
    }

    public SupporterUserModel? CreateSupporter(ulong userId)
    {
        return CreateSupporterAsync(userId).Result;
    }

    public bool IsSynchronizedSupporter(ulong userId)
    {
        return SupporterUsers.Values.FirstOrDefault(v => v.UserLink.DiscordUserId == userId) is not null;
    }
    
    public async Task<SupporterUserModel?> CreateSupporterAsync(ulong userId)
    {
        if (SupporterUsers.Values.FirstOrDefault(v => v.UserLink.DiscordUserId == userId) is not null)
            return null;
        if (GetUserLink(userId) is not {} link)
            return null;
        var isMod = link.PlayerObj.TryGetValue("mod", out var modObj) && modObj is not null && Convert.ToBoolean(modObj);
        if (!isMod && (!link.PlayerObj.TryGetValue("supporter", out var supporterObj) || supporterObj is null ||
            SupporterUserModel.TryParseExpirationDate(supporterObj.ToString()!) is not {} date ||
            DateTime.UtcNow >= date)) 
            return null;
        
        var expiration = (link.PlayerObj["supporter"] is {} dateObj ? SupporterUserModel.TryParseExpirationDate(dateObj.ToString()!) : DateTime.MinValue) ?? DateTime.MinValue;
        var supporterUser = new SupporterUserModel(Discord, link.UserLink, expiration, isMod);
        SupporterUsers.Add(supporterUser.UserLink.GamePlayerId, supporterUser);
        var settings = await Database.LoadSettingsAsync() ?? new SettingsModel();
        foreach (var (guildId, roleId) in settings.SupporterRolesForGuilds)
        {
            if (Discord.Client.GetGuild(guildId) is not {} guild)
                continue;
            if (guild.GetUser(userId) is not {} user)
                continue;

            await user.AddRoleAsync(roleId);
        }
        return supporterUser;
    }
}