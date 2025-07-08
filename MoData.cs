using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

        private GlobalKeyboardHook keyboardHook;

        public PathToDiskUsageConverter PathToDiskConverter { set; get; } = null;

        public MoData(IPlayniteAPI api) : base(api)
        {
            settings = new MoDataSettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = false
            };

            keyboardHook = new GlobalKeyboardHook();
            keyboardHook.KeyPressed += OnKeyPressed;

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

            PathToDiskConverter = new PathToDiskUsageConverter(this);

            AddConvertersSupport(new AddConvertersSupportArgs
            {
                SourceName = "MoData",
                Converters = new List<IValueConverter>
                {
                    PathToDiskConverter
                }
            });
        }

        private void OnKeyPressed(Keys key, bool altPressed)
        {
            // Escape to hide MoDetails window
            if (key == Keys.Escape && MoDetailsWindow != null && MoDetailsWindow.IsVisible)
            {
                MoDetailsWindow.Close();
                MoDetailsWindow = null;
            }
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

    public class GlobalKeyboardHook : IDisposable
    {
        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;

        public event Action<Keys, bool> KeyPressed;

        public GlobalKeyboardHook()
        {
            _proc = HookCallback;
            _hookID = SetHook(_proc);
        }

        public void Dispose()
        {
            UnhookWindowsHookEx(_hookID);
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                bool altPressed = (System.Windows.Forms.Control.ModifierKeys & Keys.Alt) == Keys.Alt;

                KeyPressed?.Invoke((Keys)vkCode, altPressed);
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }

}