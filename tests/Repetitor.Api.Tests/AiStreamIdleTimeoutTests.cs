using System.Text;
using Repetitor.Api.Infrastructure.Ai;

namespace Repetitor.Api.Tests;

public sealed class AiStreamIdleTimeoutTests
{
    /// <summary>Поток, который вечно молчит: провайдер принял соединение и не присылает данных.</summary>
    private sealed class SilentStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    [Fact]
    public async Task StalledStream_FailsInsteadOfHangingForever()
    {
        using var reader = new StreamReader(new SilentStream(), Encoding.UTF8);

        var error = await Assert.ThrowsAsync<AiProviderException>(
            () => AiHttp.ReadLineWithIdleTimeoutAsync(
                reader, TimeSpan.FromMilliseconds(200), "ollama", CancellationToken.None));

        Assert.Contains("stalled", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ActiveStream_ReadsLinesUntilEnd()
    {
        using var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("{\"a\":1}\n\n{\"b\":2}\n")), Encoding.UTF8);
        var lines = new List<string>();

        while (await AiHttp.ReadLineWithIdleTimeoutAsync(reader, TimeSpan.FromSeconds(30), "ollama", CancellationToken.None)
                   is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        Assert.Equal(["{\"a\":1}", "{\"b\":2}"], lines);
    }

    [Fact]
    public async Task CallerCancellation_IsNotReportedAsStall()
    {
        using var reader = new StreamReader(new SilentStream(), Encoding.UTF8);
        using var cts = new CancellationTokenSource();

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AiHttp.ReadLineWithIdleTimeoutAsync(reader, TimeSpan.FromSeconds(30), "ollama", cts.Token));
    }

    [Theory]
    [InlineData(5, 30)]
    [InlineData(120, 120)]
    [InlineData(100000, 900)]
    public void IdleTimeout_IsClampedToSaneBounds(int configured, int expected)
    {
        Assert.Equal(expected, AiHttp.IdleTimeout(configured).TotalSeconds);
    }
}
