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
            Logger.Log("Hello world to everyone");
            Logger.Log("This is remembered forever", true);
            //new PingOverlayTest().Show();
            //new MainWindow().Show();
            //new PacketScanTest().Show();
            new SteamPacketScanTest().Show();
        }
    }
}
