using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PalindromeServer
{
    public class WebServer
    {
        private readonly HttpListener _listener;
        private readonly RequestQueue _queue;
        private readonly Cache _cache;
        private readonly string _rootFolder;
        private readonly SemaphoreSlim _concurrencySemaphore;
        private readonly int _maxConcurrent;
        private volatile bool _isRunning = false;
        private Thread? _acceptThread;
        private Thread? _dispatchThread;

        public WebServer(string rootFolder, int maxConcurrent = 8, int cacheSize = 10)
        {
            _rootFolder = rootFolder;
            _maxConcurrent = maxConcurrent;
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://localhost:5050/");
            _queue = new RequestQueue(200);
            _cache = new Cache(cacheSize);
            _concurrencySemaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        }

        public void Start()
        {
            _isRunning = true;
            _listener.Start();
            Logger.Log($"Server pokrenut | max konkurentnih: {_maxConcurrent}");

            // Klasicna dedicated nit za prijem zahteva
            // GetContext() je blokirajuci poziv - klasicna nit je opravdana
            _acceptThread = new Thread(AcceptLoop)
            {
                Name = "AcceptThread",
                IsBackground = true
            };
            _acceptThread.Start();

            // Klasicna dedicated nit za dispatch
            // DequeueAsync() ceka na zahteve - klasicna nit je opravdana
            _dispatchThread = new Thread(() => DispatchLoop().GetAwaiter().GetResult())
            {
                Name = "DispatchThread",
                IsBackground = true
            };
            _dispatchThread.Start();

            Logger.Log("AcceptThread i DispatchThread pokrenuti");
        }

        /// <summary>
        /// Prijem zahteva - KLASICNA NIT
        /// GetContext() blokira dok ne stigne HTTP zahtev
        /// </summary>
        private void AcceptLoop()
        {
            Logger.Log("AcceptLoop: pokrenut");
            while (_isRunning)
            {
                try
                {
                    HttpListenerContext context = _listener.GetContext();
                    string fileName = context.Request.Url!.AbsolutePath.TrimStart('/');
                    Logger.Log($"PRIJEM: '{fileName}'");

                    // EnqueueAsync je async - koristimo GetAwaiter().GetResult()
                    // jer smo u klasicnoj niti koja ne moze await
                    _queue.EnqueueAsync(context).GetAwaiter().GetResult();
                }
                catch (HttpListenerException) when (!_isRunning) { break; }
                catch (Exception ex)
                {
                    if (_isRunning) Logger.Log($"AcceptLoop GRESKA: {ex.Message}");
                }
            }
            Logger.Log("AcceptLoop: zaustavljen");
        }

        /// <summary>
        /// Dispatch petlja - KLASICNA NIT koja pokrece async taskove
        /// Uzima zahteve iz reda i delegira obradu ThreadPool-u
        /// </summary>
        private async Task DispatchLoop()
        {
            Logger.Log("DispatchLoop: pokrenut");
            while (_isRunning)
            {
                HttpListenerContext? context = await _queue.DequeueAsync();
                if (context == null) break;

                // Cekamo slobodan slot
                await _concurrencySemaphore.WaitAsync();
                Logger.Log($"DISPATCH: Startuje obrada, slobodnih: {_concurrencySemaphore.CurrentCount}/{_maxConcurrent}");

                // Eksplicitna upotreba ThreadPool-a
                // async lambda omogucava await unutar ThreadPool niti
                ThreadPool.QueueUserWorkItem(async _ =>
                {
                    try
                    {
                        await ProcessRequestAsync(context);
                    }
                    finally
                    {
                        _concurrencySemaphore.Release();
                        Logger.Log($"DISPATCH: Obrada zavrsena, slobodnih: {_concurrencySemaphore.CurrentCount}/{_maxConcurrent}");
                    }
                });
            }
            Logger.Log("DispatchLoop: zaustavljen");
        }

        /// <summary>
        /// Asinhrona obrada jednog HTTP zahteva - izvrsava se na ThreadPool niti
        /// Sve I/O operacije su async - nit nije blokirana
        /// </summary>
        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            string fileName = context.Request.Url!.AbsolutePath.TrimStart('/');
            Logger.Log($"OBRADA: '{fileName}'");

            try
            {
                // Korak 1: async pretraga fajla
                string? filePath = await PalindromeCounter.FindFileAsync(_rootFolder, fileName);

                if (filePath == null)
                {
                    await SendResponseAsync(context,
                        $"Greska: Fajl '{fileName}' nije pronadjen.", 404);
                    return;
                }

                // Korak 2: dohvatamo iz kesa ili racunamo
                int count = await _cache.GetOrComputeAsync(
                    fileName,
                    () => PalindromeCounter.CountAsync(filePath));

                // Korak 3: saljemo odgovor
                string msg = count == 0
                    ? $"Fajl '{fileName}' ne sadrzi nijedan palindrom."
                    : $"Fajl '{fileName}' sadrzi {count} palindrom(a).";

                await SendResponseAsync(context, msg, 200);
            }
            catch (Exception ex)
            {
                Logger.Log($"OBRADA GRESKA '{fileName}': {ex.Message}");
                await SendResponseAsync(context, $"Interna greska: {ex.Message}", 500);
            }
        }

        /// <summary>
        /// Asinhrono slanje HTTP odgovora
        /// WriteAsync - nit nije blokirana tokom slanja
        /// </summary>
        private async Task SendResponseAsync(HttpListenerContext context, string message, int statusCode)
        {
            try
            {
                HttpListenerResponse response = context.Response;
                response.StatusCode = statusCode;
                response.ContentType = "text/plain; charset=utf-8";
                byte[] buffer = Encoding.UTF8.GetBytes(message);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
                response.Close();
                Logger.Log($"ODGOVOR [{statusCode}]: {message}");
            }
            catch (Exception ex)
            {
                Logger.Log($"GRESKA slanja: {ex.Message}");
            }
        }

        public void Stop()
        {
            Logger.Log("Server: zaustavljam...");
            _isRunning = false;
            _listener.Stop();
            _queue.Stop();
            _acceptThread?.Join(2000);
            _dispatchThread?.Join(2000);
            Logger.Log("Server: zaustavljen");
        }
    }
}