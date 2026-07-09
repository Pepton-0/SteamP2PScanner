using CommunityToolkit.Mvvm.ComponentModel;
using SpsLogic;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpsGui.ViewModels
{
    /// <summary>
    /// Bindable snapshot for a Steam or DNS ping profile row.
    /// </summary>
    public sealed class PingProfileSnapshot : ObservableObject
    {
        private readonly ConnectionStats sourceStats;
        private string state = "-";
        private string name;
        private DateTime startedAt;
        private double min;
        private double max;
        private double avg;
        private double loss;
        private double q1;
        private double med;
        private double q3;
        private double? limitOverride;
        private double[] recentPings = new double[0];
        private ulong? netIdValue;
        private BasePacketArchive packetArchive;
        private bool usingRelay;
        private bool usingDns;

        private PingProfileSnapshot(ConnectionStats stats)
        {
            sourceStats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <summary>
        /// Creates a DNS profile snapshot from a statistics source and initial samples.
        /// </summary>
        /// <param name="stats">Statistics object to back the snapshot. Must not be null.</param>
        /// <param name="initialPings">Initial ping samples. Null is treated as empty.</param>
        /// <returns>A DNS snapshot refreshed from the supplied samples.</returns>
        public static PingProfileSnapshot CreateDns(ConnectionStats stats, IEnumerable<double> initialPings)
        {
            var snapshot = new PingProfileSnapshot(stats)
            {
                PacketArchive = null,
                UsingRelay = false,
                UsingDns = true
            };

            foreach (double ping in initialPings ?? new double[0])
            {
                stats.PushPing(ping);
            }

            snapshot.RefreshFromStats();
            return snapshot;
        }

        /// <summary>
        /// Creates a Steam packet scan snapshot from a packet history.
        /// </summary>
        /// <param name="state">Short state label for the history. Null is displayed as "-".</param>
        /// <param name="netId">Optional packet scan network identifier.</param>
        /// <param name="history">Packet scan history backing the row. Must not be null.</param>
        /// <returns>A Steam snapshot refreshed from the packet history statistics.</returns>
        public static PingProfileSnapshot FromPacketScan(
            string state,
            ulong? netId,
            BasePlayerPingHistory history)
        {
            if (history == null)
            {
                throw new ArgumentNullException(nameof(history));
            }

            var snapshot = new PingProfileSnapshot(history.Stats);
            snapshot.State = string.IsNullOrWhiteSpace(state) ? "-" : state;
            snapshot.NetIdValue = netId;
            snapshot.PacketArchive = history.Archive;
            snapshot.UsingRelay = false;
            snapshot.UsingDns = false;
            snapshot.RefreshFromStats();
            return snapshot;
        }

        /// <summary>
        /// Gets the row state label.
        /// </summary>
        public string State
        {
            get { return state; }
            private set { SetProperty(ref state, value); }
        }

        /// <summary>
        /// Gets the display name for the monitored endpoint.
        /// </summary>
        public string Name
        {
            get { return name; }
            private set { SetProperty(ref name, value); }
        }

        /// <summary>
        /// Gets the time when the statistics source started.
        /// </summary>
        public DateTime StartedAt
        {
            get { return startedAt; }
            private set
            {
                if (SetProperty(ref startedAt, value))
                {
                    OnPropertyChanged(nameof(StartedAtText));
                }
            }
        }

        /// <summary>
        /// Gets the minimum successful ping.
        /// </summary>
        public double Min
        {
            get { return min; }
            private set { SetProperty(ref min, value); }
        }

        /// <summary>
        /// Gets the maximum successful ping.
        /// </summary>
        public double Max
        {
            get { return max; }
            private set
            {
                if (SetProperty(ref max, value))
                {
                    OnPropertyChanged(nameof(Limit));
                }
            }
        }

        /// <summary>
        /// Gets the recent average ping.
        /// </summary>
        public double Avg
        {
            get { return avg; }
            private set
            {
                if (SetProperty(ref avg, value))
                {
                    OnPropertyChanged(nameof(AverageText));
                }
            }
        }

        /// <summary>
        /// Gets packet loss percentage.
        /// </summary>
        public double Loss
        {
            get { return loss; }
            private set
            {
                if (SetProperty(ref loss, value))
                {
                    OnPropertyChanged(nameof(LossText));
                }
            }
        }

        /// <summary>
        /// Gets the first quartile ping.
        /// </summary>
        public double Q1
        {
            get { return q1; }
            private set { SetProperty(ref q1, value); }
        }

        /// <summary>
        /// Gets the median ping.
        /// </summary>
        public double Med
        {
            get { return med; }
            private set { SetProperty(ref med, value); }
        }

        /// <summary>
        /// Gets the third quartile ping.
        /// </summary>
        public double Q3
        {
            get { return q3; }
            private set { SetProperty(ref q3, value); }
        }

        /// <summary>
        /// Gets recent samples where negative values represent packet loss.
        /// </summary>
        public double[] RecentPings
        {
            get { return recentPings; }
            private set { SetProperty(ref recentPings, value); }
        }

        /// <summary>
        /// Gets the packet archive associated with this row, or null for DNS rows.
        /// </summary>
        public BasePacketArchive PacketArchive
        {
            get { return packetArchive; }
            private set { SetProperty(ref packetArchive, value); }
        }

        /// <summary>
        /// Gets the left edge scale value for box plots.
        /// </summary>
        public double Origin
        {
            get { return 0.0; }
        }

        /// <summary>
        /// Gets the right edge scale value for box plots.
        /// </summary>
        public double Limit
        {
            get { return limitOverride.HasValue ? limitOverride.Value : Max + 10.0; }
        }

        /// <summary>
        /// Gets the Steam ID associated with this row.
        /// </summary>
        public ulong SteamID
        {
            get { return sourceStats.Id; }
        }

        /// <summary>
        /// Gets whether Steam relay is used.
        /// </summary>
        public bool UsingRelay
        {
            get { return usingRelay; }
            private set
            {
                if (SetProperty(ref usingRelay, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        /// <summary>
        /// Gets whether this row measures DNS reachability rather than Steam P2P.
        /// </summary>
        public bool UsingDns
        {
            get { return usingDns; }
            private set
            {
                if (SetProperty(ref usingDns, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        /// <summary>
        /// Gets the optional packet scan network identifier.
        /// </summary>
        public ulong? NetIdValue
        {
            get { return netIdValue; }
            private set { SetProperty(ref netIdValue, value); }
        }

        /// <summary>
        /// Gets started time formatted for table cells.
        /// </summary>
        public string StartedAtText
        {
            get { return StartedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture); }
        }

        /// <summary>
        /// Gets average ping text rounded to an integer.
        /// </summary>
        public string AverageText
        {
            get { return Avg < 0 ? "-" : Math.Round(Avg).ToString(CultureInfo.InvariantCulture); }
        }

        /// <summary>
        /// Gets packet loss text rounded to one decimal place.
        /// </summary>
        public string LossText
        {
            get { return Loss < 0 ? "0.0%" : Loss.ToString("0.0", CultureInfo.InvariantCulture) + "%"; }
        }

        /// <summary>
        /// Gets Steam ID text for history display.
        /// </summary>
        public string SteamIDText
        {
            get { return SteamID == 0 ? "-" : SteamID.ToString(CultureInfo.InvariantCulture); }
        }

        /// <summary>
        /// Gets the Steam community profile URL for the Steam ID.
        /// </summary>
        public string SteamProfileUrl
        {
            get
            {
                return SteamID == 0
                    ? null
                    : "https://steamcommunity.com/profiles/" + SteamID.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Gets whether the row has a Steam community profile URL.
        /// </summary>
        public bool HasSteamProfileUrl
        {
            get { return SteamID != 0; }
        }

        /// <summary>
        /// Gets compact relay or DNS text for single-column overlay display.
        /// </summary>
        public string StatusText
        {
            get
            {
                if (UsingRelay && UsingDns)
                {
                    return "relay/dns";
                }

                if (UsingRelay)
                {
                    return "relay";
                }

                return UsingDns ? "dns" : string.Empty;
            }
        }

        /// <summary>
        /// Adds one ping sample and refreshes all bindable values.
        /// </summary>
        /// <param name="value">Ping in milliseconds, or a negative value for packet loss.</param>
        public void PushPing(double value)
        {
            sourceStats.PushPing(value);
            RefreshFromStats();
        }

        /// <summary>
        /// Creates a copy for overlay rows with a shared box plot scale.
        /// </summary>
        /// <param name="limit">Shared right edge scale value in milliseconds.</param>
        /// <returns>A snapshot backed by the same statistics source with a fixed limit.</returns>
        public PingProfileSnapshot CreateOverlaySnapshot(double limit)
        {
            var snapshot = new PingProfileSnapshot(sourceStats)
            {
                State = State,
                PacketArchive = PacketArchive,
                UsingRelay = UsingRelay,
                UsingDns = UsingDns,
                NetIdValue = NetIdValue,
                limitOverride = NormalizeMetric(limit)
            };

            snapshot.RefreshFromStats();
            return snapshot;
        }

        /// <summary>
        /// Refreshes the snapshot from its statistics source.
        /// </summary>
        public void RefreshFromStats()
        {
            sourceStats.ReadValues((nameValue, startedAtValue, minValue, maxValue, avgValue, lossValue, q1Value, medValue, q3Value, recentPingsValue) =>
            {
                Name = nameValue;
                StartedAt = startedAtValue;
                Min = NormalizeMetric(minValue);
                Max = NormalizeMetric(maxValue);
                Avg = avgValue;
                Loss = lossValue;
                Q1 = NormalizeMetric(q1Value);
                Med = NormalizeMetric(medValue);
                Q3 = NormalizeMetric(q3Value);
                RecentPings = recentPingsValue ?? new double[0];
                return 0;
            });
        }

        private static double NormalizeMetric(double value)
        {
            return value < 0 ? 0 : value;
        }
    }
}
