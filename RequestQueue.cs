using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PalindromeServer
{
    public class RequestQueue
    {
        private readonly Queue<HttpListenerContext> _queue;
        private readonly object _queueLock = new object();
        private readonly SemaphoreSlim _itemAvailable;
        private readonly SemaphoreSlim _spaceAvailable;
        private readonly int _maxSize;
        private volatile bool _isStopped = false;

        public RequestQueue(int maxSize = 100)
        {
            _maxSize = maxSize;
            _queue = new Queue<HttpListenerContext>();
            // signalizira da ima zahteva u redu
            _itemAvailable = new SemaphoreSlim(0, maxSize);
            // signalizira da ima mesta u redu
            _spaceAvailable = new SemaphoreSlim(maxSize, maxSize);
        }

        // Zove je AcceptThread - stavlja zahtev u red
        public async Task EnqueueAsync(HttpListenerContext context)
        {
            // Cekamo slobodno mesto - async, ne blokira nit
            await _spaceAvailable.WaitAsync();

            if (_isStopped) return;

            lock (_queueLock)
            {
                _queue.Enqueue(context);
                Logger.Log($"RED: Dodat zahtev, u redu: {_queue.Count}/{_maxSize}");
            }

            // Signaliziramo da ima novog zahteva
            _itemAvailable.Release();
        }

        // Zove je DispatchThread - uzima zahtev iz reda
        public async Task<HttpListenerContext?> DequeueAsync()
        {
            // Cekamo da se pojavi zahtev - async, ne blokira nit
            await _itemAvailable.WaitAsync();

            if (_isStopped) return null;

            HttpListenerContext context;
            lock (_queueLock)
            {
                context = _queue.Dequeue();
                Logger.Log($"RED: Uzet zahtev, ostalo: {_queue.Count}/{_maxSize}");
            }

            // Signaliziramo da se oslobodilo mesto
            _spaceAvailable.Release();
            return context;
        }

        public void Stop()
        {
            _isStopped = true;
            // Budimo sve cekajuce operacije
            _itemAvailable.Release(_maxSize);
            _spaceAvailable.Release(_maxSize);
            Logger.Log("RED: Zaustavljanje");
        }

        public int Count
        {
            get { lock (_queueLock) { return _queue.Count; } }
        }
    }
}