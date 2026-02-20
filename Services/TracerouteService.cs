using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SimpleIPScanner.Models;

namespace SimpleIPScanner.Services
{
    public class TracerouteService
    {
        private const int MaxHops = 30;
        private const int Timeout = 2000;

        public async Task RunTraceOnceAsync(TraceSession session, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(session.Destination)) return;

            string dest = session.Destination;
            using var pingSender = new Ping();
            PingOptions options = new PingOptions(1, true);
            byte[] buffer = Encoding.ASCII.GetBytes("TracerouteData");

            for (int ttl = 1; ttl <= MaxHops; ttl++)
            {
                if (ct.IsCancellationRequested) break;

                options.Ttl = ttl;
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    PingReply reply = await pingSender.SendPingAsync(dest, Timeout, buffer, options);
                    sw.Stop();

                    // Update or add hop
                    TraceHop? hop = null;
                    if (session.Hops.Count >= ttl)
                    {
                        hop = session.Hops[ttl - 1];
                    }
                    else
                    {
                        var newHop = new TraceHop { HopNumber = ttl };
                        App.Current.Dispatcher.Invoke(() => session.Hops.Add(newHop));
                        hop = newHop;
                    }

                    if (reply.Status == IPStatus.TtlExpired || reply.Status == IPStatus.Success)
                    {
                        hop.IP = reply.Address?.ToString() ?? "Unknown";
                        hop.Latency = sw.ElapsedMilliseconds;
                        hop.IsTimeout = false;

                        // Async DNS lookup — fire-and-forget, guarded against null address
                        var hopRef = hop;
                        var addr = reply.Address;
                        if (addr is not null)
                        {
                            _ = Task.Run(async () => {
                                try {
                                    var hostEntry = await Dns.GetHostEntryAsync(addr);
                                    App.Current.Dispatcher.Invoke(() => hopRef.Hostname = hostEntry.HostName);
                                } catch { }
                            }, ct);
                        }
                    }
                    else
                    {
                        hop.IP = "*";
                        hop.Hostname = "Request timed out.";
                        hop.IsTimeout = true;
                        hop.Latency = -1;
                    }

                    if (reply.Status == IPStatus.Success)
                    {
                        break;
                    }
                }
                catch
                {
                    // Ignore and let high-frequency loop handle latency
                }
            }
        }
    }
}
