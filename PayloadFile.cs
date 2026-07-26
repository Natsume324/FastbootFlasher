using System.IO;

namespace FastbootFlasher;

internal static class PayloadFile
{
    public static List<ListViewItem> ParsePayloadFile(string filePath)
    {
        var entries = new List<ListViewItem>();
        var partitions = PayloadBin.ParsePayloadBin(filePath);
        for (int index = 0; index < partitions.Count; index++)
        {
            Partition partition = partitions[index];
            entries.Add(new ListViewItem
            {
                Num = index,
                Part = partition.Name,
                Size = partition.Size,
                Addr = string.Empty,
                Source = filePath
            });
        }
        return entries;
    }

    public static async Task ExtractPartition(string filePath, int index)
    {
        var partitions = PayloadBin.ParsePayloadBin(filePath);
        Partition partition = partitions[index];
        var progress = new Progress<double>(value => MainWindow.pb.Value = value);
        bool success = await PayloadBin.ExtractPartitionImage(partition.Name, filePath, progress);
        if (!success) throw new InvalidDataException($"提取 payload 分区 {partition.Name} 失败。");
    }
}

internal sealed class MetadataInfo
{
    public string ProductName { get; set; } = string.Empty;
    public string AndroidVersion { get; set; } = string.Empty;
    public string SecurityPatch { get; set; } = string.Empty;
    public string VersionName { get; set; } = string.Empty;

    public MetadataInfo(string metadataPath)
    {
        foreach (string line in File.ReadLines(metadataPath))
        {
            string[] parts = line.Split('=', 2);
            if (parts.Length != 2) continue;
            switch (parts[0].Trim())
            {
                case "product_name": ProductName = parts[1].Trim(); break;
                case "android_version": AndroidVersion = parts[1].Trim(); break;
                case "security_patch": SecurityPatch = parts[1].Trim(); break;
                case "version_name": VersionName = parts[1].Trim(); break;
            }
        }
    }
}
