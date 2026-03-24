namespace UnityExplorer.MCP
{
    /// <summary>
    /// Thread-safe queue using lock. Works on net35 where ConcurrentQueue is unavailable.
    /// </summary>
    internal class LockedQueue<T>
    {
        private readonly Queue<T> queue = new();
        private readonly object lockObj = new();

        public void Enqueue(T item)
        {
            lock (lockObj)
                queue.Enqueue(item);
        }

        public bool TryDequeue(out T item)
        {
            lock (lockObj)
            {
                if (queue.Count > 0)
                {
                    item = queue.Dequeue();
                    return true;
                }
                item = default;
                return false;
            }
        }

        public int Count
        {
            get { lock (lockObj) return queue.Count; }
        }

        public void Clear()
        {
            lock (lockObj)
                queue.Clear();
        }
    }
}
