namespace Nexus.Core.Pipeline;

public interface IEventSink
{
    ValueTask PublishCriticalAsync(StateEvent evt, CancellationToken ct = default);
    ValueTask PublishProgressAsync(ProgressEvent evt, CancellationToken ct = default);
}
