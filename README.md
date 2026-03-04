<img width="1527" height="875" alt="image" src="https://github.com/user-attachments/assets/a88b5a15-7017-4788-8178-1c721415a119" />
<img width="1647" height="926" alt="image" src="https://github.com/user-attachments/assets/b61f32ee-e270-482f-b7b1-139718edb005" />
<img width="1529" height="918" alt="image" src="https://github.com/user-attachments/assets/1a9d6c7e-cdd7-496a-9f2c-f086d2b3fffa" />
<img width="1529" height="918" alt="image" src="https://github.com/user-attachments/assets/bbe6d598-ae1d-47d5-8a0c-ec33c7f0b492" />



# Simple IP Scanner & DNS Benchmark

A modern, high-performance WPF application for network discovery, DNS performance testing, visual traceroute monitoring, live packet capture analysis, and internet speed testing.

![Version](https://img.shields.io/badge/Version-2.1.4-10B981)
![Platform](https://img.shields.io/badge/Platform-Windows-0078d4)
![Framework](https://img.shields.io/badge/Framework-.NET%208-512bd4)

---

## 📥 Download & Quick Start

### 1. Download the Installer
Go to the [**Releases**](https://github.com/ChaseCorbin/SimpleIPScanner/releases) page and download `SimpleIPScanner-win-Setup.exe`.

### 2. Run the Installer
Double-click `SimpleIPScanner-win-Setup.exe`. It will install the app to `%LocalAppData%\SimpleIPScanner\` and create a Start Menu shortcut automatically.

*Note: Windows SmartScreen may show a warning because the app is not code-signed. Click "More Info" → "Run anyway".*

### 3. Automatic Updates
Once installed, the app silently checks for new releases on startup. When an update is available, a banner appears at the top of the window — click **Update & Restart** to apply it in seconds. No manual downloads needed.

> **Portable version**: A `SimpleIPScanner-win-Portable.zip` is also available on the Releases page for users who prefer a no-install option. Note that the portable version does not support automatic updates.

---

## ✨ Features

### ⚡ Network Scanner
- **Multi-Subnet Scanning**: Add multiple CIDR ranges as chips and scan them all in one session. Results are merged into a single list; each IP shows its subnet prefix inline (e.g. `192.168.0.25/24`).
- **Flexible CIDR Support**: Scan any range from /24 down to /32 — including single-host (/32) and point-to-point (/31) targets.
- **Auto-Detect Subnets**: Automatically discovers all connected subnets from your active network interfaces.
- **Fast Discovery**: Concurrent async pinging with semaphore throttling (up to 50 simultaneous probes).
- **OS Fingerprinting**: Detects Windows, Apple, and Linux devices via TTL heuristics.
- **Vendor Detection**: Identifies device manufacturers from MAC OUI (IEEE database).
- **Hostname Resolution**: Two-step fallback — standard DNS PTR lookup, then NetBIOS Node Status (UDP 137) for Windows machines not registered in DNS.
- **Configurable Port Scanning**: Choose from three modes — **Common** (21 well-known ports: HTTP, HTTPS, SSH, RDP, SMB, etc.), **All** (full 1–65535 TCP scan via sequential batching), or **Custom** (scan only the ports you specify, comma-separated).
- **Right-Click Device Tools**: Right-click any scanned device to instantly **Open in Browser**, send a **Wake-on-LAN** magic packet, launch **Remote Desktop (RDP)**, or open a **PowerShell Remote Session** (`Enter-PSSession`).

### 🚀 DNS Benchmark
- **Live Latency Testing**: Compare cached and uncached response times across major DNS providers (Google, Cloudflare, OpenDNS, Quad9, and more).
- **Uncached Queries**: Direct UDP queries with randomized transaction IDs and unique subdomains to bypass caches and measure true resolver speed.
- **Configurable Duration**: Choose 5, 15, or 30 second test runs.
- **Real-time Stats**: Min, Max, and Average latency tracked live.

### 🗺️ Visual Traceroute
- **Live Hop-by-Hop Monitoring**: Continuous traceroute to one or more targets simultaneously.
- **Multi-Chart View**: All monitored targets display their latency charts simultaneously on one scrollable page — no clicking through sessions.
- **Performance Timeline**: Interactive latency chart with 1m / 5m / 10m / 1h / 2h zoom windows, mouse-over crosshair with timestamp/latency tooltip, click-drag panning through history, and **right-click drag to zoom** into any specific time range; a **Reset Zoom** button returns to the live 5-minute view.
- **Unlimited Runtime with History Archive**: Traces run indefinitely with no time limit. Data is stored in three tiers: the last 60 minutes at full 1-second resolution, the prior hour at 10 points per minute, and anything older at 1 point per minute — pan back hours or days without any memory growth.
- **Event Marker Overlays**: Timeouts and latency spikes ≥ 100 ms are preserved at their exact timestamps as colored vertical lines overlaid on the chart (red = timeout, orange = spike), keeping every notable event visible even in long archive views. Hovering near a marker snaps the tooltip to show its exact value.
- **Route Path Sidebar**: Click any session to reveal its hop-by-hop path in the sidebar; hop latencies are color-coded green / orange / red as they approach 200ms for at-a-glance health assessment.
- **Timeout Log**: Timeouts are automatically logged to `traceroute_timeouts.log` in the app directory with timestamps, making it easy to review outages after a long monitoring run.
- **Multi-Target**: Add any number of hosts and monitor them in parallel.
- **Packet Loss & Jitter**: Track packet loss percentage and average latency per session.

### 📡 Packet Capture & Analysis
- **Live Top Talkers**: Captures traffic on any network interface in real time and ranks hosts by total bandwidth consumed — see who is using the most traffic at a glance.
- **Auto-Detect Primary Interface**: The interface dropdown automatically identifies and pre-selects the active NIC (the one with a default gateway), marked with a ★ Primary badge alongside its IP address.
- **Protocol Breakdown**: Click the expand arrow on any host row to reveal a per-protocol breakdown (TCP/UDP/ICMP and service names like HTTPS, DNS, SMB) with a relative bandwidth bar for each — capped at the top 8 protocols for a clean overview.
- **Export for AI Analysis**: Export the full capture session to a structured JSON file — includes capture metadata, every host's byte counts, and the complete protocol breakdown per host. Designed to be pasted directly into an AI assistant for deeper traffic analysis.
- **Npcap Detection**: Requires [Npcap](https://npcap.com) (free, by the Nmap Project). If Npcap is not installed, an in-app banner prompts the user with a direct download link rather than blocking the rest of the app.

### 🌐 Internet Speed Test
- **Live Throughput Chart**: Real-time download and upload speed chart updating every 250 ms — cyan line for download, orange for upload. Values are smoothed with a 1-second rolling average to filter per-chunk noise while staying responsive.
- **Parallel TCP Streams**: Opens 8 concurrent connections (matching Ookla's methodology) to saturate the link and produce accurate readings rather than being throttled by a single TCP connection's congestion window.
- **Progress Phases**: Clearly labeled test phases — Pinging → Download → Upload → Done — with a progress bar spanning the full run.
- **Server Latency Breakdown**: Pings five well-known resolvers (Cloudflare, Google, Quad9, OpenDNS × 2) in parallel before the throughput test and displays best-of-3 latency for each.
- **Peak & Average Stats**: Tracks live Mbps alongside the peak sample and final average for both download and upload.
- **Configurable Duration**: Choose 5 s / 10 s / 20 s per phase via the duration selector.
- **Reliable Endpoints**: Uses Cloudflare's speed test infrastructure (`speed.cloudflare.com`) as the primary source with automatic fallback to Hetzner's public test servers if Cloudflare is unavailable.

### ⚙️ Settings
- **About**: View the current app version and open the GitHub repository.
- **Auto-Update Toggle**: Enable or disable the startup update check. Manual "Check Now" always available regardless of the setting.
- **Port Scan Mode**: Choose between Common ports, All ports (1–65535), or a Custom port list. The selected mode is applied to every device scanned.
- Settings persist across sessions in `%AppData%\SimpleIPScanner\settings.json`.

---

## 🛠️ For Developers

### Building from Source
1. Clone the repository: `git clone https://github.com/ChaseCorbin/SimpleIPScanner.git`
2. Open the solution in **Visual Studio 2022** or use the CLI.
3. Ensure **.NET 8 SDK** is installed.
4. Build: `dotnet build` or build in Release mode in Visual Studio.

### Publishing a Release
Requires the `vpk` CLI (one-time setup: `dotnet tool install -g vpk`).

```powershell
.\publish_release.ps1 -Version "2.0.0" -GitHubToken "ghp_xxxx"
```

This publishes a self-contained build, packages it with Velopack (`vpk pack`), and uploads the installer and update feed to GitHub Releases (`vpk upload github`). Output goes to `bin\Release\VelopackOutput\`.

### Technology Stack
- **Language**: C# 12
- **UI Framework**: WPF (Windows Presentation Foundation)
- **Runtime**: .NET 8 (self-contained)
- **Networking**: `System.Net.Sockets`, `System.Net.NetworkInformation`, `System.Net.Ping`
- **Packet Capture**: [SharpPcap](https://github.com/dotpcap/sharppcap) + [PacketDotNet](https://github.com/dotpcap/packetnet) — live capture and packet parsing; requires [Npcap](https://npcap.com) at runtime
- **Auto-Update**: [Velopack](https://velopack.io) — installer packaging and GitHub Releases update feed

---

## 📋 Changelog

### v2.1.4
- **Three-tier history compression** — traceroute data is now stored at three resolutions: the last 60 minutes at full 1-second precision, the prior hour at 10 points per minute (one point every 6 seconds), and anything older at 1 point per minute; panning back 2+ hours shows a smooth baseline without sacrificing mid-range detail
- **Event marker overlays** — timeouts and latency spikes ≥ 100 ms are now preserved at their exact second-precision timestamps as vertical lines overlaid on the chart: red for timeouts, orange for spikes; the main polyline stays clean while every event remains individually visible no matter how far back you pan
- **Snap-to event markers on hover** — when the mouse is within 12 pixels of a timeout or spike marker line, the crosshair tooltip snaps to that exact event and shows its precise timestamp and latency (or "Timeout"); this makes it easy to inspect individual high-ping or drop events without hunting pixel-by-pixel
- **Resizable Route Path sidebar** — the Route Path hop panel now grows with the window and can be resized by dragging the divider between the session list and the hop panel; the hop list fills all available height with a scrollbar rather than being capped at a fixed size

### v2.1.3
- **Unlimited traceroute runtime** — traces no longer stop after 2 hours; the session runs indefinitely without any restart required
- **History archive with compression** — data older than 60 minutes is automatically compressed to 1 point per minute and moved to an archive rather than discarded; panning back hours or days into the past works seamlessly, and memory stays flat (~240 KB for a full 24-hour session)

### v2.1.2
- **1-second rolling average** — the live chart and stat card numbers display a rolling average of the last four 250 ms samples rather than raw instantaneous readings, removing per-chunk noise while keeping the display responsive

### v2.1.1
- **Internet Speed Test tab** — new tab that measures download speed, upload speed, and latency to five well-known servers; a live dual-line chart (cyan = download, orange = upload) updates every 250 ms alongside peak/average stat cards and a per-server ping breakdown
- **Parallel TCP streams** — speed test opens 8 concurrent connections (matching Ookla's methodology) instead of a single stream; a single TCP connection is throttled by its congestion window ÷ RTT and severely under-reports fast connections — parallel streams saturate the link accurately
- **Cloudflare endpoint fix** — resolved HTTP 403 errors caused by missing CORS headers; requests to `speed.cloudflare.com` now include the required `Origin` and `Referer` headers
- **Download fallback chain** — if Cloudflare is unavailable, the download test automatically falls back to Hetzner's public speed test servers (US East → Germany) without any user action required

### v2.1.0
- **Chart zoom** — right-click and drag horizontally on any traceroute latency chart to draw a selection box and zoom into that exact time range; a semi-transparent cyan highlight shows the selection as you drag
- **Reset Zoom button** — an orange **⊟ Reset Zoom** button appears in the chart toolbar whenever a zoom is active; clicking it exits zoom mode and returns to the live 5-minute view
- **Tooltip accuracy in zoom** — the mouse-over crosshair and timestamp/latency popup now correctly map to the zoomed time window rather than the full interval duration
- **↩ Live compatibility** — the existing ↩ Live button also clears zoom state, so either control can be used to return to live tracking

### v2.0.0
- **Packet Capture & Analysis tab** — new dedicated tab for live network traffic capture powered by [SharpPcap](https://github.com/dotpcap/sharppcap) and [Npcap](https://npcap.com); requires Npcap to be installed (free, by the Nmap Project); an in-app banner with a direct download link is shown if Npcap is not detected
- **Top Talkers view** — captures packets in promiscuous mode and ranks all active hosts by total bandwidth in real time; updates every second with sent, received, total bytes, and packet count per host
- **Primary interface auto-detection** — the interface picker scans for NICs with a default gateway and pre-selects the most likely active adapter, labeled with its IPv4 address and a ★ Primary badge
- **Per-host protocol breakdown** — expand any host row to reveal a top-8 protocol overview (TCP/UDP/ICMP, with service names like HTTPS, DNS, RDP, SMB) and a proportional bandwidth bar per protocol; collapses cleanly without interfering with the grid's scroll behavior
- **AI-ready JSON export** — the Export button saves a structured JSON file containing capture metadata (interface, start time, duration, total packets) and the full per-host breakdown including all protocol entries (not capped); intended for pasting into an AI assistant for deeper traffic analysis
- **Traceroute memory and performance overhaul** — resolved a memory leak and UI lockup that occurred after several hours of continuous monitoring; root cause was an O(n²) WPF rendering loop where each incoming ping triggered N+1 chart redraws instead of one; fixed by replacing `ObservableCollection` with an atomic list swap (single `PropertyChanged` per update), reducing the ping rate from 5 per second to 1 per second (capping 2-hour history at 7,200 points instead of 36,000), and adding a hard point-count cap with efficient front-trimming via `RemoveRange`

### v1.6.1
- **Multi-chart view** — all monitored traceroute targets now display their latency charts simultaneously on a single scrollable page; no longer need to click through each session individually
- **Timeout logging** — timeouts are written to `traceroute_timeouts.log` in the app directory with a timestamp per entry, making it easy to review which hosts went down during a long monitoring session
- **Route Path sidebar** — selecting a session in the session list reveals a collapsible Route Path panel in the left sidebar showing each hop's number, IP, hostname, and latency; the full chart area is preserved on the right
- **Hop latency color-coding** — hop latency values in the Route Path panel are color-coded: green below 100ms, orange 100–199ms, red 200ms+, and gray for non-responding hops; mirrors the chart color scale for quick identification of slow hops
- **Mouse-over chart tooltip** — hovering over any chart card displays a crosshair line with a popup showing the exact timestamp and latency at that point
- **Per-card controls** — each chart card has its own Resume/Stop toggle, interval selector (1m / 5m / 10m / 1h / 2h), Pause, and ↩ Live buttons; drag-to-pan works independently per chart

### v1.6.0
- **Configurable port scan mode** — new setting in the Settings dialog to choose how ports are scanned per device: **Common** scans 21 well-known ports (HTTP, HTTPS, SSH, RDP, SMB, and more); **All** performs a full TCP sweep of ports 1–65535 using sequential 500-port batches (note: expect longer scan times per device); **Custom** scans only the comma-separated list of ports you enter. The selected mode persists across sessions.
- **Custom port list** — when Custom mode is selected, a text input appears in Settings where you can enter any port numbers separated by commas; invalid entries are silently ignored and duplicates are deduplicated.
- **Hostname resolution fix** — corrected a regression that caused garbled hostnames (e.g. `??c?`) for some devices; DNS PTR → NetBIOS Node Status fallback chain is restored to its original reliable behavior.

### v1.5.2
- **Right-click context menu** — right-clicking a device row now only opens the menu when an actual device is clicked; right-clicking empty space in the results grid no longer triggers the menu
- **Row hover highlight** — device rows now highlight on mouse-over, making it clear which device is being targeted before right-clicking
- **Selectable cell text** — all output columns (IP, Hostname, MAC, Vendor, Open Ports, Ping) now support text selection so values can be highlighted and copied directly from the grid
- **Removed cell focus outline** — the dotted rectangle that appeared when clicking a cell has been removed for a cleaner look

### v1.5.1
- **Scrollbar styling** — custom slim scrollbar replaces the Windows default; rounded thumb transitions from subtle gray → dark green → bright green on hover/drag to match the app accent color
- **Corner rendering fix** — resolved a white halo artifact at the rounded corners of all DataGrid panels (scanner, DNS, traceroute hops); corners now blend cleanly against the dark background
- **Column header chrome** — replaced the default WPF Aero2 raised-header look with a flat dark template that matches the rest of the UI; column resizing remains fully functional

### v1.5.0
- **Settings menu** — new ⚙ gear button in the header opens a settings dialog with an About section (version, GitHub link) and an Updates section (toggle auto-check on startup, manual "Check Now" button); preferences saved to `%AppData%\SimpleIPScanner\settings.json`
- **Wake-on-LAN** — right-click any scanned device and send a WOL magic packet directly to its MAC address to remotely power it on
- **Open in Browser** — right-click any device to open `http://<ip>` in the default browser (useful for printers, routers, cameras, and other web-accessible devices)
- **App icon** — custom switch-stack icon now appears in the taskbar, Alt+Tab, Start Menu, and window title bar; embedded as a multi-resolution `.ico` (16 / 32 / 48 / 64 / 256 px)

### v1.4.2
- **Right-click remote tools** — right-click any scanned device to launch **Remote Desktop (RDP)** or open a **PowerShell remote session** (`Enter-PSSession`) directly from the results grid; uses hostname when available, falls back to IP
- **IP column with CIDR prefix** — subnet is now shown inline as part of the IP (e.g. `192.168.0.25/24`), removing the redundant Subnet column
- **Extended CIDR range** — scanner now accepts /24 through /32; /32 scans a single host, /31 scans both point-to-point addresses (RFC 3021)
- **Traceroute chart pan** — click and drag the latency chart to scroll through historical data; "↩ Live" button snaps back to the live view
- **Traceroute X-axis accuracy** — time labels now reflect the actual visible window instead of raw data timestamps
- UI fixes: dark-themed right-click menu with no icon gutter and green accent hover

### v1.4.1
- Patch release — internal fixes and update pipeline verification

### v1.4.0
- **Auto-update** — app silently checks GitHub Releases on startup; an in-app banner lets you apply updates with one click (powered by Velopack)
- **Installer distribution** — replaced portable single-file exe with `SimpleIPScanner-win-Setup.exe`; installs to `%LocalAppData%`, creates Start Menu shortcut, and handles future updates automatically
- Portable zip still provided for no-install users

### v1.3.0
- **Custom DNS servers** — add your own DNS servers to the benchmark alongside the built-in providers
- **Chart X-axis labels** — traceroute latency timeline now shows readable time markers on the X-axis
- UI polish across multiple panels

### v1.2.0
- **Multi-subnet / VLAN scanning** — add multiple CIDR ranges as chips; sequential scan merges results into one list with a Subnet column
- **Auto-detect subnets** — discovers all connected NICs and populates the chip list automatically
- **Visual Traceroute tab** — continuous multi-target traceroute with live latency timeline chart, packet loss, and per-session stats
- **NetBIOS hostname fallback** — resolves Windows machine names (UDP 137) when DNS PTR returns nothing
- **Switch stack logo** — vector icon rendered in-app; no image file dependency; appears in header, title bar, and taskbar
- **Emerald color theme** — replaced lime-green accent with emerald (#10B981 / #059669)
- **Dark-themed ComboBox** — DNS duration picker now matches the app style
- **Performance**: async DNS resolution, `ConcurrentBag` for results, yield-return IP range, cached `SortKey`, `Dispatcher.BeginInvoke` in hot paths
- **Reliability**: randomized DNS transaction IDs, `volatile` OUI map reference, `Ping` disposal fix in traceroute service

### v1.0.0
- Initial release: Network Scanner, DNS Benchmark, OS detection, MAC vendor lookup, port scanning

---

Developed by **Chase Corbin**
