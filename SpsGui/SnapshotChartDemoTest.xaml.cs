using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace SpsGui
{
    public partial class SnapshotChartDemoTest : Window
    {
        public SnapshotChartDemoTest()
        {
            InitializeComponent();
            Snapshots = new ObservableCollection<DemoPingSnapshot>
            {
                DemoPingSnapshot.Create(
                    "Tokyo teammate",
                    new double[] { 24, 26, 25, 27, 24, 28, 26, 25, 27, 23, 26, 25, 29, 24, 26, 27 }),
                DemoPingSnapshot.Create(
                    "Overseas player",
                    new double[] { 112, 126, 139, 118, 154, 132, 121, 168, 145, 119, 137, 176, 128, 151, 134, 160 }),
                DemoPingSnapshot.Create(
                    "Unstable overseas peer",
                    new double[] { 76, -1, 148, 92, 214, -1, 121, 188, 83, -1, 167, 240, 105, -1, 196, 132 })
            };
            DataContext = this;
        }

        public ObservableCollection<DemoPingSnapshot> Snapshots { get; private set; }
    }

    public sealed class DemoPingSnapshot
    {
        private DemoPingSnapshot()
        {
        }

        public static DemoPingSnapshot Create(string name, double[] recentPings)
        {
            if (recentPings == null)
            {
                throw new ArgumentNullException(nameof(recentPings));
            }

            double[] successfulPings = recentPings.Where(value => value >= 0).OrderBy(value => value).ToArray();
            double loss = recentPings.Length == 0
                ? 0.0
                : recentPings.Count(value => value < 0) * 100.0 / recentPings.Length;

            var snapshot = new DemoPingSnapshot
            {
                Name = name,
                RecentPings = recentPings,
                Loss = loss
            };

            if (successfulPings.Length == 0)
            {
                snapshot.Origin = 0.0;
                snapshot.Min = 0.0;
                snapshot.Max = 0.0;
                snapshot.Q1 = 0.0;
                snapshot.Med = 0.0;
                snapshot.Q3 = 0.0;
                snapshot.Avg = 0.0;
                snapshot.Limit = 10.0;
                return snapshot;
            }

            snapshot.Origin = 0.0;
            snapshot.Min = successfulPings.First();
            snapshot.Max = successfulPings.Last();
            snapshot.Q1 = Percentile(successfulPings, 0.25);
            snapshot.Med = Percentile(successfulPings, 0.50);
            snapshot.Q3 = Percentile(successfulPings, 0.75);
            snapshot.Avg = successfulPings.Average();
            snapshot.Limit = snapshot.Max + 10.0;
            return snapshot;
        }

        public string Name { get; private set; }

        public double Min { get; private set; }

        public double Origin { get; private set; }

        public double Limit { get; private set; }

        public double Max { get; private set; }

        public double Q1 { get; private set; }

        public double Med { get; private set; }

        public double Q3 { get; private set; }

        public double Avg { get; private set; }

        public double Loss { get; private set; }

        public double[] RecentPings { get; private set; }

        public string AverageText
        {
            get { return Math.Round(Avg).ToString(CultureInfo.InvariantCulture) + "ms"; }
        }

        public string LossText
        {
            get { return Loss.ToString("0.0", CultureInfo.InvariantCulture) + "%"; }
        }

        private static double Percentile(double[] sortedValues, double percentile)
        {
            if (sortedValues.Length == 1)
            {
                return sortedValues[0];
            }

            double index = (sortedValues.Length - 1) * percentile;
            int lowerIndex = (int)Math.Floor(index);
            int upperIndex = (int)Math.Ceiling(index);
            if (lowerIndex == upperIndex)
            {
                return sortedValues[lowerIndex];
            }

            double weight = index - lowerIndex;
            return sortedValues[lowerIndex] * (1.0 - weight) + sortedValues[upperIndex] * weight;
        }
    }
}
