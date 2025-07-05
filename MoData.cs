using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Markup;

namespace MoData
{
    public class MoData : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public MoDataSettingsViewModel settings { get; set; }

        public override Guid Id { get; } = Guid.Parse("5a0a672a-998a-4cf3-94ea-44543de5afcb");

        public IPlayniteAPI Api { get; }

        public static Window MoDetailsWindow { get; private set; }

        private System.Threading.Timer updateTimer;

        private readonly object timerLock = new object();

        public NetworkDataService WifiService = new NetworkDataService();
        DiskStatsService DiskService = new DiskStatsService();

        public MoData(IPlayniteAPI api) : base(api)
        {
            settings = new MoDataSettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = false
            };

            Api = api;
            settings.Settings.MoDetails = new RelayCommand(() =>
            {
                ShowMoDataWindow(Api, settings.Settings);
            });
            AddSettingsSupport(new AddSettingsSupportArgs
            {
                SourceName = "MoData",
                SettingsRoot = $"settings.Settings"
            });
        }

        private void UpdateData()
        {
            try
            {
                var networkInfo = new NetworkInfo
                {
                    Connection = NetworkConnection.None,
                    SSID = string.Empty
                };
                try
                {
                    networkInfo = WifiService.GetCurrentNetworkInfo();
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    logger.Error(uaEx, "UnauthorizedAccessException getting network info - likely due to missing app capabilities");
                    if (Api.Notifications.Messages.FirstOrDefault(n => n.Id == "modata_network_access") == null)
                    {
                        Api.Notifications.Add(new NotificationMessage(
                            "modata_network_access",
                            "MoData plugin cannot access network information due to Location Permissions Settings. Click to open Location Settings to allow Desktop Applications location access",
                            NotificationType.Error,
                            () => WindowsSettingsHelper.OpenLocationSettings()
                        ));
                    }
                }
                var diskUsages = DiskService.GetAllDiskUsage();
                settings.Settings.WifiStrength = networkInfo.Connection;
                settings.Settings.DiskUsages = diskUsages;
                settings.Settings.NetworkName = networkInfo.NetworkName;

                // Update battery-related properties
                settings.Settings.Time = DateTime.Now.ToString("t");
                settings.Settings.PercentCharge = (int)(SystemInformation.PowerStatus.BatteryLifePercent * 100);
                settings.Settings.IsCharging = SystemInformation.PowerStatus.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Charging);

                logger.Debug("Data updated successfully");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error updating data");
            }
        }

        private static void ShowMoDataWindow(IPlayniteAPI api, MoDataSettings data)
        {
            var parent = api.Dialogs.GetCurrentAppWindow();
            MoDetailsWindow = api.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false
            });

            MoDetailsWindow.Height = parent.Height;
            MoDetailsWindow.Width = parent.Width;
            MoDetailsWindow.Title = "MoData";

            string xamlString = @"
            <Viewbox Stretch=""Uniform"" 
                     xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                     xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
                <Grid Width=""1920"" Height=""1080"">
                    <ContentControl x:Name=""MoDataWindow""
                                    Focusable=""False""
                                    Style=""{DynamicResource MoDataWindowStyle}"" />
                </Grid>
            </Viewbox>";

            // Parse the XAML string
            var element = (FrameworkElement)XamlReader.Parse(xamlString);


            // Set content of a window. Can be loaded from xaml, loaded from UserControl or created from code behind
            MoDetailsWindow.Content = element;

            // Set data context if you want to use MVVM pattern
            MoDetailsWindow.DataContext = parent.DataContext;

            // Set owner if you need to create modal dialog window
            MoDetailsWindow.Owner = parent;
            MoDetailsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Use Show or ShowDialog to show the window
            MoDetailsWindow.ShowDialog();
        }

        private void TimerCallback(object state)
        {
            // Run update on a background thread to avoid blocking
            Task.Run(() => UpdateData());
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            lock (timerLock)
            {
                // Update data immediately on start
                Task.Run(() => UpdateData());

                // Start timer to update every 10s
                updateTimer = new System.Threading.Timer(TimerCallback, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

                logger.Info("MoData plugin started - updating data every minute");
            }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            lock (timerLock)
            {
                // Stop and dispose the timer
                if (updateTimer != null)
                {
                    updateTimer.Dispose();
                    updateTimer = null;
                    logger.Info("MoData plugin stopped - timer disposed");
                }
            }
        }

        public override void OnControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            if (MoDetailsWindow != null && MoDetailsWindow.IsVisible && args.Button == ControllerInput.B && args.State == ControllerInputState.Pressed)
            {
                MoDetailsWindow.Close();
                MoDetailsWindow = null;
            }
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunSettings)
        {
            return new MoDataSettingsView();
        }
    }
}