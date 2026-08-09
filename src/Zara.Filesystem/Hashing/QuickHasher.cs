using System.IO.Hashing;
using Zara.Core.Files;

namespace Zara.Filesystem.Hashing;

/// <inheritdoc cref="IQuickHasher"/>
public sealed class QuickHasher : IQuickHasher
{
    private const int ChunkSize = 4096;

    public long Compute(CanonicalPath path)
    {
        using var stream = new FileStream(path.Value, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long size = stream.Length;

        Span<byte> head = stackalloc byte[ChunkSize];
        int headRead = ReadFully(stream, head);

        Span<byte> tail = stackalloc byte[ChunkSize];
        int tailRead = 0;

        // Only read a separate tail chunk if the file is bigger than one
        // chunk — otherwise the head read above already covered the whole
        // file and a "tail" would just be re-reading the same bytes.
        if (size > ChunkSize)
        {
            stream.Seek(Math.Max(size - ChunkSize, 0), SeekOrigin.Begin);
            tailRead = ReadFully(stream, tail);
        }

        Span<byte> combined = stackalloc byte[sizeof(long) + ChunkSize + ChunkSize];
        BitConverter.TryWriteBytes(combined, size);

        int offset = sizeof(long);
        head[..headRead].CopyTo(combined[offset..]);
        offset += headRead;

        if (tailRead > 0)
        {
            tail[..tailRead].CopyTo(combined[offset..]);
            offset += tailRead;
        }

        ulong hash = XxHash3.HashToUInt64(combined[..offset]);
        return unchecked((long)hash);
    }

    private static int ReadFully(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
