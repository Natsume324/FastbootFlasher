using FastbootCS;
using HuaweiUpdateLibrary;
using HuaweiUpdateLibrary.Core;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


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
                    Index = Partitions.Count,
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
        public static async Task<bool> ExtractPartitionImage(int index, string filePath, IProgress<double> progress = null)
        {
            var appfile = UpdateFile.Open(filePath, false);
            var entry = appfile.Entries[index];
           
            try
            {
                string partitionName=entry.FileType.ToLower();
                if (entry.FileType.ToLower() == "hisiufs_gpt")
                    partitionName = "ptable";
                else if (entry.FileType.ToLower() == "ufsfw")
                    partitionName = "ufs_fw";
                else if(entry.FileType.ToLower() == "super")
                {
                    if (appfile.Entries[index+1].FileType.ToLower()=="super")
                    {
                        partitionName= "super.1";
                    }
                    if (File.Exists($@".\images\super.1.img"))
                    {
                        partitionName = "super.2";
                    }
                }
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
            return false;
        }
            
        
        public static async Task MergerSperImage(IProgress<double> progress=null)
        {
            var superPath1 = $@".\images\super.1.img";
            var superPath2 = $@".\images\super.2.img";
            if (!File.Exists(superPath1) || !File.Exists(superPath2))
                throw new FileNotFoundException("super parts not found");

            // 保证 pathA 是较小的（或按原逻辑置换）
            var len1 = new FileInfo(superPath1).Length;
            var len2 = new FileInfo(superPath2).Length;
            string smallPath = superPath1, largePath = superPath2;
            long smallLen = len1, largeLen = len2;
            if (len1 > len2)
            {
                smallPath = superPath2;
                largePath = superPath1;
                smallLen = len2;
                largeLen = len1;
            }

            string outputPath = $@".\images\super.img";
            long totalSize = smallLen + largeLen;
            progress?.Report(0.0);
            long bytesCopied = 0;

            // 池中申请 4MB 缓冲区
            const int bufferSize = 4 * 1024 * 1024;
            var pool = ArrayPool<byte>.Shared;
            var buffer = pool.Rent(bufferSize);

            try
            {
                // 1) 从 largePath 复制到最后一个 chunk 之前的全部数据（保留最后一个 chunk 不拷贝）
                long lastChunkOffset;
                using (var fs = new FileStream(largePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan))
                using (var reader = new BinaryReader(fs, Encoding.Default, leaveOpen: true))
                {
                    var header2 = SparseReader.ReadStruct<SparseHeader>(reader);
                    lastChunkOffset = 0;
                    for (int i = 0; i < header2.TotalChunks; i++)
                    {
                        long chunkOffset = fs.Position;
                        var chunk = SparseReader.ReadStruct<ChunkHeader>(reader);
                        lastChunkOffset = chunkOffset;
                        // 跳过该 chunk 的数据区域
                        fs.Seek(chunk.TotalSize - header2.ChunkHeaderSize, SeekOrigin.Current);
                    }
                }

                // 复制 largePath 的 [0, lastChunkOffset) 到 output
                using (var src = new FileStream(largePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan))
                using (var dst = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.SequentialScan))
                {
                    long remaining = lastChunkOffset;
                    while (remaining > 0)
                    {
                        int toRead = (int)Math.Min(bufferSize, remaining);
                        int read = await src.ReadAsync(buffer, 0, toRead);
                        if (read == 0) break;
                        await dst.WriteAsync(buffer, 0, read);
                        remaining -= read;
                        bytesCopied += read;
                        var percent = (double)bytesCopied * 100.0 / totalSize;
                        if (percent > 100.0) percent = 100.0;
                        progress?.Report(percent);
                    }
                }

                // 2) 从 smallPath 跳过其第一个 chunk 的 header + data，然后将剩余全部追加到 output
                long newDataOffset;
                using (var fs = new FileStream(smallPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan))
                using (var reader = new BinaryReader(fs, Encoding.Default, leaveOpen: true))
                {
                    var header1 = SparseReader.ReadStruct<SparseHeader>(reader);
                    long firstChunkOffset = fs.Position;
                    var firstChunk = SparseReader.ReadStruct<ChunkHeader>(reader);
                    newDataOffset = fs.Position + (firstChunk.TotalSize - header1.ChunkHeaderSize);
                }

                using (var src = new FileStream(smallPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan))
                using (var dst = new FileStream(outputPath, FileMode.Append, FileAccess.Write, FileShare.None, bufferSize, FileOptions.SequentialScan))
                {
                    src.Seek(newDataOffset, SeekOrigin.Begin);
                    int read;
                    while ((read = await src.ReadAsync(buffer, 0, bufferSize)) > 0)
                    {
                        await dst.WriteAsync(buffer, 0, read);
                        bytesCopied += read;
                        var percent = (double)bytesCopied * 100.0 / totalSize;
                        if (percent > 100.0) percent = 100.0;
                        progress?.Report(percent);
                    }
                }

                progress?.Report(100.0);

                // 更新 header 中的 block size 和 total chunks（保持原逻辑）
                // 重新读取 header1/header2 计算 newTotalChunks
                SparseHeader h1, h2;
                using (var r1 = new FileStream(smallPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var br1 = new BinaryReader(r1, Encoding.Default, leaveOpen: true))
                {
                    h1 = SparseReader.ReadStruct<SparseHeader>(br1);
                }
                using (var r2 = new FileStream(largePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var br2 = new BinaryReader(r2, Encoding.Default, leaveOpen: true))
                {
                    h2 = SparseReader.ReadStruct<SparseHeader>(br2);
                }

                uint newTotalChunks = (h1.TotalChunks - 1) + (h2.TotalChunks - 1);
                using (var fs = new FileStream(outputPath, FileMode.Open, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(fs, Encoding.Default, leaveOpen: true))
                {
                    fs.Seek(0x0C, SeekOrigin.Begin);
                    writer.Write(4096);
                    fs.Seek(0x14, SeekOrigin.Begin);
                    writer.Write(newTotalChunks);
                }
            }
            finally
            {
                pool.Return(buffer);
            }
        }
    }
}
