using System;
using System.IO;
using System.Threading.Tasks;

namespace PalindromeServer
{
    public static class PalindromeCounter
    {
        private static readonly char[] Separators = {
            ' ', '\n', '\r', '\t', '.', ',', '!', '?', ':', ';', '-', '(', ')', '[', ']'
        };

        // ContinueWith demonstracija: nakon sto I/O zavrsi, 
        // kontinuacija broji palindrome
        public static Task<int> CountAsync(string filePath)
        {
            Logger.Log($"PALINDROMI: Citam '{filePath}'");

            return File.ReadAllTextAsync(filePath)
                .ContinueWith(readTask =>
                {
                    if (readTask.IsFaulted)
                    {
                        Logger.Log($"PALINDROMI: Greska citanja: {readTask.Exception?.InnerException?.Message}");
                        throw readTask.Exception!.InnerException!;
                    }

                    string content = readTask.Result;
                    Logger.Log($"PALINDROMI: Procitano {content.Length} karaktera, brojim...");

                    string[] words = content.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
                    int count = 0;

                    foreach (string word in words)
                    {
                        string clean = word.ToLower();
                        if (clean.Length > 1 && IsPalindrome(clean))
                        {
                            count++;
                            Logger.Log($"PALINDROM: '{clean}'");
                        }
                    }

                    Logger.Log($"PALINDROMI: Ukupno {count} palindroma");
                    return count;

                }, TaskScheduler.Default);
        }

        // ContinueWith demonstracija: nakon pretrage,
        // kontinuacija loguje rezultat
        public static Task<string?> FindFileAsync(string rootFolder, string fileName)
        {
            Logger.Log($"PRETRAGA: Trazim '{fileName}'");

            return Task.Run(() =>
                Directory.GetFiles(rootFolder, fileName, SearchOption.AllDirectories))
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Logger.Log($"PRETRAGA GRESKA: {t.Exception?.InnerException?.Message}");
                    return (string?)null;
                }

                string? found = t.Result.Length > 0 ? t.Result[0] : null;

                Logger.Log(found != null
                    ? $"PRETRAGA: Pronadjen '{found}'"
                    : $"PRETRAGA: '{fileName}' nije pronadjen");

                return found;
            }, TaskScheduler.Default);
        }

        private static bool IsPalindrome(string word)
        {
            int left = 0, right = word.Length - 1;
            while (left < right)
            {
                if (word[left] != word[right]) return false;
                left++;
                right--;
            }
            return true;
        }
    }
}