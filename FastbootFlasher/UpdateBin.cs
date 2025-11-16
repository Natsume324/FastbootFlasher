using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FastbootFlasher
{
    internal class UpdateBin
    {
        private const int COMPINFO_LEN_OFFSET = 178;
        private const int COMPONENT_INFO_SIZE = 87;
        private const int NAME_MAX_LENGTH = 256;
        private const int HEADER_CHECK_LEN = 96;
        public class ComponentInfo
        {
            public string Name { get; set; }
            public long Offset { get; set; }
            public long Size { get; set; }
        }

        public static ObservableCollection<Partition> ParseUpdateBin(string filePath)
        {
            var components = new List<ComponentInfo>();
            var Partitions = new ObservableCollection<Partition>();

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return Partitions;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs, Encoding.UTF8, leaveOpen: false);

            long fileSize = fs.Length;
            if (fileSize < COMPINFO_LEN_OFFSET + 2)
                return Partitions;

            // 读取组件信息长度
            fs.Seek(COMPINFO_LEN_OFFSET, SeekOrigin.Begin);
            ushort compinfoAllSize = reader.ReadUInt16();

            // 边界检查
            if (compinfoAllSize == 0 || COMPINFO_LEN_OFFSET + 2 + compinfoAllSize > fileSize)
                return Partitions;

            // 计算数据起始偏移量
            long dataStartOffset = CalculateDataStartOffset(reader, compinfoAllSize, fileSize);
            if (dataStartOffset < 0 || dataStartOffset > fileSize)
                return Partitions;

            // 计算组件数量
            int componentCount = compinfoAllSize / COMPONENT_INFO_SIZE;
            if (componentCount <= 0)
                return Partitions;

            long currentOffset = COMPINFO_LEN_OFFSET + 2;

            for (int i = 0; i < componentCount; i++)
            {
                var component = ParseComponentInfo(reader, ref currentOffset, ref dataStartOffset, fileSize);
                if (component != null)
                    components.Add(component);
                else
                    break; // 出现解析错误时中止
            }

            for (int i = 0; i < components.Count; i++)
            {
                var component = components[i];
                Partitions.Add(new Partition
                {
                    Index = i + 1,
                    Name = component.Name,
                    Size = ImageFile.FormatImageSize(component.Size),
                    SourceFile = filePath
                });
            }

            return Partitions;
        }
        public static async Task<Stream?> ExtractPartitionStream(string partitionName, string filePath, IProgress<double> progress = null)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(partitionName) || !File.Exists(filePath))
                return null;

            return await Task.Run(() =>
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);

                long fileSize = fs.Length;

                if (fileSize < COMPINFO_LEN_OFFSET + 2)
                    return (Stream?)null;

                fs.Seek(COMPINFO_LEN_OFFSET, SeekOrigin.Begin);
                ushort compinfoAllSize = reader.ReadUInt16();

                if (compinfoAllSize == 0 || COMPINFO_LEN_OFFSET + 2 + compinfoAllSize > fileSize)
                    return (Stream?)null;

                long dataStartOffset = CalculateDataStartOffset(reader, compinfoAllSize, fileSize);
                if (dataStartOffset < 0 || dataStartOffset > fileSize)
                    return (Stream?)null;

                int componentCount = compinfoAllSize / COMPONENT_INFO_SIZE;
                if (componentCount <= 0)
                    return (Stream?)null;

                long currentOffset = COMPINFO_LEN_OFFSET + 2;

                for (int i = 0; i < componentCount; i++)
                {
                    var component = ParseComponentInfo(reader, ref currentOffset, ref dataStartOffset, fileSize);
                    if (component == null)
                        break;

                    if (string.Equals(component.Name?.Trim(), partitionName.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        if (component.Offset < 0 || component.Size <= 0 || component.Offset + component.Size > fileSize)
                            return (Stream?)null;

                        var partStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        partStream.Seek(component.Offset, SeekOrigin.Begin);
                        return (Stream)new SubStream(partStream, component.Size, progress);
                    }
                }

                return (Stream?)null;
            }).ConfigureAwait(false);
        }
        public static async Task<bool> ExtractPartitionImage(string partitionName, string filePath,IProgress<double> progress = null)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(partitionName)  || !File.Exists(filePath))
                return false;

            Stream src = null;
            try
            {
                src = await ExtractPartitionStream(partitionName, filePath, progress).ConfigureAwait(false);
                if (src == null)
                    return false;

                // Ensure output directory exists
                var dir = Path.GetDirectoryName(@$".\images\{partitionName}.img");
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using var dest = new FileStream(@$".\images\{partitionName}.img", FileMode.Create, FileAccess.Write, FileShare.None, 262144, FileOptions.Asynchronous | FileOptions.SequentialScan);
                byte[] buffer = new byte[1024*1024];
                int read;
                while ((read = await src.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    await dest.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                }
                // Ensure final 100% report
                try { progress?.Report(100.0); } catch { }
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                src?.Dispose();
            }
        }
        private static ComponentInfo ParseComponentInfo(BinaryReader reader, ref long offset, ref long dataStartOffset, long fileSize)
        {
            try
            {
                if (offset < 0 || offset + COMPONENT_INFO_SIZE > fileSize)
                    return null;

                reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                byte[] infoBytes = reader.ReadBytes(COMPONENT_INFO_SIZE);
                if (infoBytes.Length != COMPONENT_INFO_SIZE)
                    return null;

                // 读取组件名称 (在块内查找第一个0字节)
                int nameEnd = Array.IndexOf(infoBytes, (byte)0);
                if (nameEnd < 0)
                    nameEnd = Math.Min(NAME_MAX_LENGTH, COMPONENT_INFO_SIZE);

                string componentName = Encoding.UTF8.GetString(infoBytes, 0, nameEnd);
                componentName = componentName.TrimStart('/');

                // 读取组件大小 (位于块内偏移47处，8字节，little-endian)
                if (47 + 8 > infoBytes.Length)
                    return null;

                ulong componentSize = BinaryPrimitives.ReadUInt64LittleEndian(new ReadOnlySpan<byte>(infoBytes, 47, 8));

                long currentDataOffset = dataStartOffset;
                dataStartOffset += (long)componentSize;

                // 移动到下一个组件信息的位置
                offset += COMPONENT_INFO_SIZE;

                return new ComponentInfo
                {
                    Name = componentName,
                    Offset = currentDataOffset,
                    Size = (long)componentSize
                };
            }
            catch (Exception)
            {
                // 保持函数稳定，不抛出异常
                return null;
            }
        }

        private static long CalculateDataStartOffset(BinaryReader reader, ushort compinfoAllSize, long fileSize)
        {
            long baseOffset = COMPINFO_LEN_OFFSET + 2 + compinfoAllSize + 16;
            if (baseOffset + 2 > fileSize)
                return -1;

            reader.BaseStream.Seek(baseOffset, SeekOrigin.Begin);

            ushort type2 = reader.ReadUInt16();

            if (type2 == 0x08)
            {
                if (baseOffset + 2 + 4 > fileSize)
                    return -1;
                uint hashdataSize = reader.ReadUInt32();
                return baseOffset + 2 + 4 + hashdataSize;
            }
            else if (type2 == 0x06)
            {
                if (baseOffset + 2 + 16 + 4 > fileSize)
                    return -1;
                reader.ReadBytes(16); // 跳过16字节
                uint hashdataSize = reader.ReadUInt32();
                return baseOffset + 2 + 16 + 4 + hashdataSize;
            }
            else
            {
                return -1;
            }
        }

        public static bool IsUpdateBin(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: false);

                int len = (int)Math.Min(HEADER_CHECK_LEN, fs.Length);
                if (len <= 0)
                    return false;

                var bytes = br.ReadBytes(len);
                string header = Encoding.ASCII.GetString(bytes);
                return header.Contains("OpenHarmony", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// A wrapper stream that exposes only a sub-range of an underlying stream. Disposes the underlying stream when disposed.
        /// This avoids loading the entire partition into memory.
        /// </summary>
        private class SubStream : Stream
        {
            private readonly Stream _baseStream;
            private readonly long _start;
            private readonly long _length;
            private long _position;
            private readonly IProgress<double> _progress;
            private long _totalRead;
            // If true, base stream position must be reset to _start + _position before next read
            private bool _needsBaseSeek;

            public SubStream(Stream baseStream, long length, IProgress<double> progress = null)
            {
                _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
                _start = baseStream.Position;
                _length = length;
                _position = 0;
                _progress = progress;
                _totalRead = 0;
                _needsBaseSeek = false; // base stream already at correct position when created
            }

            public override bool CanRead => _baseStream.CanRead;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _length;

            public override long Position
            {
                get => _position;
                set => Seek(value, SeekOrigin.Begin);
            }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_position >= _length) return 0;
                long remaining = _length - _position;
                if (count > remaining) count = (int)remaining;

                // Only seek base stream when needed (after a Seek on the SubStream)
                if (_needsBaseSeek)
                {
                    _baseStream.Seek(_start + _position, SeekOrigin.Begin);
                    _needsBaseSeek = false;
                }

                int read = _baseStream.Read(buffer, offset, count);
                _position += read;
                if (read > 0)
                {
                    _totalRead += read;
                    try { _progress?.Report((_totalRead * 100.0) / _length); } catch { }
                }
                return read;
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (_position >= _length) return 0;
                long remaining = _length - _position;
                if (count > remaining) count = (int)remaining;

                if (_needsBaseSeek)
                {
                    // Seek synchronously to avoid issues with streams that don't support async seek
                    _baseStream.Seek(_start + _position, SeekOrigin.Begin);
                    _needsBaseSeek = false;
                }

                int read = await _baseStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                _position += read;
                if (read > 0)
                {
                    _totalRead += read;
                    try { _progress?.Report((_totalRead * 100.0) / _length); } catch { }
                }
                return read;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                long newPos;
                switch (origin)
                {
                    case SeekOrigin.Begin: newPos = offset; break;
                    case SeekOrigin.Current: newPos = _position + offset; break;
                    case SeekOrigin.End: newPos = _length + offset; break;
                    default: throw new ArgumentOutOfRangeException(nameof(origin));
                }

                if (newPos < 0 || newPos > _length) throw new ArgumentOutOfRangeException(nameof(offset));
                _position = newPos;
                // Mark that base stream must be repositioned before next read
                _needsBaseSeek = true;
                return _position;
            }

            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _baseStream?.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
