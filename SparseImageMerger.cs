using System.Buffers;
using System.IO;
using System.Text;

namespace FastbootFlasher;

internal static class SparseImageMerger
{
    private const uint SparseMagic = 0xED26FF3A;
    private const ushort RawChunk = 0xCAC1;
    private const ushort FillChunk = 0xCAC2;
    private const ushort DontCareChunk = 0xCAC3;
    private const ushort Crc32Chunk = 0xCAC4;
    private const int SparseHeaderSize = 28;
    private const int ChunkHeaderSize = 12;

    private sealed record SparseHeader(
        ushort MajorVersion, ushort MinorVersion, ushort FileHeaderSize,
        ushort ChunkHeaderSize, uint BlockSize, uint TotalBlocks);

    private sealed record DataChunk(
        ushort Type, long StartBlock, long EndBlock, string SourcePath, long DataOffset);

    private sealed record Segment(long StartBlock, long EndBlock, DataChunk Chunk);

    public static async Task MergeAsync(
        IReadOnlyList<string> inputPaths,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (inputPaths.Count < 2)
            throw new InvalidDataException("至少需要两个 sparse super 分片才能合并。");

        var parsed = inputPaths.Select(Parse).ToList();
        SparseHeader reference = parsed[0].Header;
        for (int i = 1; i < parsed.Count; i++)
        {
            if (parsed[i].Header.BlockSize != reference.BlockSize ||
                parsed[i].Header.TotalBlocks != reference.TotalBlocks)
            {
                throw new InvalidDataException(
                    $"{Path.GetFileName(inputPaths[i])} 与第一个 super 分片的块大小或总块数不一致。");
            }
        }

        List<Segment> segments = BuildSegments(parsed.Select(x => x.Chunks).ToList(), reference.TotalBlocks);
        uint outputChunkCount = checked((uint)segments.Count);
        long previousBlock = 0;
        foreach (Segment segment in segments)
        {
            if (segment.StartBlock > previousBlock) outputChunkCount++;
            previousBlock = segment.EndBlock;
        }
        if (previousBlock < reference.TotalBlocks) outputChunkCount++;

        long rawBytes = segments
            .Where(x => x.Chunk.Type == RawChunk)
            .Sum(x => checked((x.EndBlock - x.StartBlock) * reference.BlockSize));
        long copiedBytes = 0;
        string temporaryPath = outputPath + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        try
        {
            await using var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
            using (var writer = new BinaryWriter(output, Encoding.UTF8, true))
            {
                writer.Write(SparseMagic);
                writer.Write(reference.MajorVersion);
                writer.Write(reference.MinorVersion);
                writer.Write((ushort)SparseHeaderSize);
                writer.Write((ushort)ChunkHeaderSize);
                writer.Write(reference.BlockSize);
                writer.Write(reference.TotalBlocks);
                writer.Write(outputChunkCount);
                writer.Write(0u);
            }

            var sources = inputPaths.ToDictionary(
                x => x,
                x => new FileStream(x, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true),
                StringComparer.OrdinalIgnoreCase);
            try
            {
                previousBlock = 0;
                foreach (Segment segment in segments)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (segment.StartBlock > previousBlock)
                        WriteChunkHeader(output, DontCareChunk, segment.StartBlock - previousBlock, ChunkHeaderSize);

                    long blockCount = segment.EndBlock - segment.StartBlock;
                    FileStream source = sources[segment.Chunk.SourcePath];
                    if (segment.Chunk.Type == FillChunk)
                    {
                        WriteChunkHeader(output, FillChunk, blockCount, ChunkHeaderSize + 4);
                        source.Position = segment.Chunk.DataOffset;
                        byte[] fill = new byte[4];
                        await ReadExactlyAsync(source, fill, cancellationToken);
                        await output.WriteAsync(fill, cancellationToken);
                    }
                    else
                    {
                        long byteCount = checked(blockCount * reference.BlockSize);
                        WriteChunkHeader(output, RawChunk, blockCount, checked(ChunkHeaderSize + byteCount));
                        source.Position = checked(segment.Chunk.DataOffset +
                            (segment.StartBlock - segment.Chunk.StartBlock) * reference.BlockSize);
                        copiedBytes += await CopyExactlyAsync(source, output, byteCount, cancellationToken,
                            copied => progress?.Report(rawBytes == 0 ? 100 : (copiedBytes + copied) * 100.0 / rawBytes));
                    }
                    previousBlock = segment.EndBlock;
                }
                if (previousBlock < reference.TotalBlocks)
                    WriteChunkHeader(output, DontCareChunk, reference.TotalBlocks - previousBlock, ChunkHeaderSize);
            }
            finally
            {
                foreach (FileStream source in sources.Values) await source.DisposeAsync();
            }

            await output.FlushAsync(cancellationToken);
            await output.DisposeAsync();
            File.Move(temporaryPath, outputPath, true);
            progress?.Report(100);
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    private static (SparseHeader Header, List<DataChunk> Chunks) Parse(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        if (reader.ReadUInt32() != SparseMagic)
            throw new InvalidDataException($"{Path.GetFileName(path)} 不是 Android sparse 镜像。");
        var header = new SparseHeader(reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16(),
            reader.ReadUInt16(), reader.ReadUInt32(), reader.ReadUInt32());
        uint totalChunks = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        if (header.MajorVersion != 1 || header.FileHeaderSize < SparseHeaderSize ||
            header.ChunkHeaderSize < ChunkHeaderSize || header.BlockSize == 0)
            throw new InvalidDataException($"{Path.GetFileName(path)} 的 sparse 文件头无效或不受支持。");

        stream.Position = header.FileHeaderSize;
        long currentBlock = 0;
        var chunks = new List<DataChunk>();
        for (uint i = 0; i < totalChunks; i++)
        {
            long chunkHeaderOffset = stream.Position;
            ushort type = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            uint chunkBlocks = reader.ReadUInt32();
            uint totalSize = reader.ReadUInt32();
            if (totalSize < header.ChunkHeaderSize)
                throw new InvalidDataException($"{Path.GetFileName(path)} 的 sparse chunk {i + 1} 大小无效。");
            long dataOffset = checked(chunkHeaderOffset + header.ChunkHeaderSize);
            long dataSize = totalSize - header.ChunkHeaderSize;
            long endBlock = checked(currentBlock + chunkBlocks);
            if (endBlock > header.TotalBlocks || dataOffset + dataSize > stream.Length)
                throw new InvalidDataException($"{Path.GetFileName(path)} 的 sparse chunk {i + 1} 越界或已截断。");

            switch (type)
            {
                case RawChunk when dataSize == checked((long)chunkBlocks * header.BlockSize):
                case FillChunk when dataSize == 4:
                    chunks.Add(new DataChunk(type, currentBlock, endBlock, path, dataOffset));
                    currentBlock = endBlock;
                    break;
                case DontCareChunk when dataSize == 0:
                    currentBlock = endBlock;
                    break;
                case Crc32Chunk when chunkBlocks == 0 && dataSize == 4:
                    break;
                default:
                    throw new InvalidDataException($"{Path.GetFileName(path)} 的 sparse chunk {i + 1} 类型或大小无效。");
            }
            stream.Position = dataOffset + dataSize;
        }
        if (currentBlock != header.TotalBlocks)
            throw new InvalidDataException($"{Path.GetFileName(path)} 的 sparse 块布局不完整。");
        return (header, chunks);
    }

    private static List<Segment> BuildSegments(IReadOnlyList<List<DataChunk>> images, uint totalBlocks)
    {
        var boundaries = new SortedSet<long> { 0, totalBlocks };
        foreach (DataChunk chunk in images.SelectMany(x => x))
        {
            boundaries.Add(chunk.StartBlock);
            boundaries.Add(chunk.EndBlock);
        }
        long[] points = boundaries.ToArray();
        var positions = new int[images.Count];
        var result = new List<Segment>();
        for (int p = 0; p < points.Length - 1; p++)
        {
            long start = points[p], end = points[p + 1];
            DataChunk? selected = null;
            for (int image = 0; image < images.Count; image++)
            {
                while (positions[image] < images[image].Count && images[image][positions[image]].EndBlock <= start)
                    positions[image]++;
                if (positions[image] < images[image].Count)
                {
                    DataChunk candidate = images[image][positions[image]];
                    if (candidate.StartBlock <= start && candidate.EndBlock >= end) selected = candidate;
                }
            }
            if (selected is null) continue;
            if (result.Count > 0 && result[^1].EndBlock == start && result[^1].Chunk == selected)
                result[^1] = result[^1] with { EndBlock = end };
            else
                result.Add(new Segment(start, end, selected));
        }
        return result;
    }

    private static void WriteChunkHeader(Stream output, ushort type, long blocks, long totalSize)
    {
        if (blocks > uint.MaxValue || totalSize > uint.MaxValue)
            throw new InvalidDataException("合并后的 sparse chunk 超出格式允许的大小。");
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write(type);
        writer.Write((ushort)0);
        writer.Write((uint)blocks);
        writer.Write((uint)totalSize);
    }

    private static async Task ReadExactlyAsync(Stream source, Memory<byte> buffer, CancellationToken token)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await source.ReadAsync(buffer[offset..], token);
            if (read == 0) throw new EndOfStreamException("读取 sparse 数据时文件意外结束。");
            offset += read;
        }
    }

    private static async Task<long> CopyExactlyAsync(Stream source, Stream output, long count,
        CancellationToken token, Action<long> report)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(4 * 1024 * 1024);
        long copied = 0;
        try
        {
            while (copied < count)
            {
                int wanted = (int)Math.Min(buffer.Length, count - copied);
                int read = await source.ReadAsync(buffer.AsMemory(0, wanted), token);
                if (read == 0) throw new EndOfStreamException("读取 sparse RAW 数据时文件意外结束。");
                await output.WriteAsync(buffer.AsMemory(0, read), token);
                copied += read;
                report(copied);
            }
            return copied;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
