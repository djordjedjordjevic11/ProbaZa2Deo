using System;
using System.IO;
using System.Threading;

namespace PalindromeServer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string rootFolder = Path.Combine(Directory.GetCurrentDirectory(), "Files");

            if (!Directory.Exists(rootFolder))
            {
                Directory.CreateDirectory(rootFolder);
                Logger.Log($"Napravljen root folder: {rootFolder}");
            }

            Logger.Log($"Root folder: {rootFolder}");

            WebServer server = new WebServer(
                rootFolder,
                maxConcurrent: 8,
                cacheSize: 10
            );

            server.Start();

            Console.WriteLine("=========================================");
            Console.WriteLine("  Palindrome Server V2 (Tasks + Async)  ");
            Console.WriteLine("  http://localhost:5050/fajl.txt         ");
            Console.WriteLine("  Ukucaj Q + Enter za gasenje            ");
            Console.WriteLine("=========================================");

            string input = "";
            while (input != "Q" && input != "q")
            {
                input = Console.ReadLine() ?? "";
            }

            server.Stop();
            Logger.Log("Server ugasen");
        }
    }
}