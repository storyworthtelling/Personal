using Android.Media;
using Java.Nio;

namespace Score;

/// <summary>
/// Wraps a seekable .NET <see cref="Stream"/> as the Java-side data source
/// <see cref="MediaExtractor"/> reads from, so decoding can run directly over the same
/// Drive-backed stream <see cref="MusicTagReader"/> already opens — no local download
/// step just to look for silence.
/// </summary>
internal sealed class StreamMediaDataSource(System.IO.Stream stream) : MediaDataSource
{
    public override long Size => stream.CanSeek ? stream.Length : -1;

    public override int ReadAt(long position, byte[]? buffer, int offset, int size)
    {
        if (buffer is null)
        {
            return -1;
        }

        stream.Seek(position, SeekOrigin.Begin);
        var read = stream.Read(buffer, offset, size);
        return read == 0 ? -1 : read;
    }

    public override void Close()
    {
    }
}

/// <summary>
/// Decodes a track's audio to a per-frame loudness series for
/// <see cref="Codex.Domain.Music.HiddenTrackDetector"/> — the platform half of hidden-
/// track detection, kept as thin as the classic synchronous <see cref="MediaCodec"/>
/// buffer loop allows. Only loudness survives the decode; the PCM itself is discarded
/// frame by frame; nothing is written to disk.
/// </summary>
public sealed class AudioLevelReader
{
    // Fine enough to place a boundary within a fraction of a second; coarse enough
    // that a whole album's worth of scanning stays cheap.
    public static readonly TimeSpan FrameDuration = TimeSpan.FromMilliseconds(100);

    private const int TimeoutMicroseconds = 10_000;

    public (IReadOnlyList<float> Levels, TimeSpan FrameDuration) ReadLevels(
        System.IO.Stream stream, CancellationToken cancellationToken)
    {
        using var dataSource = new StreamMediaDataSource(stream);
        using var extractor = new MediaExtractor();
        extractor.SetDataSource(dataSource);

        var (trackIndex, format) = FindAudioTrack(extractor);
        if (trackIndex < 0 || format is null)
        {
            return ([], FrameDuration);
        }

        extractor.SelectTrack(trackIndex);

        var sampleRate = format.GetInteger(MediaFormat.KeySampleRate);
        var channelCount = format.GetInteger(MediaFormat.KeyChannelCount);
        var samplesPerFrame = Math.Max(1, (int)(sampleRate * channelCount * FrameDuration.TotalSeconds));

        using var codec = MediaCodec.CreateDecoderByType(format.GetString(MediaFormat.KeyMime)!)!;
        codec.Configure(format, null, null, MediaCodecConfigFlags.None);
        codec.Start();

        try
        {
            return (Decode(extractor, codec, samplesPerFrame, cancellationToken), FrameDuration);
        }
        finally
        {
            codec.Stop();
        }
    }

    private static (int Index, MediaFormat? Format) FindAudioTrack(MediaExtractor extractor)
    {
        for (var i = 0; i < extractor.TrackCount; i++)
        {
            var candidate = extractor.GetTrackFormat(i)!;
            if (candidate.GetString(MediaFormat.KeyMime)?.StartsWith("audio/") == true)
            {
                return (i, candidate);
            }
        }

        return (-1, null);
    }

    private static List<float> Decode(
        MediaExtractor extractor, MediaCodec codec, int samplesPerFrame, CancellationToken cancellationToken)
    {
        var levels = new List<float>();
        var accumulator = new List<short>(samplesPerFrame);
        var bufferInfo = new MediaCodec.BufferInfo();
        var inputDone = false;
        var outputDone = false;

        while (!outputDone && !cancellationToken.IsCancellationRequested)
        {
            if (!inputDone)
            {
                inputDone = FeedInput(extractor, codec);
            }

            outputDone = DrainOutput(codec, bufferInfo, samplesPerFrame, accumulator, levels);
        }

        // Whatever did not fill a whole frame at the very end is still real audio, not
        // silence — folding it into one short final frame beats losing it, and losing
        // it is how a trailing hidden track's last instant would go unreported.
        if (accumulator.Count > 0)
        {
            levels.Add(Rms(accumulator));
        }

        return levels;
    }

    // Returns true once the extractor has handed the decoder its last sample.
    private static bool FeedInput(MediaExtractor extractor, MediaCodec codec)
    {
        var inputIndex = codec.DequeueInputBuffer(TimeoutMicroseconds);
        if (inputIndex < 0)
        {
            return false;
        }

        var inputBuffer = codec.GetInputBuffer(inputIndex)!;
        var sampleSize = extractor.ReadSampleData(inputBuffer, 0);
        if (sampleSize < 0)
        {
            codec.QueueInputBuffer(inputIndex, 0, 0, 0, MediaCodecBufferFlags.EndOfStream);
            return true;
        }

        codec.QueueInputBuffer(inputIndex, 0, sampleSize, extractor.SampleTime, MediaCodecBufferFlags.None);
        extractor.Advance();
        return false;
    }

    // Returns true once the decoder has reported end-of-stream on its output side.
    private static bool DrainOutput(
        MediaCodec codec, MediaCodec.BufferInfo bufferInfo, int samplesPerFrame, List<short> accumulator,
        List<float> levels)
    {
        var outputIndex = codec.DequeueOutputBuffer(bufferInfo, TimeoutMicroseconds);
        if (outputIndex < 0)
        {
            // A negative index below -1 is a format/buffer-set change, not a frame —
            // nothing to release, and the next call tries again.
            return false;
        }

        var endOfStream = (bufferInfo.Flags & MediaCodecBufferFlags.EndOfStream) != 0;
        if (bufferInfo.Size > 0)
        {
            var outputBuffer = codec.GetOutputBuffer(outputIndex)!;
            AccumulateSamples(outputBuffer, bufferInfo, samplesPerFrame, accumulator, levels);
        }

        codec.ReleaseOutputBuffer(outputIndex, render: false);
        return endOfStream;
    }

    // PCM 16-bit little-endian is what every decoder on Android emits regardless of
    // the source codec — that normalization is the whole point of going through
    // MediaCodec rather than reading each codec's own bitstream by hand.
    private static void AccumulateSamples(
        ByteBuffer outputBuffer, MediaCodec.BufferInfo bufferInfo, int samplesPerFrame,
        List<short> accumulator, List<float> levels)
    {
        var pcm = new byte[bufferInfo.Size];
        outputBuffer.Position(bufferInfo.Offset);
        outputBuffer.Limit(bufferInfo.Offset + bufferInfo.Size);
        outputBuffer.Get(pcm);

        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            accumulator.Add((short)(pcm[i] | (pcm[i + 1] << 8)));
            if (accumulator.Count >= samplesPerFrame)
            {
                levels.Add(Rms(accumulator));
                accumulator.Clear();
            }
        }
    }

    private static float Rms(List<short> samples)
    {
        var sumSquares = 0.0;
        foreach (var sample in samples)
        {
            var normalized = sample / 32768.0;
            sumSquares += normalized * normalized;
        }

        return (float)Math.Sqrt(sumSquares / samples.Count);
    }
}
