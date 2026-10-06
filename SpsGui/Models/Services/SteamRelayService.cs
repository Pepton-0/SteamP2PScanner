using Newtonsoft.Json.Linq;
using SpsLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SpsGui.Models.Services
{
    public interface ISteamRelayService
    {
        /// <summary>Fetches Valve's IPv4 prefixes (AS32590) and the app's SDR relay addresses. Never throws.</summary>
        Task LoadAsync(string steamAppId);

        /// <summary>True if the address is in Valve's network or the SDR relay list loaded so far.</summary>
        bool IsRelayAddress(IPAddress address);
    }

    /// <summary>Relay candidate lists fetched online. They are kept in memory only.</summary>
    public class SteamRelayService : ISteamRelayService
    {
        private const string Asn = "AS32590";
        private const string AnnouncedPrefixesUrl = "https://stat.ripe.net/data/announced-prefixes/data.json?resource=" + Asn;
        private const string SdrConfigUrl = "https://api.steampowered.com/ISteamApps/GetSDRConfig/v1?appid=";
        private static readonly HttpClient httpClient = new HttpClient();

        private readonly struct Ipv4Range
        {
            public readonly uint Network;
            public readonly uint Mask;

            public Ipv4Range(uint network, uint mask)
            {
                Network = network & mask;
                Mask = mask;
            }

            public bool Contains(uint hostOrderIpv4)
            {
                return (hostOrderIpv4 & Mask) == Network;
            }
        }

        // Replaced as a whole so readers on other threads never see a half-built list.
        private volatile Ipv4Range[] valveRanges = new Ipv4Range[0];
        private volatile HashSet<uint> sdrRelayAddresses = new HashSet<uint>();

        public Task LoadAsync(string steamAppId)
        {
            return Task.WhenAll(LoadValvePrefixesAsync(), LoadSdrRelaysAsync(steamAppId));
        }

        public bool IsRelayAddress(IPAddress address)
        {
            if (address == null)
            {
                return false;
            }

            IPAddress v4 = address.MapToIPv4();
            if (v4.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            uint hostOrder = ToHostOrder(v4);
            return sdrRelayAddresses.Contains(hostOrder) || valveRanges.Any(range => range.Contains(hostOrder));
        }

        private async Task LoadValvePrefixesAsync()
        {
            try
            {
                string json = await httpClient.GetStringAsync(AnnouncedPrefixesUrl);
                var prefixes = (JObject.Parse(json)["data"]?["prefixes"] as JArray ?? new JArray())
                    .Select(prefix => prefix.Value<string>("prefix"))
                    .Where(prefix => prefix != null && !prefix.Contains(":"))
                    .ToArray();

                valveRanges = ParsePrefixes(prefixes);
                Logger.Log($"Loaded {valveRanges.Length} {Asn} IPv4 prefixes for relay detection.", true);
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to get {Asn} prefixes because {e.Message}", true);
            }
        }

        /// <summary>Some SDR relays are hosted outside AS32590, so they are added individually.</summary>
        private async Task LoadSdrRelaysAsync(string steamAppId)
        {
            if (string.IsNullOrWhiteSpace(steamAppId))
            {
                return;
            }

            try
            {
                string json = await httpClient.GetStringAsync(SdrConfigUrl + Uri.EscapeDataString(steamAppId));
                JObject pops = JObject.Parse(json)["pops"] as JObject;
                if (pops == null)
                {
                    Logger.Log("Steam SDR config did not include pops.", true);
                    return;
                }

                var addresses = new HashSet<uint>();
                foreach (JProperty pop in pops.Properties())
                {
                    foreach (JObject relay in (pop.Value["relays"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        if (IPAddress.TryParse(relay.Value<string>("ipv4"), out IPAddress address) &&
                            address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            addresses.Add(ToHostOrder(address));
                        }
                    }
                }

                sdrRelayAddresses = addresses;
                Logger.Log($"Loaded {addresses.Count} SDR relay addresses for relay detection.", true);
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to get Steam SDR relays because {e.Message}", true);
            }
        }

        private static Ipv4Range[] ParsePrefixes(IEnumerable<string> prefixes)
        {
            var ranges = new List<Ipv4Range>();
            foreach (string prefix in prefixes)
            {
                string[] parts = prefix.Split('/');
                if (parts.Length != 2 ||
                    !IPAddress.TryParse(parts[0], out IPAddress address) ||
                    address.AddressFamily != AddressFamily.InterNetwork ||
                    !int.TryParse(parts[1], out int length) ||
                    length < 0 || length > 32)
                {
                    Logger.Log($"Skipped invalid IPv4 prefix: {prefix}", true);
                    continue;
                }

                uint mask = length == 0 ? 0u : uint.MaxValue << (32 - length);
                ranges.Add(new Ipv4Range(ToHostOrder(address), mask));
            }

            return ranges.ToArray();
        }

        private static uint ToHostOrder(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            return (uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3];
        }
    }
}
