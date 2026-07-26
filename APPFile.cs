using HuaweiUpdateLibrary.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FastbootFlasher
{
    class APPFile
    {
        public static List<ListViewItem> ParseAPPFile(string filePath, out string versionInfo)
        {
            versionInfo = string.Empty;
            var entries = new List<ListViewItem>();
            var appfile = UpdateFile.Open(filePath, false);
            var entry = appfile.Entries[2];
            using var dataStream = entry.GetDataStream(filePath);
            using var reader = new StreamReader(dataStream, Encoding.UTF8);

            versionInfo = reader.ReadToEnd();

            for (int i = 0; i < appfile.Entries.Count; i++)
            {
                entry = appfile.Entries[i];
                entries.Add(new ListViewItem
                {
                    Num = i,
                    Part = entry.FileType.ToLower(),
                    Size = MainWindow.FormatSize(entry.FileSize),
                    Addr = $"0x{entry.FileSize:X8}",
                    Source = filePath
                });
            }
            
            return entries;
        }

        public static async Task ExtractPartition(string FilePath, int index)
        {
            var APPFile = UpdateFile.Open(FilePath, false);
            var entry = APPFile.Entries[index];
            Directory.CreateDirectory(@".\images");

            long totalSize = entry.FileSize;
            long currentBytes = 0;
            string partition = entry.FileType.ToLowerInvariant();
            if (partition == "hisiufs_gpt")
                partition = "ptable";
            if (partition == "ufsfw")
                partition = "ufs_fw";
            if (partition == "super")
            {
                var superEntries = APPFile.Entries
                    .Select((value, entryIndex) => (value, entryIndex))
                    .Where(x => string.Equals(x.value.FileType, "super", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (superEntries.Count > 1)
                    partition = $"super.{superEntries.FindIndex(x => x.entryIndex == index) + 1}";
            }

            using (var entryStream = entry.GetDataStream(FilePath))
            using (var fileStream = new FileStream(@$".\images\{partition}.img", FileMode.Create, FileAccess.Write))
            {
                byte[] buffer = new byte[1024 * 1024];
                int bytesRead;

                while ((bytesRead = await entryStream.ReadAsync(buffer)) > 0)
                {
                    fileStream.Write(buffer, 0, bytesRead);
                    currentBytes += bytesRead;

                    MainWindow.pb.Value = (double)currentBytes / totalSize * 100;
                }
            }
        }

        public static async Task MergerSuperSparse(IReadOnlyList<string>? fragments = null, string super = @".\images\super.img")
        {
            fragments ??= Directory.GetFiles(@".\images", "super.*.img")
                .OrderBy(path => ParseSuperPartNumber(path))
                .ToArray();
            var progress = new Progress<double>(value => MainWindow.pb.Value = value);
            await SparseImageMerger.MergeAsync(fragments, super, progress);
        }

        private static int ParseSuperPartNumber(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return int.TryParse(name[(name.LastIndexOf('.') + 1)..], out int number) ? number : int.MaxValue;
        }
    }
}
