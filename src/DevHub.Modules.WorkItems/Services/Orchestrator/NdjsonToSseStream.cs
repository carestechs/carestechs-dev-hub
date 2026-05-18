using System.Text;
using System.Text.Json;

namespace DevHub.Modules.WorkItems.Services.Orchestrator;

/// <summary>
/// FEAT-010: read-only <see cref="Stream"/> that converts the orchestrator's
/// <c>application/x-ndjson</c> trace into <c>text/event-stream</c> frames.
///
/// Behavior:
/// <list type="bullet">
/// <item>Emits an initial <c>: ready\n\n</c> heartbeat so the SSE client's <c>onopen</c>
/// fires quickly.</item>
/// <item>One NDJSON line in → one <c>data: &lt;json&gt;\n\n</c> frame out.</item>
/// <item>Blank / whitespace-only lines suppressed.</item>
/// <item>Malformed JSON lines suppressed (the SSE consumer never sees them).</item>
/// </list>
///
/// Reads from the upstream stream line-by-line; closes the upstream when disposed.
/// </summary>
internal sealed class NdjsonToSseStream : Stream
{
    private readonly Stream _upstream;
    private readonly StreamReader _reader;
    private readonly Queue<byte[]> _outbox = new();
    private byte[] _current = Array.Empty<byte>();
    private int _currentOffset;
    private bool _eof;

    public NdjsonToSseStream(Stream upstream)
    {
        _upstream = upstream;
        _reader = new StreamReader(upstream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: false);
        // Initial heartbeat — emitted before any upstream byte is read.
        _outbox.Enqueue(Encoding.UTF8.GetBytes(": ready\n\n"));
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        // Sync read kept for completeness; ASP.NET Core will call ReadAsync.
        return ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return 0;

        while (_currentOffset >= _current.Length)
        {
            if (_outbox.TryDequeue(out var queued))
            {
                _current = queued;
                _currentOffset = 0;
                continue;
            }

            if (_eof) return 0;

            // Pull the next non-empty NDJSON line from the upstream and convert.
            var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                _eof = true;
                return 0;
            }
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Validate JSON; suppress malformed lines (matches the orchestrator's "skip
            // bad records, don't break the stream" semantics).
            try
            {
                using var _ = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            _current = Encoding.UTF8.GetBytes($"data: {line}\n\n");
            _currentOffset = 0;
        }

        var available = _current.Length - _currentOffset;
        var toCopy = Math.Min(available, buffer.Length);
        new ReadOnlySpan<byte>(_current, _currentOffset, toCopy).CopyTo(buffer.Span);
        _currentOffset += toCopy;
        return toCopy;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reader.Dispose();
            _upstream.Dispose();
        }
        base.Dispose(disposing);
    }
}
