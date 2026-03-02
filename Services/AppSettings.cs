using System;
using System.IO;
using System.Text.Json;

namespace SimpleIPScanner.Services
{
    public enum PortScanMode { Common, All, Custom }

    public class AppSettings
    {
        public bool AutoCheckUpdates { get; set; } = true;
        public PortScanMode PortScanMode { get; set; } = PortScanMode.Common;
        public string CustomPorts { get; set; } = "";

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SimpleIPScanner", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                string path = SettingsPath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                string path = SettingsPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(this,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
