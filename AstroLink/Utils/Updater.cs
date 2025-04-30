namespace Astro.Utils;

public class Updater
{
    public readonly uint TickInterval;
    public event Func<Task>? Tick;
    public bool Running { get; private set; }
    
    public Updater(uint tickInterval)
    {
        TickInterval = tickInterval;
    }

    public void Loop()
    {
        Task.Run(async void () =>
        {
            while (Running)
            {
                try
                {
                    await (Tick?.Invoke() ?? Task.CompletedTask);
                }
                catch
                {
                    // Ignored
                }
                await Task.Delay((int)TickInterval);
            }
        });
    }

    public void Start()
    {
        if (Running)
            return;
        Running = true;
        Loop();
    }

    public void Stop()
    {
        if (!Running)
            return;
        Running = false;
    }
}