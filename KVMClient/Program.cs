using Avalonia;
using System;
using System.Net;

namespace KVMClient
{
    internal class Program
    {
        public static string TitleBranding = "KVMClient ALPHA 0.09";
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            Console.WriteLine(TitleBranding);
            ServicePointManager.ServerCertificateValidationCallback += (o, c, ch, er) => true;

            BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
