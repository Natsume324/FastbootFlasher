using ChromeosUpdateEngine;
using SharpCompress;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.Xz;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp;

namespace FastbootFlasher
{
    internal class PayloadBin
    {
        private const string Magic = "CrAU";

        public static ObservableCollection<Partition> ParsePayloadBin(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);
            string magic = Encoding.ASCII.GetString(br.ReadBytes(4));
            ulong version = BitConverter.ToUInt64(br.ReadBytes(8).Reverse().ToArray(), 0);
            ulong manifestLen = BitConverter.ToUInt64(br.ReadBytes(8).Reverse().ToArray(), 0);
            uint metadataSigLen = BitConverter.ToUInt32(br.ReadBytes(4).Reverse().ToArray(), 0);
            var manifest = DeltaArchiveManifest.Parser.ParseFrom(br.ReadBytes((int)manifestLen));
            var Partitions = new ObservableCollection<Partition>();
            foreach (var partition in manifest.Partitions)
            {
                string partName = partition.PartitionName;
                long blockSize = (long)manifest.BlockSize;
                long partSize = partition.Operations.Sum(op =>
                            (op.DstExtents?.Sum(e => (long)e.NumBlocks) ?? 0L) * blockSize);
                Partitions.Add(new Partition
                {
                    Index = Partitions.Count + 1,
                    Name = partName,
                    Size = ImageFile.FormatImageSize(partSize),
                    SourceFile = filePath
                });
            }
            return Partitions;
        }

