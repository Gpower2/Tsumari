namespace Tsumari.Bot.Services;

// Fixed stripes bound memory use. Shared by source edit synchronization and retry commits.
public sealed class MessageMutationCoordinator
{
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 256).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    public async Task<IDisposable> AcquireAsync(ulong originalId)
    {
        var gate = _locks[originalId % (ulong)_locks.Length];
        await gate.WaitAsync();
        return new Lease(gate);
    }
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
