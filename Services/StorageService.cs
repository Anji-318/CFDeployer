using System;
using System.IO;
using System.Text.Json;
using CFDeployer.Models;

namespace CFDeployer.Services
{
    public static class StorageService
    {
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CFDeployer");
        private static readonly string DataFile = Path.Combine(AppDataPath, "data.json");

        public static void Initialize()
        {
            if (!Directory.Exists(AppDataPath))
            {
                Directory.CreateDirectory(AppDataPath);
            }
        }

        public static AppData? LoadAppData()
        {
            try
            {
                if (File.Exists(DataFile))
                {
                    var json = File.ReadAllText(DataFile);
                    return JsonSerializer.Deserialize<AppData>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load error: {ex.Message}");
            }
            return null;
        }

        public static void SaveAppData(AppData data)
        {
            try
            {
                Initialize();
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(DataFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save error: {ex.Message}");
            }
        }

        public static void Reset()
        {
            try
            {
                if (File.Exists(DataFile))
                {
                    File.Delete(DataFile);
                }
            }
            catch { }
        }
    }
}
