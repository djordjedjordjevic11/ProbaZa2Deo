using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PalindromeServer
{
    public class Logger
    {
        private static readonly object logLock = new object();
        private static readonly string logFile = "server.log";

        public static void Log(string message)
        {
            // Ako se izvrsava unutar Taska prikazujemo Task ID, inace Thread ID
            string taskInfo = Task.CurrentId.HasValue
                ? $"Task-{Task.CurrentId}"
                : $"Nit-{Thread.CurrentThread.ManagedThreadId}";

            string line = $"[{DateTime.Now:HH:mm:ss}][{taskInfo}] {message}";

            // KRITICNA SEKCIJA: stiti i konzolu i fajl
            lock (logLock)
            {
                Console.WriteLine(line);
                File.AppendAllText(logFile, line + Environment.NewLine);
            }
        }
    }
}