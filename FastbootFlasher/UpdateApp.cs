using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using HuaweiUpdateLibrary;
using HuaweiUpdateLibrary.Core;


namespace FastbootFlasher
{
    internal class UpdateApp
    {
        public static ObservableCollection<Partition> ParseUpdateApp(string filePath)
        {
            var appfile = UpdateFile.Open(filePath, false);
            var Partitions = new ObservableCollection<Partition>();
            foreach (var entry in appfile)
            {
                Partitions.Add(new Partition
                {
                    Index = Partitions.Count+1,
                    Name = entry.FileType.ToLower(),
                    Size = ImageFile.FormatImageSize(entry.FileSize),
                    SourceFile = filePath
                });
            }
            return Partitions;
        }
        public static async Task<Stream?> ExtractPartitionStream(string partitionName, string filePath)
        {
            var appfile = UpdateFile.Open(filePath, false);
            foreach (var entry in appfile)
            {
                if (entry.FileType.ToLower() == partitionName)
                {
                    try
                    {
                        var dataStream = entry.GetDataStream(filePath);
                        return dataStream;
                    }
                    catch
                    {
                        return null;
                    }

                }
            }
            return null;
        }
        public static async Task<bool> ExtractPartitionImage(string partitionName, string filePath, IProgress<double> progress = null)
        {
            var appfile=UpdateFile.Open(filePath, false);
            foreach (var entry in appfile)
            {
                if (entry.FileType.ToLower() == partitionName)
                {
                    Directory.CreateDirectory(@"images");
                    try
                    {
                        if (partitionName == "hisiufs_gpt")
                            partitionName = "ptable";
                        else if (partitionName == "ufsfw")
                            partitionName = "ufs_fw";
                        using var dataStream = entry.GetDataStream(filePath);
                        using var fs = new FileStream($@".\images\{partitionName}.img", FileMode.Create, FileAccess.Write);
                        {
                            
                            var totalSize = (long)entry.FileSize;
                            
                            if (totalSize <= 0)
                            {
                                progress?.Report(100.0);
                                return true;
                            }

                           
                            progress?.Report(0.0);

                            var buffer = new byte[1024 * 1024];
                            long bytesCopied = 0;
                            int read;
                            while ((read = await dataStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fs.WriteAsync(buffer, 0, read);
                                bytesCopied += read;

                                var percent = (double)bytesCopied * 100.0 / totalSize;
                                if (percent > 100.0) percent = 100.0;
                                progress?.Report(percent);
                            }
                            progress?.Report(100.0);

                            return true;
                        }
                    }
                    catch
                    {
                        progress?.Report(0.0);
                        return false;
                    }
                  
                }
            }
            return false;
        }
    }
}
