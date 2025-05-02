using Astro.Core;
using Astro.Utils;

namespace Astro.Managers;

public class UptimeManager
{
    public const double TickPingCooldown = 5 * 1000;
    
    public AstroLink Main => AstroLink.Instance;
    public GameManager Game => Main.GameManager;
    public bool FirstPing { get; private set; } = true;
    public double NextTickPing { get; private set; } = 0;
    public bool IsOnline { get; private set; } = false;
    public bool WasOnline { get; private set; } = false;

    public event Func<double, Task>? OnServerOnline;
    public event Func<Task>? OnServerOffline;
    
    public UptimeManager()
    {
        
    }

    public async Task TickAsync(Updater updater)
    {
        if (FirstPing || updater.Time <= NextTickPing)
        {
            FirstPing = false;
            NextTickPing = updater.Time + TickPingCooldown;
            var serverTime = await Game.GetServerTimeAsync();
            IsOnline = serverTime is not null && serverTime != 0;
            switch (IsOnline)
            {
                case true when !WasOnline:
                    OnServerOnline?.Invoke(serverTime!.Value);
                    break;
                case false when WasOnline:
                    OnServerOffline?.Invoke();
                    break;
            }

            WasOnline = IsOnline;
        }
    }
}