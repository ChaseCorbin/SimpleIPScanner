# Simple IP Scanner & DNS Benchmark

A modern, high-performance WPF application for network discovery and DNS performance testing.

![Aesthetic](https://img.shields.io/badge/Aesthetic-Premium-94c744)
![Platform](https://img.shields.io/badge/Platform-Windows-0078d4)
![Framework](https://img.shields.io/badge/Framework-.NET%208-512bd4)

## Key Features

### ⚡ Network Scanner
- **Fast Discovery**: Scans entire subnets using optimized asynchronous pinging.
- **Improved OS Detection**: Heuristically detects Windows, Apple, and Linux devices.
- **Enhanced Icons**: Displays modern icons including a custom **Bow Tie icon** for Linux and standard high-quality logos for Windows and Apple.
- **Vendor Lookup**: Automatically identifies device manufacturers (Dell, Apple, HP, etc.) using an IEEE OUI database.
- **Port Scanning**: Automatically scans for common open ports (HTTP, HTTPS, SSH, SMB, etc.) and displays them directly in the grid.
- **Modern UI**: Dark-themed dashboard with green accents and real-time progress bars.

### 🚀 DNS Benchmark
- **Latency Testing**: Real-time monitoring of response times from major DNS providers.
- **Cached vs Live**: Compares standard OS-level (cached) queries against manual UDP (uncached) queries to bypass recursive caches.
- **Multi-Threaded**: Runs tests concurrently across Google (8.8.8.8), Cloudflare (1.1.1.1), OpenDNS, Quad9, and your local default.
- **Average Tracking**: Displays average, min, and max latencies over 15-30 second test durations.

## Screenshots
*(Add screenshots here)*

## How to Build
1. Open the solution in **Visual Studio 2022**.
2. Restore NuGet packages.
3. Build and run in **Release** mode for best performance.

## Technologies Used
- **C# / WPF** (Windows Presentation Foundation)
- **.NET 8.0**
- **Asynchronous Programming** (Task Parallel Library)
- **Raw Socket/UDP** (for Live DNS Benchmarking)
- **XAML Styling** (Custom templates and animations)

---
Developed by **Chase Corbin**
