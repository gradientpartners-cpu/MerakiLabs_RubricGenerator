using System.Threading.Channels;
using RubricGrader.Grading;

namespace RubricGrader.Worker;

/// <summary><see cref="IGradeQueue"/> backed by a bounded channel. Single reader (one
/// worker), bounded capacity with Wait full-mode = backpressure on the enqueuer.</summary>
public sealed class ChannelGradeQueue : IGradeQueue
{
    private readonly Channel<GradeRequest> _channel = Channel.CreateBounded<GradeRequest>(
        new BoundedChannelOptions(capacity: 100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    public ValueTask EnqueueAsync(GradeRequest request, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(request, ct);

    public IAsyncEnumerable<GradeRequest> DequeueAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
