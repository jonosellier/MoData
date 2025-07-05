using System;
using System.Net.NetworkInformation;
using System.Linq;
using ManagedNativeWifi;

namespace MoData
{
    public enum NetworkConnection
    {
        None,
        Weak,
        Medium,
        Strong,
        Full,
        Wired
    }

    public class NetworkInfo
    {
        public NetworkConnection Connection { get; set; } = NetworkConnection.None;
        private string ssid;

        public string SSID
        {
            get => ssid;
            set => ssid = value;
        }

        public string NetworkName
        {
            get
            {
                if (Connection == NetworkConnection.None)
                {
                    return "Not Connected";
                }
                if (Connection == NetworkConnection.Wired)
                {
                    return "Wired Connection";
                }
                return ssid;
            }
        }
    }

    public class NetworkDataService
    {
        public NetworkInfo GetCurrentNetworkInfo()
        {
            try
            {
                // Get connected SSIDs first
                var connectedSsids = NativeWifi.EnumerateConnectedNetworkSsids().ToList();

                if (connectedSsids.Any())
                {
                    var connectedSsid = connectedSsids.First().ToString();

                    // Find the corresponding network info from available networks
                    var availableNetworks = NativeWifi.EnumerateAvailableNetworks();
                    var matchingNetwork = availableNetworks
                        .FirstOrDefault(x => x.Ssid?.ToString() == connectedSsid);

                    if (matchingNetwork != null)
                    {
                        return new NetworkInfo
                        {
                            Connection = ConvertSignalQualityToEnum(matchingNetwork.SignalQuality),
                            SSID = connectedSsid
                        };
                    }
                    else
                    {
                        // We have a connected SSID but can't find it in available networks
                        // This can happen, so return with default medium signal
                        return new NetworkInfo
                        {
                            Connection = NetworkConnection.Medium,
                            SSID = connectedSsid
                        };
                    }
                }
            }
            catch (UnauthorizedAccessException e)
            {
                throw e;
            }
            catch (Exception)
            {
                // Handle other exceptions and continue to wired check
            }

            // Check if there's an active wired connection as a fallback
            if (HasActiveWiredConnection())
            {
                return new NetworkInfo { Connection = NetworkConnection.Wired };
            }

            return new NetworkInfo { Connection = NetworkConnection.None };
        }

        private bool HasActiveWiredConnection()
        {
            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

                foreach (NetworkInterface ni in interfaces)
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet &&
                        ni.OperationalStatus == OperationalStatus.Up)
                    {
                        var ipProperties = ni.GetIPProperties();
                        var unicastAddresses = ipProperties.UnicastAddresses;

                        bool hasValidIP = unicastAddresses.Any(addr =>
                            addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                            !System.Net.IPAddress.IsLoopback(addr.Address) &&
                            !addr.Address.ToString().StartsWith("169.254"));

                        if (hasValidIP)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // If we can't determine wired status, return false
            }

            return false;
        }

        private NetworkConnection ConvertSignalQualityToEnum(int signalQuality)
        {
            // SignalQuality is typically 0-100
            if (signalQuality >= 80)
                return NetworkConnection.Full;
            else if (signalQuality >= 60)
                return NetworkConnection.Strong;
            else if (signalQuality >= 40)
                return NetworkConnection.Medium;
            else if (signalQuality >= 20)
                return NetworkConnection.Weak;
            else
                return NetworkConnection.None;
        }
    }
}