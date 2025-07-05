using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MoData
{
    public class DiskUsage
    {
        public long TotalSpace { get; set; }
        public long UsedSpace { get; set; }
        public long FreeSpace { get; set; }
        public string Label { get; set; }
        public double UsedPercentage => TotalSpace > 0 ? (double)UsedSpace / TotalSpace * 100 : 0;
        public double UsedPercentageAngle => UsedPercentage * 3.6F;
        public string UsedPercentageString => $"{UsedPercentage:F2}%";
        public string TotalSpaceString => FormatBytes(TotalSpace);
        public string UsedSpaceString => FormatBytes(UsedSpace);
        public string FreeSpaceString => FormatBytes(FreeSpace);

        private string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int idx = 0;
            double size = bytes;
            while (size >= 1024 && idx < units.Length - 1)
            {
                size /= 1024;
                idx++;
            }
            return $"{size:F2} {units[idx]}";
        }

        public DiskUsage(string label, long totalSpace, long usedSpace, long freeSpace)
        {
            TotalSpace = totalSpace;
            UsedSpace = usedSpace;
            FreeSpace = freeSpace;
            Label = label ?? "Unknown Disk";
        }
    }

    public class DiskStatsService
    {
        public List<DiskUsage> GetAllDiskUsage()
        {
            var diskUsageList = new List<DiskUsage>();

            try
            {
                // Get all logical drives
                DriveInfo[] drives = DriveInfo.GetDrives();

                foreach (DriveInfo drive in drives)
                {
                    try
                    {
                        // Only process ready drives (mounted and accessible)
                        if (drive.IsReady)
                        {
                            long totalSpace = drive.TotalSize;
                            long freeSpace = drive.AvailableFreeSpace;
                            long usedSpace = totalSpace - freeSpace;

                            // Get drive letter (C:\, D:\, etc.)
                            string label = drive.Name; // This already returns format like "C:\"


                            var diskUsage = new DiskUsage(label, totalSpace, usedSpace, freeSpace);
                            diskUsageList.Add(diskUsage);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Handle individual drive errors (e.g., removable media not inserted)
                        string label = $"{drive.Name} (Error: {ex.Message})";
                        var diskUsage = new DiskUsage(label, 0, 0, 0);
                        diskUsageList.Add(diskUsage);
                    }
                }
            }
            catch (Exception)
            {
                // Handle general errors
            }

            return diskUsageList;
        }

        // Alternative method to get only ready/accessible drives
        public List<DiskUsage> GetReadyDiskUsage()
        {
            var diskUsageList = new List<DiskUsage>();

            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();

                foreach (DriveInfo drive in drives.Where(d => d.IsReady))
                {
                    try
                    {
                        long totalSpace = drive.TotalSize;
                        long freeSpace = drive.AvailableFreeSpace;
                        long usedSpace = totalSpace - freeSpace;

                        string label = !string.IsNullOrEmpty(drive.VolumeLabel)
                            ? $"{drive.VolumeLabel} ({drive.Name})"
                            : drive.Name;

                        var diskUsage = new DiskUsage(label, totalSpace, usedSpace, freeSpace);
                        diskUsageList.Add(diskUsage);
                    }
                    catch (Exception)
                    {
                        // Skip drives that can't be accessed
                        continue;
                    }
                }
            }
            catch (Exception)
            {
                // Handle general errors
            }

            return diskUsageList;
        }
    }
}
