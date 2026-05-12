using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.X11;

namespace FidelSec.UI.Avalonia
{
    internal sealed class Program
    {
        [STAThread]
        public static void Main(string[] args) =>
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp()
        {
            var builder = AppBuilder.Configure<App>()
                                    .UsePlatformDetect()
                                    .WithInterFont()
                                    .LogToTrace();

            // On Linux, add software rendering as fallback for devices
            // without Vulkan/OpenGL support (e.g. VMs, embedded devices).
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                builder = builder.With(new X11PlatformOptions
                {
                    RenderingMode = new[]
                    {
                        X11RenderingMode.Glx,
                        X11RenderingMode.Software
                    }
                });
            }

            return builder;
        }
    }
}
