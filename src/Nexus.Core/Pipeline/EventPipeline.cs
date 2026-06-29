using System.Threading.Channels;

namespace Nexus.Core.Pipeline;

/// <summary>
/// Two-channel pipeline: critical (bounded, no drop) and progress (bounded, DropOldest).
/// A SINGLE serial consumer loop is the only path that processes events — handlers are
/// never called concurrently, guaranteeing the single-writer invariant.
/// </summary>
public sealed class EventPipeline : IEventSink, IAsyncDisposable
{
    private readonly Channel<StateEvent> _critical;
    private readonly Channel<ProgressEvent> _progress;
    private readonly List<Func<StateEvent, Task>> _handlers = new();

    public EventPipeline()
    {
        _critical = Channel.CreateBounded<StateEvent>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

        _progress = Channel.CreateBounded<ProgressEvent>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
    }

    public void RegisterHandler(Func<StateEvent, Task> handler) =>
        _handlers.Add(handler);

    public ValueTask PublishCriticalAsync(StateEvent evt, CancellationToken ct = default) =>
        _critical.Writer.WriteAsync(evt, ct);

    public ValueTask PublishProgressAsync(ProgressEvent evt, CancellationToken ct = default) =>
        _progress.Writer.WriteAsync(evt, ct);

    public void Complete()
    {
        _critical.Writer.TryComplete();
        _progress.Writer.TryComplete();
    }

    /// <summary>
    /// Single serial consumer: reads from critical first (no-drop, priority),
    /// then progress (drop-oldest). Never two concurrent calls to handlers.
    /// </summary>
    public async Task RunConsumerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Drain all pending critical events first (priority, no drop)
            while (_critical.Reader.TryRead(out var critEvt))
                await DispatchAsync(critEvt);

            // One progress tick
            if (_progress.Reader.TryRead(out var progEvt))
            {
                await DispatchAsync(progEvt);
                continue;
            }

            // Nothing ready — wait for either channel to have data
            var critReady = _critical.Reader.WaitToReadAsync(ct).AsTask();
            var progReady = _progress.Reader.WaitToReadAsync(ct).AsTask();
            await Task.WhenAny(critReady, progReady);
        }
    }

    private async Task DispatchAsync(StateEvent evt)
    {
        foreach (var handler in _handlers)
            await handler(evt);
    }

    public ValueTask DisposeAsync()
    {
        Complete();
        return ValueTask.CompletedTask;
    }
}
