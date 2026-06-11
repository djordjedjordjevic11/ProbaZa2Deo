using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PalindromeServer
{
    public class Cache
    {
        private readonly Dictionary<string, int> _store;
        private readonly Queue<string> _order;
        private readonly Dictionary<string, TaskCompletionSource<int>> _inProgress;
        private readonly int _maxSize;
        private readonly object _storeLock = new object();

        public Cache(int maxSize = 10)
        {
            _maxSize = maxSize;
            _store = new Dictionary<string, int>();
            _order = new Queue<string>();
            _inProgress = new Dictionary<string, TaskCompletionSource<int>>();
        }

        public async Task<int> GetOrComputeAsync(string key, Func<Task<int>> computeAsync)
        {
            var stopwatch = Stopwatch.StartNew();
            TaskCompletionSource<int>? existingTcs = null;

            lock (_storeLock)
            {
                // 1. Proveravamo da li postoji u kesu
                if (_store.ContainsKey(key))
                {
                    Logger.Log($"KES HIT: '{key}' - {stopwatch.ElapsedMilliseconds}ms");
                    return _store[key];
                }

                // 2. Da li neki task vec racuna ovaj kljuc? (cache stampede)
                if (_inProgress.TryGetValue(key, out existingTcs))
                {
                    Logger.Log($"KES: Cekam na vec pokrenuto racunanje za '{key}'");
                }
                else
                {
                    // 3. Niko ne racuna - registrujemo TCS i preuzimamo racunanje
                    var tcs = new TaskCompletionSource<int>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _inProgress[key] = tcs;
                    Logger.Log($"KES MISS: '{key}' - ovaj task racuna");
                }
            }

            // Ako neko drugi vec racuna - await bez blokiranja niti
            if (existingTcs != null)
            {
                int waitedResult = await existingTcs.Task;
                Logger.Log($"KES: Dobijen rezultat za '{key}': {waitedResult}");
                return waitedResult;
            }

            // Uzimamo nas TCS izvan locka
            TaskCompletionSource<int> ourTcs;
            lock (_storeLock) { ourTcs = _inProgress[key]; }

            int value;
            try
            {
                // Racunamo IZVAN locka - nit nije blokirana
                // ContinueWith: logujemo kada racunanje zavrsi
                value = await computeAsync().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Logger.Log($"KES: Racunanje za '{key}' nije uspelo: {t.Exception?.InnerException?.Message}");
                        throw t.Exception!.InnerException!;
                    }
                    Logger.Log($"KES: Racunanje za '{key}' zavrseno, rezultat: {t.Result}");
                    return t.Result;
                }, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                lock (_storeLock) { _inProgress.Remove(key); }
                ourTcs.SetException(ex);
                Logger.Log($"KES GRESKA za '{key}': {ex.Message}");
                throw;
            }

            // Upisujemo rezultat u kes
            lock (_storeLock)
            {
                // Eviction - brisemo najstariji ako je kes pun
                if (_store.Count >= _maxSize)
                {
                    string oldest = _order.Dequeue();
                    _store.Remove(oldest);
                    Logger.Log($"KES PUN: Obrisan najstariji '{oldest}'");
                }

                _store[key] = value;
                _order.Enqueue(key);
                _inProgress.Remove(key);

                stopwatch.Stop();
                Logger.Log($"KES: Upisan '{key}' = {value}, vreme: {stopwatch.ElapsedMilliseconds}ms");
            }

            // Signaliziramo svim cekajucim taskovima da je rezultat spreman
            ourTcs.SetResult(value);
            return value;
        }

        public int Count
        {
            get { lock (_storeLock) { return _store.Count; } }
        }
    }
}