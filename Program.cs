namespace Deskwell;

internal static class Program
{
    private const string SingleInstanceMutexName = "Deskwell.SingleInstance";

    [STAThread]
    private static void Main()
    {
        using var singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }

        Application.ThreadException += (_, exceptionEvent) => Logger.Write("Unhandled UI exception", exceptionEvent.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, exceptionEvent) => Logger.Write("Unhandled exception", exceptionEvent.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
