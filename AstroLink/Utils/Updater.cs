namespace Astro.Utils;

public class Updater(double tickInterval)
{
    public readonly double TickInterval = tickInterval;
    public double Time { get; private set; } = 0;
    public event Func<Updater, Task>? Tick;
    public bool Running { get; private set; }

    public void Loop()
    {
        _ = Task.Run(async () =>
        {
            while (Running)
            {
                Time += TickInterval;
                try
                {
                    await (Tick?.Invoke(this) ?? Task.CompletedTask);
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