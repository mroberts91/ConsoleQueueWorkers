using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CbUploader
{
    public abstract class QueueBase<T>
    {
        private readonly ConcurrentQueue<T> _queue;

        public QueueBase() { _queue = new(); }

        public Task Add(T file) => Task.Run(() => _queue.Enqueue(file));

        public async Task Add(IEnumerable<T> files) =>  await Task.WhenAll(files.Select(f => Add(f)));

        public Task<T?> GetNext() => Task.Run(() => _queue.TryDequeue(out T result) ? result : (T?)(object?)null);

        public Task<int> CurrentCount() => Task.FromResult(_queue.Count);

        public Task<bool> IsEmpty() => Task.FromResult(!_queue.Any());

        public override string ToString()
        {
            return $"{GetType().FullName} - Count: {_queue.Count}";
        }
    }

    public class ProcessingQueue : QueueBase<ClientBillingFile> { }

    public class DeadLetterQueue : QueueBase<ClientBillingFile> { }
}
