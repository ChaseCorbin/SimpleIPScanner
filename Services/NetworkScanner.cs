using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SimpleIPScanner.Models;

namespace SimpleIPScanner.Services
{
    public class NetworkScanner
    {
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(uint destIP, uint srcIP, byte[] macAddr, ref int macAddrLen);

        private readonly SemaphoreSlim _throttle = new(50);
        private const int PingRetries = 3;
        private const int PingTimeoutMs = 1000;
        public const int MaxHostLimit = 65536; // Limit to /16 to prevent OOM/exhaustion

        public event Action<int, int>? ProgressChanged;
        public event Action<ScanResult>? DeviceScanned;

        public async Task<List<ScanResult>> ScanSubnetAsync(string cidr, CancellationToken ct)
        {
            if (!TryParseCIDR(cidr, out uint network, out int prefix))
                throw new ArgumentException("Invalid CIDR format.");

            List<string> ips = GetIPRange(network, prefix);
            if (ips.Count > MaxHostLimit)
                throw new ArgumentException($"Subnet range too large ({ips.Count} hosts). Maximum allowed is {MaxHostLimit}.");

            var results = new List<ScanResult>();
            var lockObj = new object();
            int completed = 0;
            int total = ips.Count;

            var tasks = ips.Select(ip => Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                await _throttle.WaitAsync(ct);
                try
                {
                    var result = await ScanSingleIP(ip, ct);
                    lock (lockObj) { results.Add(result); }
                    DeviceScanned?.Invoke(result);
                }
                finally
                {
                    _throttle.Release();
                    int done = Interlocked.Increment(ref completed);
                    ProgressChanged?.Invoke(done, total);
                }
            }, ct)).ToArray();

            await Task.WhenAll(tasks);
            return results.OrderBy(r => r.SortKey).ToList();
        }

        /// <summary>
        /// Re-scan a single IP address. Used by the per-row refresh button.
        /// </summary>
        public static async Task<ScanResult> RescanIP(string ip)
        {
            return await ScanSingleIP(ip, CancellationToken.None);
        }

        public static int GetHostCount(string cidr)
        {
            if (!TryParseCIDR(cidr, out uint network, out int prefix)) return 0;
            return GetIPRange(network, prefix).Count;
        }

        /// <summary>
        /// Pings an IP up to 3 times. If any attempt succeeds, resolves hostname, MAC, and vendor.
        /// Always returns a result (online or offline).
        /// </summary>
        private static async Task<ScanResult> ScanSingleIP(string ip, CancellationToken ct)
        {
            var result = new ScanResult { IP = ip };

            try
            {
                for (int attempt = 0; attempt < PingRetries; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using Ping ping = new();
                        var reply = await ping.SendPingAsync(ip, PingTimeoutMs);

                        if (reply.Status == IPStatus.Success)
                        {
                            result.IsOnline = true;
                            result.PingMs = (int)reply.RoundtripTime;
                            if (reply.Options != null)
                                result.TTL = reply.Options.Ttl;
                            break;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }

                    // Small delay between retries
                    if (attempt < PingRetries - 1)
                        await Task.Delay(200, ct);
                }

                if (result.IsOnline)
                {
                    result.Hostname = ResolveHostname(ip);
                    result.MAC = GetMacViaSendARP(ip);
                    result.Vendor = MacVendorLookup.Lookup(result.MAC);
                }
                else
                {
                    // Even for offline devices, try to get MAC from ARP cache
                    // (they may have been seen recently)
                    result.MAC = GetMacViaSendARP(ip);
                    if (result.MAC != "N/A")
                        result.Vendor = MacVendorLookup.Lookup(result.MAC);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }

            return result;
        }

        private static string ResolveHostname(string ip)
        {
            try
            {
                var entry = Dns.GetHostEntry(ip);
                return entry.HostName;
            }
            catch { return "N/A"; }
        }

        private static string GetMacViaSendARP(string ip)
        {
            try
            {
                IPAddress addr = IPAddress.Parse(ip);
                byte[] ipBytes = addr.GetAddressBytes();
                uint ipUint = BitConverter.ToUInt32(ipBytes, 0);

                byte[] macAddr = new byte[6];
                int macAddrLen = macAddr.Length;

                int result = SendARP(ipUint, 0, macAddr, ref macAddrLen);
                if (result == 0 && macAddrLen > 0)
                    return string.Join("-", macAddr.Take(macAddrLen).Select(b => b.ToString("X2")));
            }
            catch { }
            return "N/A";
        }

        private static bool TryParseCIDR(string cidr, out uint network, out int prefix)
        {
            network = 0; prefix = 24;
            if (string.IsNullOrWhiteSpace(cidr)) return false;

            var parts = cidr.Split('/');
            if (parts.Length != 2) return false;
            if (!IPAddress.TryParse(parts[0], out IPAddress? ip)) return false;
            if (!int.TryParse(parts[1], out prefix)) return false;
            if (prefix < 0 || prefix > 32) return false;

            byte[] bytes = ip.GetAddressBytes();
            if (bytes.Length != 4) return false; // Only IPv4 supported

            network = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
            return true;
        }

        private static List<string> GetIPRange(uint network, int prefix)
        {
            var result = new List<string>();
            uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
            uint networkAddress = network & mask;
            uint broadcast = networkAddress | ~mask;
            for (uint i = networkAddress + 1; i < broadcast; i++)
                result.Add(UIntToIP(i));
            return result;
        }

        public static string GetActiveSubnet()
        {
            try
            {
                // Find the first interface that is Up, not Loopback, and has an IPv4 address
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0) // Prefer ones with gateway
                    .FirstOrDefault();

                if (nic == null) return "192.168.1.0/24";

                var ipProps = nic.GetIPProperties();
                var ipv4 = ipProps.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

                if (ipv4 == null) return "192.168.1.0/24";

                // meaningful fix: calculate network address from IP & Subnet Mask
                uint ip = BitConverter.ToUInt32(ipv4.Address.GetAddressBytes().Reverse().ToArray(), 0);
                uint mask = BitConverter.ToUInt32(ipv4.IPv4Mask.GetAddressBytes().Reverse().ToArray(), 0);
                uint network = ip & mask;

                // correct bit counting for prefix length
                uint m = mask;
                int prefixLength = 0;
                while (m != 0) { m <<= 1; prefixLength++; }

                byte[] networkBytes = BitConverter.GetBytes(network).Reverse().ToArray();
                string networkIp = new IPAddress(networkBytes).ToString();

                return $"{networkIp}/{prefixLength}";
            }
            catch
            {
                return "192.168.1.0/24";
            }
        }

        private static string UIntToIP(uint ip)
            => $"{(ip >> 24) & 0xFF}.{(ip >> 16) & 0xFF}.{(ip >> 8) & 0xFF}.{ip & 0xFF}";
    }
}
