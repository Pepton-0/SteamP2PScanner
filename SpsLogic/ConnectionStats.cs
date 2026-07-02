using System;
using System.Collections.Generic;
using System.Linq;

namespace SpsLogic
{
    public sealed class ConnectionStats
    {
        private readonly object syncRoot = new object();

        /// <summary>
        /// When the ping profiling starts
        /// </summary>
        public readonly DateTime StartedAt;

        /// <summary>
        /// Name to display in the overlay interface and tables
        /// </summary>
        public readonly string Name;

        private double _min = -1;
        /// <summary>
        /// Min value among pings
        /// </summary>
        public double Min
        {
            get { lock (syncRoot) { return _min; } }
            private set { _min = value; }
        }

        private double _max = -1;
        /// <summary>
        /// Max value among pings
        /// </summary>
        public double Max
        {
            get { lock (syncRoot) { return _max; } }
            private set { _max = value; }
        }

        private double _avg = -1;
        /// <summary>
        /// Average value of the pings
        /// </summary>
        public double Avg
        {
            get { lock (syncRoot) { return _avg; } }
            private set { _avg = value; }
        }

        private double _loss = -1;
        /// <summary>
        /// Packet loss percentage(%) of the pings
        /// </summary>
        public double Loss
        {
            get { lock (syncRoot) { return _loss; } }
            private set { _loss = value; }
        }

        private double _q1 = -1;
        /// <summary>
        /// Median of the pings
        /// </summary>
        public double Q1
        {
            get { lock (syncRoot) { return _q1; } }
            private set { _q1 = value; }
        }

        private double _med = -1;
        /// <summary>
        /// Median of the pings
        /// </summary>
        public double Med
        {
            get { lock (syncRoot) { return _med; } }
            private set { _med = value; }
        }

        private double _q3 = -1;
        /// <summary>
        /// Median of the pings
        /// </summary>
        public double Q3
        {
            get { lock (syncRoot) { return _q3; } }
            private set { _q3 = value; }
        }

        /// <summary>
        /// Might contain -1 for packet loss
        /// Used by hardcoded renderer so no need to be binded.
        /// Not null after constructor
        /// </summary>
        private double[] recentPings;

        public double[] RecentPings
        {
            get { lock (syncRoot) { return recentPings.ToArray(); } }
        }

        private int lossCount = 0;
        private List<double> sortedPings;
        private Queue<double> RecentPingQueue;
        private readonly int RecentPingLimit;

        public ConnectionStats(int recentPingLimit, string name)
        {
            this.Name = name;
            RecentPingQueue = new Queue<double>();
            sortedPings = new List<double>();
            this.RecentPingLimit = recentPingLimit;
            StartedAt = DateTime.Now;
            for (int i = 0; i < RecentPingLimit; i++)
            {
                RecentPingQueue.Enqueue(-2);
            }
            recentPings = RecentPingQueue.ToArray();
        }

        /// <summary>
        /// Update latest ping info
        /// -1 if packet loss
        /// </summary>
        /// <param name="value"></param>
        public void PushPing(double value)
        {
            lock (syncRoot)
            {
                Logger.Log("pushed ping: " + value);
                RecentPingQueue.Enqueue(value);
                while (RecentPingQueue.Count > RecentPingLimit)
                {
                    RecentPingQueue.Dequeue();
                }
                recentPings = RecentPingQueue.ToArray();

                if (value >= 0)
                {
                    if (_min < 0 || value < _min)
                    {
                        Min = value;
                    }
                    if (value > _max)
                    {
                        Max = value;
                    }
                    sortedPings.Add(value);
                    sortedPings.Sort();
                    Avg = RecentPings.Sum(x => x < 0 ? 0 : x) / RecentPings.Count(x => x >= 0);
                    Q1 = Percentile(sortedPings, 0.25);
                    Med = Percentile(sortedPings, 0.5);
                    Q3 = Percentile(sortedPings, 0.75);
                }
                else if (value == -1)
                {
                    lossCount++;
                }

                int sampleCount = lossCount + sortedPings.Count;
                Loss = sampleCount == 0
                    ? 0
                    : 100d * lossCount / sampleCount;
            }
        }

        public TResult ReadValues<TResult>(
            Func<string, DateTime, double, double, double, double, double, double, double, double[], TResult> reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            lock (syncRoot)
            {
                return reader(
                    Name,
                    StartedAt,
                    _min,
                    _max,
                    _avg,
                    _loss,
                    _q1,
                    _med,
                    _q3,
                    recentPings.ToArray());
            }
        }

        private static int Percentile(List<double> sortedValues, double percentile)
        {
            if (sortedValues.Count == 1)
            {
                return (int)sortedValues[0];
            }

            double position = (sortedValues.Count - 1) * percentile;
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            double weight = position - lower;
            return (int)Math.Round(sortedValues[lower] * (1.0 - weight) + sortedValues[upper] * weight);
        }
    }
}
