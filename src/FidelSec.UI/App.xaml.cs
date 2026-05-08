using System;
using System.IO;
using System.Windows;
using FidelSec.Core.Interfaces;
using FidelSec.UI.Converters;
using FidelSec.ImagingEngine;
using FidelSec.Infrastructure.DeviceDetection;
using FidelSec.Infrastructure.DiskAccess;
using FidelSec.Infrastructure.Hashing;
using FidelSec.Infrastructure.Logging;
using FidelSec.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace FidelSec.UI
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Configure Serilog — logs to both console and rolling file
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "FidelSec", "Logs");
            Directory.CreateDirectory(logDir);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    Path.Combine(logDir, "fidelSec-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            // Build DI container
            var services = new ServiceCollection();

            // Logging
            services.AddLogging(lb =>
            {
                lb.ClearProviders();
                lb.AddSerilog(Log.Logger, dispose: true);
                lb.SetMinimumLevel(LogLevel.Debug);
            });

            // Forensic logger (writes to dedicated forensic log directory)
            string forensicLogDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "FidelSec", "ForensicLogs");
            services.AddSingleton<IForensicLogger>(sp =>
                new ForensicLogger(
                    sp.GetRequiredService<ILogger<ForensicLogger>>(),
                    forensicLogDir));

            // Device detection
            services.AddSingleton<IDeviceScanner, WmiDeviceScanner>();

            // Hashing engine
            services.AddSingleton<IHashingEngine, HashingEngine>();

            // Imaging engine
            services.AddSingleton<IImagingEngine, RawImagingEngine>();

            // ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<DeviceListViewModel>();
            services.AddTransient<ImagingConfigViewModel>();
            services.AddTransient<ImagingProgressViewModel>();

            Services = services.BuildServiceProvider();

            // Register value converters BEFORE MainWindow is created,
            // because XAML resolves StaticResource during InitializeComponent()
            Current.Resources["BoolToVisConverter"] = new BoolToVisibilityConverter();
            Current.Resources["InverseBoolToVisConverter"] = new InverseBoolToVisibilityConverter();
            Current.Resources["NullToVisConverter"] = new NullToVisibilityConverter();
            Current.Resources["BadSectorColorConverter"] = new BadSectorColorConverter();
            Current.Resources["InverseBool"] = new InverseBoolConverter();
            Current.Resources["BoolToVis"] = new BoolToVisibilityConverter();

            // Global exception handler — show error dialog instead of silently crashing
            DispatcherUnhandledException += (_, args) =>
            {
                Log.Error(args.Exception, "Unhandled UI exception");
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{args.Exception.Message}\n\nDetails:\n{args.Exception.GetType().FullName}",
                    "FidelSec Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;  // prevent app from closing
            };

            // Launch main window
            var mainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>()
            };
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
