#define MVVM_APP

using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using SpsGui.Behaviors;
using SpsGui.Models;
using SpsGui.Models.Services;
using SpsGui.ViewModels;
using SpsGui.Views;
using SpsLogic;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;

namespace SpsGui
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            Logger.Log("---Application setup is called---", true);

            // Prepare MVVM application
#if MVVM_APP
            var startupOptions = SpsGuiStartupOptions.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());
            Ioc.Default.ConfigureServices(new ServiceCollection()
                .AddSingleton<IStartupOptionsProvider>(new StartupOptionsProvider(startupOptions))
                .AddSingleton<IConductor, Conductor>()
                .AddSingleton<IApplicationTitleService, ApplicationTitleService>()
                .AddSingleton<IDialogService, DialogService>()
                .AddSingleton<IOverlayService, OverlayService>()
                .AddSingleton<ISteamRelayService, SteamRelayService>()
                .AddSingleton<INotificationService, NotificationService>()
                .AddSingleton<IVersionCheckService, VersionCheckService>()
                .AddSingleton<ISteamAppFinder, SteamAppFinder>()
                .AddSingleton<IFindSteamExeService, FindSteamExeService>()
                .AddSingleton<IPacketScan, PacketScanDivert>()
                .AddTransient<CoreWindowViewModel>()
                .BuildServiceProvider());
#else
            //new PingOverlayTest().Show();
            //new SnapshotChartDemoTest().Show();
            //new MainWindow().Show();
            // new PacketScanTest().Show();
            //new SteamDetectTest().Show();
            //new SteamDetectorV2Test().Show();
            // new SteamAppFinderTest().Show();
            //new FindSteamExeServiceTest().Show();
            //new SteamPacketScanTest().Show();
            // new SnapshotChartDemoTest().Show();
            //new FindSteamExeServiceTest().Show();
            //new OverlayWindowTest().Show();
            new NotificationServiceTest().Show();
#endif
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
#if MVVM_APP
            if (Process.GetProcessesByName("SpsGui").Length > 1)
            {
                // A start by SpsLauncher is not the user's action, so it quits silently.
                SpsGuiStartupOptions startupOptions = Ioc.Default.GetRequiredService<IStartupOptionsProvider>().Options;
                Logger.Log("Quit because SpsGui is already running. " + startupOptions, true);
                if (!startupOptions.HasTarget)
                {
                    MessageBox.Show(Resources["DuplicateSpsGui"].ToString());
                }

                Shutdown();
                return;
            }

            Logger.Log($"{Process.GetCurrentProcess().ProcessName} administrator privileges: {IsRunningAsAdministrator()}", true);
            // Install SpsLauncher into Program Files and load WinDivert from there, before any packet scan opens it.
            AppConfig.Instance.InstallAndSyncStartup(Ioc.Default.GetRequiredService<IVersionCheckService>().GetVersion());
            // using PacketScanDivert should be later than installing WinDivert into Program Files
            Ioc.Default.GetRequiredService<IConductor>();

            Logger.Log("Loaded background models", true);

            var window = new CoreWindow();
            MainWindow = window;
            window.Show();

            Logger.Log("Now you can see the window", true);
#endif
        }

        private static bool IsRunningAsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            Logger.Log("Exit the app", true);
#if MVVM_APP
            Ioc.Default.GetRequiredService<IOverlayService>().Close();
            Ioc.Default.GetRequiredService<INotificationService>().Close();
            Ioc.Default.GetRequiredService<IPacketScan>().Dispose();
#endif
            AppConfig.Instance.Save();
        }
    }
}
