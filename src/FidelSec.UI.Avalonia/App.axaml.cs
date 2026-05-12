using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FidelSec.Core.Interfaces;
using FidelSec.Core.Services;
using FidelSec.ImagingEngine;
using FidelSec.UI.Avalonia.ViewModels;
using FidelSec.UI.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace FidelSec.UI.Avalonia
{
    public class App : Application
    {
        private IServiceProvider? _services;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            _services = BuildServiceProvider();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var vm = _services.GetRequiredService<MainViewModel>();

                // Start hot-plug monitoring
                var enumerator = _services.GetRequiredService<IDeviceEnumerationService>();
                enumerator.StartMonitoring();

                desktop.MainWindow = new MainWindow { DataContext = vm };
                desktop.Exit += (_, _) => enumerator.StopMonitoring();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static IServiceProvider BuildServiceProvider()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File("logs/fidelsec_.log", rollingInterval: Serilog.RollingInterval.Day)
                .CreateLogger();

            var services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(dispose: true);
            });

            // Register platform services via reflection to avoid hard compile-time
            // references to platform assemblies that may not be present.
            RegisterPlatformServices(services);

            // Imaging engine (cross-platform)
            services.AddSingleton<IImagingEngine, RawImagingEngine>();

            // ViewModels
            services.AddSingleton<DeviceListViewModel>();
            services.AddSingleton<ImagingConfigViewModel>();
            services.AddSingleton<ImagingProgressViewModel>();
            services.AddSingleton<MainViewModel>();

            return services.BuildServiceProvider();
        }

        private static void RegisterPlatformServices(IServiceCollection services)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Calls FidelSec.Infrastructure.WindowsPlatformExtensions.AddWindowsPlatformServices()
                InvokeExtensionMethod(
                    assemblyName:  "FidelSec.Infrastructure",
                    typeName:      "FidelSec.Infrastructure.WindowsPlatformExtensions",
                    methodName:    "AddWindowsPlatformServices",
                    services:      services);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Calls FidelSec.Infrastructure.Linux.LinuxPlatformExtensions.AddLinuxPlatformServices()
                InvokeExtensionMethod(
                    assemblyName:  "FidelSec.Infrastructure.Linux",
                    typeName:      "FidelSec.Infrastructure.Linux.LinuxPlatformExtensions",
                    methodName:    "AddLinuxPlatformServices",
                    services:      services);
            }
            else
            {
                throw new PlatformNotSupportedException("Only Windows and Linux are supported.");
            }
        }

        private static void InvokeExtensionMethod(
            string assemblyName, string typeName, string methodName,
            IServiceCollection services)
        {
            // Load by full path so it works when running elevated (where Assembly.Load
            // by name may not probe the application directory).
            var dllPath = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
            var assembly = System.Reflection.Assembly.LoadFrom(dllPath);
            var type     = assembly.GetType(typeName)
                ?? throw new InvalidOperationException(
                    $"Type {typeName} not found in {assemblyName}.");
            var method   = type.GetMethod(methodName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    $"Method {methodName} not found on {typeName}.");
            method.Invoke(null, new object[] { services });
        }
    }
}
