using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace PalindromeServer
{
    public class RequestQueue
    {
        private readonly Queue<HttpListenerContext> _queue;
        private readonly object _queueLock = new object();
        private readonly int _maxSize;
        private bool _isStopped = false;

        public RequestQueue(int maxSize = 100)
        {
            _maxSize = maxSize;
            _queue = new Queue<HttpListenerContext>();
        }

        // Zove je AcceptThread - stavlja zahtev u red
        public bool EnqueueContext(HttpListenerContext context)
        {
            lock (_queueLock)
            {
                if (_isStopped) return false;

                // Cekamo dok se ne oslobodi mesto u redu
                while (_queue.Count >= _maxSize && !_isStopped)
                {
                    Logger.Log($"RED PUN ({_queue.Count}/{_maxSize}): Producer ceka...");
                    Monitor.Wait(_queueLock);
                }

                if (_isStopped) return false;

                _queue.Enqueue(context);
                Logger.Log($"RED: Dodat zahtev, u redu: {_queue.Count}/{_maxSize}");
                Monitor.Pulse(_queueLock);
                return true;
            }
        }

        // Zove je DispatchThread - uzima zahtev iz reda
        public HttpListenerContext? Dequeue()
        {
            lock (_queueLock)
            {
                // Cekamo dok ne stigne neki zahtev
                while (_queue.Count == 0 && !_isStopped)
                {
                    Logger.Log("RED PRAZAN: Consumer ceka...");
                    Monitor.Wait(_queueLock);
                }

                if (_queue.Count == 0) return null;

                HttpListenerContext context = _queue.Dequeue();
                Logger.Log($"RED: Uzet zahtev, ostalo: {_queue.Count}/{_maxSize}");
                Monitor.Pulse(_queueLock);
                return context;
            }
        }

        public void Stop()
        {
            lock (_queueLock)
            {
                _isStopped = true;
                Monitor.PulseAll(_queueLock);
                Logger.Log("RED: Zaustavljanje");
            }
        }

        public int Count
        {
            get { lock (_queueLock) { return _queue.Count; } }
        }
    }
}