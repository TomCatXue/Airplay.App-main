using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace AirPlay.App.Services;

public sealed class AppSettingsService
{
    private const string FileName = "settings.json";
    private const string StartupTaskId = "AirPlayAppStartup";
    private const string RegistryRunValueName = "AirPlay Windows App";

    public AppSettings Settings { get; private set; }

    public string SettingsFilePath { get; }

    public AppSettingsService()
    {
        SettingsFilePath = Path.Combine(global::Windows.Storage.ApplicationData.Current.LocalFolder.Path, FileName);
        Settings = Load();
        NormalizeNetworkAdapter();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    public async Task SetStartWithWindowsAsync(bool enabled)
    {
        try
        {
            StartupTask startupTask = await StartupTask.GetAsync(StartupTaskId);
            if (enabled && startupTask.State != StartupTaskState.Enabled)
                await startupTask.RequestEnableAsync();
            else if (!enabled && startupTask.State == StartupTaskState.Enabled)
                startupTask.Disable();
        }
        catch
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled)
                key?.SetValue(RegistryRunValueName, $"\"{Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0]}\"");
            else
                key?.DeleteValue(RegistryRunValueName, false);
        }

        Settings.StartWithWindows = enabled;
        Save();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFilePath)) ?? new AppSettings();
        }
        catch
        {
        }

        return new AppSettings();
    }

    private void NormalizeNetworkAdapter()
    {
        try
        {
            if (Settings.NetworkAdapterId is not null &&
                !NetworkInterface.GetAllNetworkInterfaces().Any(nic => nic.Id == Settings.NetworkAdapterId))
            {
                Settings.NetworkAdapterId = null;
            }
        }
        catch
        {
        }
    }
}
