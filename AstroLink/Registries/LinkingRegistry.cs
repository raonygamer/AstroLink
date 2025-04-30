using Astro.Core;
using Astro.Models;
using Astro.Models.Database;
using Astro.Utils;
using PlayerIOClient;

namespace Astro.Registries;

public class LinkingRegistry
{
    public readonly AstroLink Main;
    private readonly List<LinkingRequestModel> LinkingRequests = [];
    private readonly List<(UserLinkModel UserLink, DatabaseObject PlayerObj)> LinkedUsers = [];

    public LinkingRegistry(AstroLink main)
    {
        Main = main;
        var loadedPlayerObjects = main.GameManager.GetPlayerObjects(main.DatabaseManager.GetUserLinks().Select(ul => ul.GamePlayerId).ToArray());
        LinkedUsers = main.DatabaseManager.GetUserLinks()
            .Where(userLink => loadedPlayerObjects.Any(o => o.Key == userLink.GamePlayerId))
            .Select(userLink => (UserLink: userLink, PlayerObj: loadedPlayerObjects.First(o => o.Key == userLink.GamePlayerId)))
            .ToList();
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
        
        var database = Main.DatabaseManager;
        var game = Main.GameManager;
        
        var userLink = new UserLinkModel()
        {
            DiscordUserId = linkingRequest.DiscordUserId,
            DiscordUsername = linkingRequest.DiscordUsername,
            GamePlayerId = senderId
        };
        
        await database.AddLinkAsync(userLink);
        LinkedUsers.Add((UserLink: userLink, PlayerObj: await game.GetPlayerObjectAsync(senderId)));
        return linkingRequest;
    }

    public async Task<UserLinkModel?> UnlinkUser(ulong userId)
    {
        return null;
    }
}