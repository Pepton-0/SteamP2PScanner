using SpsLogic;
using SpsLogic.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SpsGui
{
    public partial class PacketScanTest : Window
    {
        private const int ClassicStunRequestLength = 56;
        private const int ClassicStunHeaderLength = 20;

        private readonly DispatcherTimer updateTimer;
        private readonly RandomNumberGenerator random = RandomNumberGenerator.Create();
        private IPacketScan packetScan;
        private ulong? registeredNetId;
        private IPEndPoint lastEndpoint;
        private int timerTickCount;
        private int autoUpdateRequestCount;
        private int manualUpdateRequestCount;
        private int executedUpdateCount;
        private string lastUpdateResult = "not called";

        public ObservableCollection<PingProfileSnapshot> ActiveHistories { get; } =
            new ObservableCollection<PingProfileSnapshot>();

        public ObservableCollection<PingProfileSnapshot> OldHistories { get; } =
            new ObservableCollection<PingProfileSnapshot>();

        public PacketScanTest()
        {
            InitializeComponent();
            DataContext = this;

            updateTimer = new DispatcherTimer(DispatcherPriority.Background);
            updateTimer.Interval = TimeSpan.FromSeconds(1);
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();

            UpdatePortFromSelectedService();
            DispatcherTextBlock.Text = $"UI thread {Dispatcher.Thread.ManagedThreadId}, timer interval 1s";
            RefreshUpdateStatus();
            AppendLog("PacketScanTest initialized.");
            AppendLog("Recommended first service: stunserver2025.stunprotocol.org:3478. Stuntman documents RFC3489 backward compatibility.");
        }

        private void ConstructButton_Click(object sender, RoutedEventArgs e)
        {
            ConstructPacketScan();
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            TryRegisterCurrentEndpoint();
        }

        private void UnregisterButton_Click(object sender, RoutedEventArgs e)
        {
            TryUnregisterCurrentEndpoint();
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            manualUpdateRequestCount++;
            TryUpdatePacketScan("manual");
        }

        private void DisposeButton_Click(object sender, RoutedEventArgs e)
        {
            DisposePacketScan("manual");
        }

        private void ServiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePortFromSelectedService();
        }

        private async void LifecycleButton_Click(object sender, RoutedEventArgs e)
        {
            await RunLifecycleTestAsync();
        }

        private async void SendClassicStunButton_Click(object sender, RoutedEventArgs e)
        {
            await SendClassicStunAsync();
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            timerTickCount++;

            if (AutoUpdateCheckBox.IsChecked == true)
            {
                autoUpdateRequestCount++;
                TryUpdatePacketScan("timer");
            }

            RefreshHistories();
            RefreshUpdateStatus();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            updateTimer.Stop();
            DisposePacketScan("window closing");
            random.Dispose();
        }

        private bool ConstructPacketScan()
        {
            try
            {
                if (packetScan != null)
                {
                    AppendLog("Construct skipped: PacketScan already exists.");
                    return true;
                }

                packetScan = new PacketScan(ReadPatienceMilliseconds());
                StateTextBlock.Text = "PacketScan: constructed";
                AppendLog("Construct succeeded.");
                RefreshHistories();
                return true;
            }
            catch (Exception ex)
            {
                packetScan = null;
                StateTextBlock.Text = "PacketScan: construct failed";
                AppendException("Construct failed", ex);
                return false;
            }
        }

        private bool TryRegisterCurrentEndpoint()
        {
            try
            {
                if (packetScan == null && !ConstructPacketScan())
                {
                    return false;
                }

                IPEndPoint endpoint = ResolveEndpointFromUi();
                ulong netId = PacketScan.CalcNetId(endpoint.Address, checked((ushort)endpoint.Port));
                packetScan.Register(netId, GetServiceHostText());

                registeredNetId = netId;
                lastEndpoint = endpoint;
                EndpointTextBlock.Text = endpoint.ToString();
                NetIdTextBlock.Text = netId.ToString(CultureInfo.InvariantCulture);
                StateTextBlock.Text = "PacketScan: registered";
                AppendLog($"Register succeeded: endpoint={endpoint}, netId={netId}");
                RefreshHistories();
                return true;
            }
            catch (Exception ex)
            {
                AppendException("Register failed", ex);
                RefreshHistories();
                return false;
            }
        }

        private bool TryUnregisterCurrentEndpoint()
        {
            try
            {
                if (packetScan == null)
                {
                    AppendLog("Unregister skipped: PacketScan is null.");
                    return false;
                }

                ulong netId;
                if (registeredNetId.HasValue)
                {
                    netId = registeredNetId.Value;
                }
                else
                {
                    IPEndPoint endpoint = ResolveEndpointFromUi();
                    netId = PacketScan.CalcNetId(endpoint.Address, checked((ushort)endpoint.Port));
                }

                packetScan.Unregister(netId);
                registeredNetId = null;
                StateTextBlock.Text = "PacketScan: unregistered";
                AppendLog($"Unregister succeeded: netId={netId}");
                RefreshHistories();
                return true;
            }
            catch (Exception ex)
            {
                AppendException("Unregister failed", ex);
                RefreshHistories();
                return false;
            }
        }

        private bool TryUpdatePacketScan(string source)
        {
            try
            {
                if (packetScan == null)
                {
                    lastUpdateResult = $"{source}: skipped because PacketScan is null at {DateTime.Now:HH:mm:ss}";
                    RefreshUpdateStatus();
                    return false;
                }

                packetScan.Update();
                executedUpdateCount++;
                lastUpdateResult = $"{source}: executed at {DateTime.Now:HH:mm:ss}";
                StateTextBlock.Text = $"PacketScan: updated by {source}";
                RefreshHistories();
                RefreshUpdateStatus();
                return true;
            }
            catch (Exception ex)
            {
                lastUpdateResult = $"{source}: threw {ex.GetType().Name} at {DateTime.Now:HH:mm:ss}";
                AppendException($"Update failed ({source})", ex);
                RefreshHistories();
                RefreshUpdateStatus();
                return false;
            }
        }

        private void RefreshUpdateStatus()
        {
            if (UpdateStatusTextBlock == null)
            {
                return;
            }

            UpdateStatusTextBlock.Text =
                $"Update: timer ticks={timerTickCount}, auto requests={autoUpdateRequestCount}, " +
                $"manual requests={manualUpdateRequestCount}, executed={executedUpdateCount}, last={lastUpdateResult}";
        }

        private void DisposePacketScan(string reason)
        {
            try
            {
                if (packetScan == null)
                {
                    return;
                }

                packetScan.Dispose();
                packetScan = null;
                registeredNetId = null;
                StateTextBlock.Text = "PacketScan: disposed";
                AppendLog($"Dispose succeeded: {reason}.");
                RefreshHistories();
            }
            catch (Exception ex)
            {
                packetScan = null;
                registeredNetId = null;
                StateTextBlock.Text = "PacketScan: dispose threw";
                AppendException($"Dispose failed ({reason})", ex);
                RefreshHistories();
            }
        }

        private async Task RunLifecycleTestAsync()
        {
            AppendLog("Lifecycle test started.");

            DisposePacketScan("before lifecycle test");
            TryUpdatePacketScan("before construct");
            ConstructPacketScan();
            TryUpdatePacketScan("after construct, before register");
            TryRegisterCurrentEndpoint();
            TryRegisterCurrentEndpoint();
            TryUpdatePacketScan("after duplicate register");

            await SendClassicStunAsync();
            await Task.Delay(1200);
            TryUpdatePacketScan("after classic STUN send");

            TryUnregisterCurrentEndpoint();
            TryUnregisterCurrentEndpoint();
            TryUpdatePacketScan("after unregister");
            DisposePacketScan("after lifecycle unregister");
            TryUpdatePacketScan("after dispose");

            AppendLog("Lifecycle test finished.");
        }

        private async Task SendClassicStunAsync()
        {
            try
            {
                if (RegisterBeforeSendCheckBox.IsChecked == true && !registeredNetId.HasValue)
                {
                    TryRegisterCurrentEndpoint();
                }

                IPEndPoint endpoint = lastEndpoint ?? ResolveEndpointFromUi();
                int localPort = ReadLocalPort();
                byte[] request = BuildClassicStunRequest();
                ClassicStunTransactionId id = new ClassicStunTransactionId(request, 4);

                AppendLog($"Sending classic STUN request: endpoint={endpoint}, bytes={request.Length}, id={id}");

                string result = await Task.Run(() => SendUdpAndWait(endpoint, request, localPort));
                ResponseTextBlock.Text = result;
                AppendLog(result);
                RefreshHistories();

                if (result.StartsWith("No UDP response", StringComparison.Ordinal))
                {
                    double patienceMs = ReadPatienceMilliseconds();
                    AppendLog($"PacketScan loss is recorded only after patience timeout. Waiting {patienceMs:0}ms, then forcing Update once.");
                    await Task.Delay(TimeSpan.FromMilliseconds(patienceMs + 200));
                    TryUpdatePacketScan("after UDP receive timeout");
                }
            }
            catch (Exception ex)
            {
                AppendException("Send classic STUN failed", ex);
            }
        }

        private string SendUdpAndWait(IPEndPoint endpoint, byte[] request, int localPort)
        {
            using (var client = localPort > 0
                ? new UdpClient(localPort)
                : new UdpClient())
            {
                client.Client.ReceiveTimeout = 1500;
                client.Connect(endpoint);
                client.Send(request, request.Length);

                try
                {
                    IPEndPoint remote = null;
                    byte[] response = client.Receive(ref remote);
                    return $"Response received: remote={remote}, bytes={response.Length}";
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        return "No UDP response within 1500ms.";
                    }

                    throw;
                }
            }
        }

        private byte[] BuildClassicStunRequest()
        {
            var bytes = new byte[ClassicStunRequestLength];

            WriteUInt16BigEndian(bytes, 0, 0x0001);
            WriteUInt16BigEndian(bytes, 2, ClassicStunRequestLength - ClassicStunHeaderLength);

            var transactionBytes = new byte[16];
            random.GetBytes(transactionBytes);
            Buffer.BlockCopy(transactionBytes, 0, bytes, 4, transactionBytes.Length);

            // Optional dummy attribute. This keeps the payload size aligned with the Steam classicstun size heuristic.
            WriteUInt16BigEndian(bytes, 20, 0x8022);
            WriteUInt16BigEndian(bytes, 22, 32);

            return bytes;
        }

        private static void WriteUInt16BigEndian(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)((value >> 8) & 0xFF);
            bytes[offset + 1] = (byte)(value & 0xFF);
        }

        private IPEndPoint ResolveEndpointFromUi()
        {
            string host = GetServiceHostText();
            int port = ReadRemotePort();

            IPAddress address;
            if (!IPAddress.TryParse(host, out address))
            {
                address = Dns.GetHostAddresses(host)
                    .FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork);
            }

            if (address == null)
            {
                throw new InvalidOperationException($"Could not resolve IPv4 address: {host}");
            }

            var endpoint = new IPEndPoint(address, port);
            ulong netId = PacketScan.CalcNetId(endpoint.Address, checked((ushort)endpoint.Port));
            EndpointTextBlock.Text = endpoint.ToString();
            NetIdTextBlock.Text = netId.ToString(CultureInfo.InvariantCulture);
            return endpoint;
        }

        private void UpdatePortFromSelectedService()
        {
            if (RemotePortTextBox == null)
            {
                return;
            }

            if (ServiceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string port)
            {
                RemotePortTextBox.Text = port;
            }
        }

        private string GetServiceHostText()
        {
            if (!string.IsNullOrWhiteSpace(ServiceComboBox.Text))
            {
                return ServiceComboBox.Text.Trim();
            }

            if (ServiceComboBox.SelectedItem is ComboBoxItem item)
            {
                return item.Content?.ToString();
            }

            throw new InvalidOperationException("STUN service host must not be empty.");
        }

        private int ReadRemotePort()
        {
            if (!int.TryParse(RemotePortTextBox.Text, out int port) || port <= 0 || port > ushort.MaxValue)
            {
                throw new InvalidOperationException("Remote port must be between 1 and 65535.");
            }

            return port;
        }

        private int ReadLocalPort()
        {
            if (!int.TryParse(LocalPortTextBox.Text, out int port) || port < 0 || port > ushort.MaxValue)
            {
                throw new InvalidOperationException("Local port must be between 0 and 65535.");
            }

            return port;
        }

        private double ReadPatienceMilliseconds()
        {
            if (!double.TryParse(PatienceTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                value <= 0)
            {
                throw new InvalidOperationException("Patience ms must be a positive number.");
            }

            return value;
        }

        private void RefreshHistories()
        {
            ActiveHistories.Clear();

            if (packetScan == null)
            {
                return;
            }

            var activeSnapshots = new List<PingProfileSnapshot>();
            packetScan.ForEachActiveHistory((state, netId, history) =>
            {
                activeSnapshots.Add(PingProfileSnapshot.FromPacketScan(state, netId, history));
            });

            foreach (PingProfileSnapshot snapshot in activeSnapshots)
            {
                ActiveHistories.Add(snapshot);
            }

            foreach (PacketScan.PlayerPingHistory history in packetScan.TakeUnseenOldHistories())
            {
                OldHistories.Add(PingProfileSnapshot.FromPacketScan("Old", null, history));
            }
        }

        private void AppendException(string label, Exception ex)
        {
            AppendLog($"{label}: {ex.GetType().Name}: {ex.Message}");
        }

        private void AppendLog(string message)
        {
            LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            LogTextBox.ScrollToEnd();
        }
    }

}
