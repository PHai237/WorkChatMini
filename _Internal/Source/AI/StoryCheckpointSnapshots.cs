using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace WorkChatMini;

// Version 2 snapshots store compressed JSON, protected by hashes of the uncompressed
// UTF-8 bytes. V1 plaintext checkpoints stay readable indefinitely; they are never
// rewritten by migration. A manifest is published only after both payloads exist.
internal static class StoryCheckpointSnapshots
{
    private const int MaxSnapshotCharacters = 128 * 1024 * 1024;

    internal static async Task CaptureAsync(string directory, StoryCheckpoint checkpoint,
        string memoryJson, string conversationJson, CancellationToken token)
    {
        checkpoint.SnapshotFormat = 2;
        checkpoint.MemorySha256 = Hash(memoryJson);
        checkpoint.ConversationSha256 = Hash(conversationJson);
        await WriteGzipAtomicAsync(Path.Combine(directory, "memory.json.gz"), memoryJson, token);
        await WriteGzipAtomicAsync(Path.Combine(directory, "conversation.json.gz"), conversationJson, token);
    }

    internal static async Task<(string Memory, string Conversation)> ReadAsync(
        string directory, StoryCheckpoint checkpoint, CancellationToken token)
    {
        if (checkpoint.SnapshotFormat == 1)
        {
            return (await File.ReadAllTextAsync(Path.Combine(directory, "memory.json"), token),
                await File.ReadAllTextAsync(Path.Combine(directory, "conversation.json"), token));
        }
        if (checkpoint.SnapshotFormat != 2 || !IsSha256(checkpoint.MemorySha256) ||
            !IsSha256(checkpoint.ConversationSha256))
            throw new InvalidDataException("Unknown or incomplete checkpoint snapshot format.");
        string memory = await ReadGzipAsync(Path.Combine(directory, "memory.json.gz"), token);
        if (!string.Equals(Hash(memory), checkpoint.MemorySha256, StringComparison.Ordinal))
            throw new InvalidDataException("Checkpoint memory checksum mismatch.");
        string conversation = await ReadGzipAsync(Path.Combine(directory, "conversation.json.gz"), token);
        if (!string.Equals(Hash(conversation), checkpoint.ConversationSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Checkpoint conversation checksum mismatch.");
        return (memory, conversation);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static async Task WriteGzipAtomicAsync(string path, string value, CancellationToken token)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 65536, FileOptions.Asynchronous))
            await using (var gzip = new GZipStream(file, CompressionLevel.Fastest, leaveOpen: false))
            await using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
            {
                await writer.WriteAsync(value.AsMemory(), token);
            }
            // The manifest is written only after both compressed files are durable.
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static async Task<string> ReadGzipAsync(string path, CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: false);
        using var reader = new StreamReader(gzip, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        var builder = new StringBuilder();
        char[] buffer = new char[16384];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            if (builder.Length > MaxSnapshotCharacters - read)
                throw new InvalidDataException("Checkpoint decompressed JSON exceeds safety limit.");
            builder.Append(buffer, 0, read);
        }
        return builder.ToString();
    }
}
