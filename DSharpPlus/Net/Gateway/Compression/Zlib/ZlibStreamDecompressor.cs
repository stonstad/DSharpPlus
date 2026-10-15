#pragma warning disable IDE0046

using System;

using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;

namespace DSharpPlus.Net.Gateway.Compression.Zlib;

/// <summary>
/// A payload decompressor using zlib on the transport level.
/// </summary>
public sealed class ZlibStreamDecompressor : IPayloadDecompressor
{
    // the gateway read loop decompresses while a reconnect on another task resets the stream; disposing
    // the stream mid-inflate frees zlib's native state under it and crashes the process (0xC0000005)
    private readonly object syncRoot = new();
    private ZlibWrapper wrapper = new();

    /// <inheritdoc/>
    public string Name => "zlib-stream";

    /// <inheritdoc/>
    public bool IsTransportCompression => true;

    /// <inheritdoc/>
    public bool TryDecompress(ReadOnlySpan<byte> compressed, ArrayPoolBufferWriter<byte> decompressed)
    {
        lock (this.syncRoot)
        {
            // reset by a reconnect, so this frame belongs to the closed connection
            if (this.wrapper == default)
                return false;

            if (!this.wrapper.TryInflate(compressed, decompressed))
            {
                decompressed.Clear();
                decompressed.Write(compressed);
            }

            return true;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (this.syncRoot)
        {
            if (this.wrapper != default)
            {
                this.wrapper.Dispose();
            }
        }
    }

    /// <inheritdoc/>
    public void Reset()
    {
        lock (this.syncRoot)
        {
            this.wrapper.Dispose();
            this.wrapper = default;
        }
    }

    /// <inheritdoc/>
    public void Initialize()
    {
        lock (this.syncRoot)
        {
            if (this.wrapper != default)
            {
                Reset();
            }

            this.wrapper = new();
        }
    }
}
