namespace HueDesk;

public static class LightCommands
{
    public static async Task RunSafely(HueLight light, Func<Task> command)
    {
        try
        {
            await command();
        }
        catch (Exception exception)
        {
            Logger.Write($"{light.SavedLight.Name}: command failed", exception);
        }
    }
}
