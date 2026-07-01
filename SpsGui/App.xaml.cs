using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using SpsGui.Behaviors;
using SpsGui.Models;
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
            Ioc.Default.ConfigureServices(new ServiceCollection()
                .AddSingleton<IConductor, Conductor>()
                .BuildServiceProvider());

            //new PingOverlayTest().Show();
            //new MainWindow().Show();
            //new PacketScanTest().Show();
            new SteamPacketScanTest().Show();
        }
    }
}
