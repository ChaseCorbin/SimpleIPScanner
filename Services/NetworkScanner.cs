using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
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

            long hostCount = ComputeHostCount(prefix);
            if (hostCount > MaxHostLimit)
                throw new ArgumentException($"Subnet range too large ({hostCount} hosts). Maximum allowed is {MaxHostLimit}.");

            int total = (int)hostCount;
            var results = new ConcurrentBag<ScanResult>();
            int completed = 0;

            var tasks = GetIPRange(network, prefix).Select(ip => Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                await _throttle.WaitAsync(ct);
                try
                {
                    var result = await ScanSingleIP(ip, ct);
                    result.Subnet = cidr;
                    results.Add(result);
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
            if (!TryParseCIDR(cidr, out _, out int prefix)) return 0;
            long count = ComputeHostCount(prefix);
            return count > int.MaxValue ? int.MaxValue : (int)count;
        }

        /// <summary>
        /// Computes the number of usable hosts in a subnet mathematically,
        /// without allocating an IP list.
        /// </summary>
        private static long ComputeHostCount(int prefix)
        {
            if (prefix is < 0 or > 32) return 0;
            if (prefix >= 31) return 0;
            return (1L << (32 - prefix)) - 2;
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
                    result.Hostname = await ResolveHostnameAsync(ip, ct);
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

        /// <summary>
        /// Resolves a hostname for an IP using a two-step fallback chain:
        ///   1. Standard PTR reverse lookup via the OS/configured DNS server.
        ///   2. NetBIOS Node Status request (UDP 137) direct to the target —
        ///      catches Windows machines that aren't registered in DNS.
        /// </summary>
        private static async Task<string> ResolveHostnameAsync(string ip, CancellationToken ct)
        {
            // Step 1 — standard PTR lookup
            try
            {
                var entry = await Dns.GetHostEntryAsync(ip).WaitAsync(ct);
                if (!string.IsNullOrWhiteSpace(entry.HostName) && entry.HostName != ip)
                    return entry.HostName;
            }
            catch (OperationCanceledException) { throw; }
            catch { }

            // Step 2 — NetBIOS Node Status (Windows machines not registered in DNS)
            return await QueryNetBiosNameAsync(ip, ct);
        }

        /// <summary>
        /// Sends a NetBIOS Node Status Request (RFC 1002 §4.2.17) directly to
        /// port 137 on the target host and parses the workstation name from the response.
        /// Effective for Windows devices on the same subnet that don't publish to DNS.
        /// </summary>
        private static async Task<string> QueryNetBiosNameAsync(string ip, CancellationToken ct)
        {
            try
            {
                // Build the 50-byte NBSTAT request packet
                byte[] req = new byte[50];
                req[0] = 0xA5; req[1] = 0x28;         // Transaction ID
                req[4] = 0x00; req[5] = 0x01;          // Question count = 1
                req[12] = 0x20;                         // Encoded name length (32 bytes)
                req[13] = 0x43; req[14] = 0x4B;        // Wildcard '*' half-byte encoded → "CK"
                for (int i = 15; i <= 44; i++) req[i] = 0x41; // 15 spaces → 30 × 'A'
                // req[45] = 0x00  end-of-name (already 0)
                req[47] = 0x21;                         // Type: NBSTAT (0x0021)
                req[49] = 0x01;                         // Class: IN   (0x0001)

                using var udp = new UdpClient();
                await udp.SendAsync(req, req.Length, ip, 137).WaitAsync(ct);

                // 1-second LAN timeout; also respect the outer cancellation token
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(1000);

                var received = await udp.ReceiveAsync(timeoutCts.Token);
                byte[] data = received.Buffer;

                // Response layout (with compressed name pointer — most Windows implementations):
                //   0–11   : Header (12 bytes)
                //   12–49  : Echoed question section (38 bytes)
                //   50–51  : Answer name — 0xC0 0x0C = compressed pointer, or full name (34 bytes)
                //   +2/+34 : Type, Class, TTL, RDLENGTH (10 bytes)
                //   RDATA  : 1-byte name count + N × 18-byte entries (15 name + 1 suffix + 2 flags)
                int numNamesOffset = (data.Length > 50 && data[50] == 0xC0) ? 62 : 94;
                if (data.Length < numNamesOffset + 1) return "N/A";

                int numNames  = data[numNamesOffset];
                int entryBase = numNamesOffset + 1;

                for (int i = 0; i < numNames && entryBase + 18 <= data.Length; i++, entryBase += 18)
                {
                    byte   suffix  = data[entryBase + 15];
                    ushort flags   = (ushort)((data[entryBase + 16] << 8) | data[entryBase + 17]);
                    bool   isGroup = (flags & 0x8000) != 0;

                    // Suffix 0x00, unique = workstation/computer name
                    if (suffix == 0x00 && !isGroup)
                    {
                        string name = Encoding.ASCII.GetString(data, entryBase, 15).TrimEnd(' ', '\0');
                        if (!string.IsNullOrWhiteSpace(name))
                            return name;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }

            return "N/A";
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

        /// <summary>
        /// Yields usable host IPs in the subnet without pre-allocating a full list.
        /// </summary>
        private static IEnumerable<string> GetIPRange(uint network, int prefix)
        {
            uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
            uint networkAddress = network & mask;
            uint broadcast = networkAddress | ~mask;
            for (uint i = networkAddress + 1; i < broadcast; i++)
                yield return UIntToIP(i);
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
