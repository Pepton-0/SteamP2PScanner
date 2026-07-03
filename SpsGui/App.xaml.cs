#define MVVM_APP

using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using SpsGui.Behaviors;
using SpsGui.Models;
using SpsGui.ViewModels;
using SpsGui.Views;
using SpsLogic;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
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
            Logger.DebugLog("Hello world!");

            // Prepare MVVM application
#if MVVM_APP
            Ioc.Default.ConfigureServices(new ServiceCollection()
                .AddSingleton<IConductor, Conductor>()
                .AddSingleton<ISteamAppFinder, SteamAppFinder>()
                .AddSingleton<IPacketScan, PacketScan>()
                .AddTransient<CoreWindowViewModel>()
                .BuildServiceProvider());
#else
            //new PingOverlayTest().Show();
            //new MainWindow().Show();
            // new PacketScanTest().Show();
            //new SteamDetectTest().Show();
            //new SteamDetectorV2Test().Show();
            // new SteamAppFinderTest().Show();
            new SteamPacketScanTest().Show();
#endif
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
#if MVVM_APP
            Ioc.Default.GetRequiredService<IConductor>();

            var window = new CoreWindow();
            MainWindow = window;
            window.Show();
#endif
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
#if MVVM_APP
            Ioc.Default.GetRequiredService<IPacketScan>().Dispose();
#endif
        }
    }
}
