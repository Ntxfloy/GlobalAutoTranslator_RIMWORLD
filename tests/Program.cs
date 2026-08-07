using System;
using System.Diagnostics;
using GlobalAutoTranslator;

namespace GlobalAutoTranslatorTests
{
    class Program
    {
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            SelfTest.Run();

            // Task 5: Measure Invalidation Cost
            Console.WriteLine("\n=== Performance Benchmark ===");
            
            // Populate TranslationCache with 15k items
            for (int i = 0; i < 15000; i++) {
                TranslationCache.Put("ui", "test_key_" + i, "test_val_" + i);
            }

            var sw = Stopwatch.StartNew();
            int misses = 0;

            for (int i = 0; i < 20000; i++) {
                string dummy;
                if (!TranslationCache.TryGetFlat("missing_key_" + i, out dummy)) {
                    misses++;
                }

                // Increment generation every 50 calls to simulate background loads
                if (i % 50 == 0) {
                    TranslationCache.Put("ui", "trigger_" + i, "val_" + i);
                }
            }

            sw.Stop();
            Console.WriteLine($"Total 20,000 TryGetFlat misses with invalidations took: {sw.ElapsedMilliseconds} ms");
            Console.WriteLine($"Average time per call: {(sw.Elapsed.TotalMilliseconds * 1000.0) / 20000.0:F2} microseconds");
        }
    }
}
