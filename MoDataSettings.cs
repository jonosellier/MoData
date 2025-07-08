using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;

namespace MoData
{

    public enum BatteryChargeLevel
    {
        Critical,
        Low,
        Medium,
        High
    }

    public class MoDataSettings : ObservableObject
    {
        private NetworkConnection _wifiStrength;
        private List<DiskUsage> _diskUsages = new List<DiskUsage>();
        private string networkName = String.Empty;

        public NetworkConnection WifiStrength
        {
            get => _wifiStrength;
            set
            {
                _wifiStrength = value;
                OnPropertyChanged();
            }
        }

        public List<DiskUsage> DiskUsages
        {
            get => _diskUsages;
            set
            {
                _diskUsages = value ?? new List<DiskUsage>();
                OnPropertyChanged();
            }
        }

        public string NetworkName
        {
            get => networkName;
            set
            {
                networkName = value;
                OnPropertyChanged();
            }
        }


        private string _time;
        public string Time
        {
            get => _time ?? DateTime.Now.ToString("t");
            set
            {
                _time = value;
                OnPropertyChanged();
            }
        }

        private int? _percentCharge;
        public int PercentCharge
        {
            get => _percentCharge ?? (int)(SystemInformation.PowerStatus.BatteryLifePercent * 100);
            set
            {
                _percentCharge = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PercentChargeString)); // Notify dependent property
                OnPropertyChanged(nameof(BatteryCharge)); // Notify dependent property
            }
        }

        public string PercentChargeString
        {
            get => $"{PercentCharge}%";
        }

        public BatteryChargeLevel BatteryCharge
        {
            get
            {
                var charge = PercentCharge;
                if (charge > 85)
                {
                    return BatteryChargeLevel.High;
                }
                else if (charge > 40)
                {
                    return BatteryChargeLevel.Medium;
                }
                else if (charge > 10)
                {
                    return BatteryChargeLevel.Low;
                }
                else
                {
                    return BatteryChargeLevel.Critical;
                }
            }
        }

        private bool? _isCharging;
        public bool IsCharging
        {
            get => _isCharging ?? SystemInformation.PowerStatus.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Charging);
            set
            {
                _isCharging = value;
                OnPropertyChanged();
            }
        }


        public ICommand OpenAppsSettings => new RelayCommand(WindowsSettingsHelper.OpenAppsAndFeatures);
        public ICommand OpenNetworkSettings => new RelayCommand(WindowsSettingsHelper.OpenNetworkSettings);
        public ICommand MoDetails { get; set; }
    }

    public class MoDataSettingsViewModel : ObservableObject, ISettings
    {
        private readonly MoData plugin;
        private MoDataSettings editingClone { get; set; }

        public MoDataSettings settings;
        public MoDataSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
                OnPropertyChanged();
            }
        }

        public MoDataSettingsViewModel(MoData plugin)
        {
            // Injecting your plugin instance is required for Save/Load method because Playnite saves data to a location based on what plugin requested the operation.
            this.plugin = plugin;

            // Load saved settings.
            var savedSettings = plugin.LoadPluginSettings<MoDataSettings>();

            // LoadPluginSettings returns null if no saved data is available.
            if (savedSettings != null)
            {
                Settings = savedSettings;
            }
            else
            {
                Settings = new MoDataSettings();
            }
        }

        public void BeginEdit()
        {
            // Code executed when settings view is opened and user starts editing values.
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            // Code executed when user decides to cancel any changes made since BeginEdit was called.
            // This method should revert any changes made to Option1 and Option2.
            Settings = editingClone;
        }

        public void EndEdit()
        {
            // Code executed when user decides to confirm changes made since BeginEdit was called.
            // This method should save settings made to Option1 and Option2.
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            // Code execute when user decides to confirm changes made since BeginEdit was called.
            // Executed before EndEdit is called and EndEdit is not called if false is returned.
            // List of errors is presented to user if verification fails.
            errors = new List<string>();
            return true;
        }
    }

    public static class WindowsSettingsHelper
    {
        public static void OpenAppsAndFeatures()
        {
            try
            {
                // Windows 10/11 Settings app - Apps & features
                Process.Start("ms-settings:appsfeatures");
            }
            catch (Exception)
            {
                try
                {
                    // Fallback to classic Control Panel Programs and Features
                    Process.Start("appwiz.cpl");
                }
                catch (Exception)
                { }
            }
        }

        public static void OpenNetworkSettings()
        {
            try
            {
                Process.Start("ms-settings:network");
            }
            catch (Exception) { }
        }

        public static void OpenLocationSettings()
        {
            try
            {
                // Windows 10/11 Settings app - Privacy & security > Location
                Process.Start("ms-settings:privacy-location");
            }
            catch (Exception) { }
        }
    }

    public class PathToDiskUsageConverter : IValueConverter
    {
        private readonly MoData plugin;

        public PathToDiskUsageConverter(MoData pluginInstance)
        {
            plugin = pluginInstance;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                return null;

            string installPath = value.ToString();
            string objPath = parameter?.ToString();

            try
            {
                // Get the root directory (drive) of the install path
                string rootPath = Path.GetPathRoot(installPath);
                if (string.IsNullOrEmpty(rootPath))
                    return null;

                // Normalize the root path (remove trailing backslash if present, except for root)
                rootPath = rootPath.TrimEnd('\\');
                if (rootPath.Length == 2 && rootPath[1] == ':') // Drive letter like "C:"
                    rootPath += "\\";

                // Find the matching DiskUsage object
                var matchingDisk = plugin.settings.Settings.DiskUsages.FirstOrDefault(disk =>
                    string.Equals(disk.Label, rootPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(disk.Label, rootPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                    (rootPath.Length >= 2 && string.Equals(disk.Label, rootPath.Substring(0, 2), StringComparison.OrdinalIgnoreCase)));
                switch (objPath)
                {
                    case "UsedSpaceString":
                        return matchingDisk?.UsedSpaceString;
                    case "FreeSpaceString":
                        return matchingDisk?.FreeSpaceString;
                    case "TotalSpaceString":
                        return matchingDisk?.TotalSpaceString;
                    case "UsedPercentageString":
                        return matchingDisk?.UsedPercentageString;
                    case "UsedPercentageAngle":
                        return matchingDisk?.UsedPercentageAngle;
                    case "Label":
                        return matchingDisk?.Label;
                    case null:
                        return matchingDisk?.Label;
                    default:
                        return matchingDisk?.Label;
                }
            }
            catch (Exception ex)
            {
                // Handle invalid paths gracefully
                System.Diagnostics.Debug.WriteLine($"Error in PathToDiskUsageConverter: {ex.Message}");
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("ConvertBack is not supported for PathToDiskUsageConverter");
        }
    }
}