        // 新增：并行提取分区的方法
        public static async Task<bool> ExtractPartitionImage(string partitionName, string filePath, IProgress<double> progress = null)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(partitionName) || !File.Exists(filePath))
                return false;

            try
            {
                // 读取 manifest
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var br = new BinaryReader(fs);

                string magic = Encoding.ASCII.GetString(br.ReadBytes(4));
                if (magic != Magic)
                    throw new Exception("Invalid payload magic (expected CrAU)");

                ulong version = BitConverter.ToUInt64(br.ReadBytes(8).Reverse().ToArray(), 0);
                ulong manifestLen = BitConverter.ToUInt64(br.ReadBytes(8).Reverse().ToArray(), 0);
                uint metadataSigLen = BitConverter.ToUInt32(br.ReadBytes(4).Reverse().ToArray(), 0);

                var manifest = DeltaArchiveManifest.Parser.ParseFrom(br.ReadBytes((int)manifestLen));
                long dataOffsetBase = 24 + (long)manifestLen + (long)metadataSigLen;

                var targetPartition = manifest.Partitions.FirstOrDefault(p => p.PartitionName == partitionName);
                if (targetPartition == null)
                    throw new ArgumentException($"Partition '{partitionName}' not found in payload.");

                // 确保输出目录存在
                var dir = Path.GetDirectoryName(@$".\images\{partitionName}.img");
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // 创建输出文件
                using var outStream = new FileStream(@$".\images\{partitionName}.img", FileMode.Create, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.RandomAccess);

                var ops = targetPartition.Operations.ToList();
                long blockSize = (long)manifest.BlockSize;
                long totalExpectedOutputBytes = ops.Sum(op =>
                    (op.DstExtents?.Sum(e => (long)e.NumBlocks) ?? 0L) * blockSize
                );

                long bytesWritten = 0;
                object progressLock = new object();
                object consoleLock = new object();

                void UpdateProgress(long addedBytes)
                {
                    if (progress == null) return;

                    lock (progressLock)
                    {
                        bytesWritten += addedBytes;
                        double percent = totalExpectedOutputBytes > 0 ? Math.Min(100.0, (double)bytesWritten / totalExpectedOutputBytes * 100.0) : 100.0;
                        try { progress.Report(percent); } catch { }
                    }
                }

                // 文件读取锁，确保对源文件的 Seek/Read 线程安全
                object fsReadLock = new object();

                // 并行处理操作
                int maxParallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
                using var sem = new SemaphoreSlim(maxParallelism);
                var tasks = new List<Task>();

                foreach (var op in ops)
                {
                    await sem.WaitAsync();

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            // 计算该操作应写入的解压后大小
                            long opExpectedBytes = (op.DstExtents?.Sum(e => (long)e.NumBlocks) ?? 0L) * blockSize;

                            byte[] compressed = Array.Empty<byte>();
                            if (op.DataLength > 0)
                            {
                                // 读取压缩数据（对源文件的 Seek+Read 需要锁）
                                long absOffset = dataOffsetBase + (long)op.DataOffset;
                                lock (fsReadLock)
                                {
                                    fs.Seek(absOffset, SeekOrigin.Begin);
                                    compressed = br.ReadBytes((int)op.DataLength);
                                }
                            }

                            switch (op.Type)
                            {
                                case InstallOperation.Types.Type.Zero:
                                    // 按 extents 写零
                                    if (op.DstExtents != null)
                                    {
                                        foreach (var e in op.DstExtents)
                                        {
                                            long destOffset = (long)e.StartBlock * blockSize;
                                            long len = (long)e.NumBlocks * blockSize;
                                            const int bufSize = 8192;
                                            var zeroBuf = new byte[bufSize];
                                            long rem = len;
                                            while (rem > 0)
                                            {
                                                int chunk = (int)Math.Min(bufSize, rem);
                                                lock (outStream)
                                                {
                                                    outStream.Seek(destOffset + (len - rem), SeekOrigin.Begin);
                                                    outStream.Write(zeroBuf, 0, chunk);
                                                }
                                                UpdateProgress(chunk);
                                                rem -= chunk;
                                            }
                                        }
                                    }
                                    break;

                                case InstallOperation.Types.Type.Replace:
                                    // 未压缩：直接写入到 dst extents
                                    if (compressed.Length == 0) break;
                                    {
                                        int srcPos = 0;
                                        if (op.DstExtents != null)
                                        {
                                            foreach (var e in op.DstExtents)
                                            {
                                                long destOffset = (long)e.StartBlock * blockSize;
                                                int len = (int)((long)e.NumBlocks * blockSize);
                                                if (srcPos + len > compressed.Length)
                                                    len = compressed.Length - srcPos;
                                                if (len <= 0) break;

                                                lock (outStream)
                                                {
                                                    outStream.Seek(destOffset, SeekOrigin.Begin);
                                                    outStream.Write(compressed, srcPos, len);
                                                }
                                                UpdateProgress(len);
                                                srcPos += len;
                                            }
                                        }
                                    }
                                    break;

                                case InstallOperation.Types.Type.ReplaceBz:
                                case InstallOperation.Types.Type.ReplaceXz:
                                case InstallOperation.Types.Type.Zstd:
                                    {
                                        // 流式解压并分片写入
                                        if (compressed.Length == 0) break;

                                        using var decompressedStream = new MemoryStream();
                                        switch (op.Type)
                                        {
                                            case InstallOperation.Types.Type.ReplaceBz:
                                                using (var bz = new BZip2Stream(new MemoryStream(compressed), SharpCompress.Compressors.CompressionMode.Decompress, false))
                                                    await bz.CopyToAsync(decompressedStream);
                                                break;
                                            case InstallOperation.Types.Type.ReplaceXz:
                                                using (var xz = new XZStream(new MemoryStream(compressed)))
                                                    await xz.CopyToAsync(decompressedStream);
                                                break;
                                            case InstallOperation.Types.Type.Zstd:
                                                using (var zstd = new DecompressionStream(new MemoryStream(compressed)))
                                                    await zstd.CopyToAsync(decompressedStream);
                                                break;
                                        }

                                        decompressedStream.Seek(0, SeekOrigin.Begin);
                                        if (op.DstExtents != null)
                                        {
                                            foreach (var e in op.DstExtents)
                                            {
                                                long destOffset = (long)e.StartBlock * blockSize;
                                                long len = (long)e.NumBlocks * blockSize;
                                                long rem = len;
                                                const int bufSize = 65536;
                                                var buf = new byte[bufSize];
                                                long writtenForExtent = 0;
                                                while (rem > 0)
                                                {
                                                    int toRead = (int)Math.Min(bufSize, rem);
                                                    int actuallyRead = await decompressedStream.ReadAsync(buf, 0, toRead);
                                                    if (actuallyRead <= 0) break;

                                                    lock (outStream)
                                                    {
                                                        outStream.Seek(destOffset + writtenForExtent, SeekOrigin.Begin);
                                                        outStream.Write(buf, 0, actuallyRead);
                                                    }
                                                    UpdateProgress(actuallyRead);
                                                    writtenForExtent += actuallyRead;
                                                    rem -= actuallyRead;
                                                }

                                                // 如果该 extent 未被填满（decompressed 数据耗尽），跳出
                                                if (decompressedStream.Position >= decompressedStream.Length) break;
                                            }
                                        }
                                    }
                                    break;

                                default:
                                    Debug.WriteLine($"[{partitionName}] Unsupported operation type: {op.Type}");
                                    break;
                            }
                        }
                        finally
                        {
                            sem.Release();
                        }
                    }));
                }

                // 等待所有操作完成
                await Task.WhenAll(tasks);

                // 确保最终进度为 100%
                try { progress?.Report(100.0); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ExtractPartitionImage failed: {ex.Message}");
                return false;
            }
        }
        

        public static async Task<Stream> DecompressAsync(InstallOperation.Types.Type type, byte[] data)
        {
            var ms = new MemoryStream();
            switch (type)
            {
                case InstallOperation.Types.Type.ReplaceBz:
                    using (var bz = new BZip2Stream(new MemoryStream(data), SharpCompress.Compressors.CompressionMode.Decompress, false))
                        await bz.CopyToAsync(ms);
                    break;

                case InstallOperation.Types.Type.ReplaceXz:
                    using (var xz = new XZStream(new MemoryStream(data)))
                        await xz.CopyToAsync(ms);
                    break;

                case InstallOperation.Types.Type.Replace:
                    await ms.WriteAsync(data, 0, data.Length);
                    break;

                case InstallOperation.Types.Type.Zstd:
                    using (var zstd = new DecompressionStream(new MemoryStream(data)))
                        await zstd.CopyToAsync(ms);
                    break;

                case InstallOperation.Types.Type.Zero:
                    await ms.WriteAsync(new byte[data.Length], 0, data.Length);
                    break;

                default:
                    Debug.WriteLine($"[!] Unsupported compression type: {type}");
                    break;
            }

            ms.Position = 0;
            return ms;
        }

        public static bool IsPayloadBin(string filePath)
        {
            using BinaryReader br = new BinaryReader(new FileStream(filePath, FileMode.Open, FileAccess.Read));
            string header = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (header == Magic)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        
        private class OpInfo
        {
            public InstallOperation.Types.Type Type { get; set; }
            public long DataOffset { get; set; }
            public long DataLength { get; set; }
            public long DecompressedLength { get; set; }
            public Stream? PredecompressedStream { get; set; } // 新增字段：预解压缩数据流
        }

        private class PartitionStream : Stream
        {
            private readonly string _payloadPath;
            private readonly FileStream _payloadFs;
            private readonly List<OpInfo> _ops;
            private readonly long _length;
            private int _currentOpIndex;
            private Stream? _currentDecompressedStream;
            private long _position;
            private readonly IProgress<double>? _progress;
            private int _completedOps;
            private double _lastReportedPercent;

            public PartitionStream(string payloadPath, List<OpInfo> ops, long length, IProgress<double>? progress)
            {
                _payloadPath = payloadPath;
                _payloadFs = new FileStream(payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                _ops = ops;
                _length = length;
                _progress = progress;
                _currentOpIndex = 0;
                _position = 0;
                _completedOps = 0;
                _lastReportedPercent = -1.0;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _length;
            public override long Position { get => _position; set => throw new NotSupportedException(); }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
            {
                if (_currentOpIndex >= _ops.Count)
                    return 0;

                int totalRead = 0;
                while (count > 0)
                {
                    if (_currentDecompressedStream == null)
                    {
                        if (_currentOpIndex >= _ops.Count)
                            break;

                        _currentDecompressedStream = await CreateDecompressedStreamForOpAsync(_ops[_currentOpIndex]);
                    }

                    int read = await _currentDecompressedStream.ReadAsync(buffer, offset, count, cancellationToken);
                    if (read == 0)
                    {
                        _currentDecompressedStream.Dispose();
                        _currentDecompressedStream = null;
                        _currentOpIndex++;
                        _completedOps++;
                        ReportProgressIfNeeded();
                        continue;
                    }

                    totalRead += read;
                    offset += read;
                    count -= read;
                    _position += read;
                    ReportProgressIfNeeded();

                    if (count == 0)
                        break;
                }

                return totalRead;
            }

            private void ReportProgressIfNeeded()
            {
                if (_length <= 0 || _progress == null) return;
                double percent = Math.Min(100.0, (_position / (double)_length) * 100.0);
                if (percent >= 100.0) percent = 100.0;
                if (Math.Abs(percent - _lastReportedPercent) >= 0.5 || percent == 100.0)
                {
                    _lastReportedPercent = percent;
                    try { _progress.Report(percent); } catch { }
                }
            }

            private Task<Stream> CreateDecompressedStreamForOpAsync(OpInfo op)
            {
                var sub = new SubStream(_payloadFs, op.DataOffset, op.DataLength, leaveOpen: true);

                switch (op.Type)
                {
                    case InstallOperation.Types.Type.Replace:
                        return Task.FromResult<Stream>(sub);

                    case InstallOperation.Types.Type.ReplaceBz:
                        return Task.FromResult<Stream>(new BZip2Stream(sub, SharpCompress.Compressors.CompressionMode.Decompress, false) as Stream);

                    case InstallOperation.Types.Type.ReplaceXz:
                        return Task.FromResult<Stream>(new XZStream(sub) as Stream);

                    case InstallOperation.Types.Type.Zstd:
                        return Task.FromResult<Stream>(new DecompressionStream(sub) as Stream);

                    case InstallOperation.Types.Type.Zero:
                        sub.Dispose();
                        return Task.FromResult<Stream>(new ZeroStream(op.DecompressedLength) as Stream);

                    default:
                        sub.Dispose();
                        return Task.FromResult<Stream>(new MemoryStream() as Stream);
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _currentDecompressedStream?.Dispose();
                    try { _payloadFs.Dispose(); } catch { }
                }
                base.Dispose(disposing);
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private class SubStream : Stream
        {
            private readonly FileStream _fs;
            private readonly long _start;
            private readonly long _length;
            private long _pos;
            private readonly bool _leaveOpen;

            public SubStream(FileStream fs, long start, long length, bool leaveOpen = false)
            {
                _fs = fs ?? throw new ArgumentNullException(nameof(fs));
                _start = start;
                _length = length;
                _pos = 0;
                _leaveOpen = leaveOpen;
                _fs.Position = _start;
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _length;
            public override long Position { get => _pos; set => _pos = value; }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
            {
                if (_pos >= _length) return 0;
                int toRead = (int)Math.Min(count, Math.Min(int.MaxValue, _length - _pos));
                int read = await _fs.ReadAsync(buffer, offset, toRead, cancellationToken);
                _pos += read;
                return read;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                long newPos = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => _pos + offset,
                    SeekOrigin.End => _length + offset,
                    _ => throw new ArgumentOutOfRangeException(nameof(origin))
                };
                if (newPos < 0 || newPos > _length) throw new IOException("Attempted to seek outside the substream bounds.");
                _pos = newPos;
                _fs.Position = _start + _pos;
                return _pos;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && !_leaveOpen)
                {
                    try { _fs.Dispose(); } catch { }
                }
                base.Dispose(disposing);
            }

            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private class ZeroStream : Stream
        {
            private readonly long _length;
            private long _pos;

            public ZeroStream(long length)
            {
                _length = length;
                _pos = 0;
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _length;
            public override long Position { get => _pos; set => _pos = value; }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int toRead = (int)Math.Min(count, _length - _pos);
                if (toRead <= 0) return 0;
                Array.Clear(buffer, offset, toRead);
                _pos += toRead;
                return toRead;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                long newPos = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => _pos + offset,
                    SeekOrigin.End => _length + offset,
                    _ => throw new ArgumentOutOfRangeException(nameof(origin))
                };
                if (newPos < 0 || newPos > _length) throw new IOException("Attempted to seek outside the zerostream bounds.");
                _pos = newPos;
                return _pos;
            }

            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
