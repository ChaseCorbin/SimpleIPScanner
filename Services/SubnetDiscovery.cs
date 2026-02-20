using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SimpleIPScanner.Services
{
    /// <summary>
    /// Discovers all directly-connected IPv4 subnets from the machine's
    /// active network interfaces. Each VLAN/NIC appears as a separate subnet
    /// as long as the OS has an IP address configured on it.
    /// </summary>
    public static class SubnetDiscovery
    {
        /// <summary>
        /// Returns a deduplicated list of (CIDR, NIC label) pairs for every
        /// usable IPv4 subnet reachable from this machine's network adapters.
        /// Skips loopback, tunnel adapters, and point-to-point links (/31 and /32).
        /// </summary>
        public static List<(string Cidr, string Label)> GetConnectedSubnets()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<(string, string)>();

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                         && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel))
            {
                var ipProps = nic.GetIPProperties();
                foreach (var addr in ipProps.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                {
                    try
                    {
                        // Compute network address from IP and subnet mask
                        byte[] ipBytes = addr.Address.GetAddressBytes();
                        byte[] maskBytes = addr.IPv4Mask.GetAddressBytes();

                        uint ip   = (uint)(ipBytes[0] << 24 | ipBytes[1] << 16 | ipBytes[2] << 8 | ipBytes[3]);
                        uint mask = (uint)(maskBytes[0] << 24 | maskBytes[1] << 16 | maskBytes[2] << 8 | maskBytes[3]);
                        uint net  = ip & mask;

                        int prefix = CountBits(mask);

                        // Skip host routes and point-to-point links
                        if (prefix >= 31) continue;

                        byte[] netBytes = new[]
                        {
                            (byte)((net >> 24) & 0xFF),
                            (byte)((net >> 16) & 0xFF),
                            (byte)((net >> 8)  & 0xFF),
                            (byte)(net & 0xFF)
                        };

                        string cidr = $"{new IPAddress(netBytes)}/{prefix}";

                        if (seen.Add(cidr))
                        {
                            // Shorten very long NIC names for display
                            string label = nic.Name.Length > 20 ? nic.Name[..17] + "…" : nic.Name;
                            results.Add((cidr, label));
                        }
                    }
                    catch { }
                }
            }

            return results;
        }

        private static int CountBits(uint value)
        {
            int count = 0;
            while (value != 0) { value <<= 1; count++; }
            return count;
        }
    }
}
