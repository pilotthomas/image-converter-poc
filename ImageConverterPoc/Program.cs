using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ImageConverterPoc
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            ConversionRunner.ConfigureMagickEnvironment();

            var flags = new HashSet<string>(
                args.Where(a => a.StartsWith("--", StringComparison.Ordinal)),
                StringComparer.OrdinalIgnoreCase);
            var positionals = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();

            var dryRun = flags.Contains("--dry-run");
            var useTest = flags.Contains("--test");
            var seedTestAssets = flags.Contains("--seed-test-assets");

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string xmlPath;
            if (positionals.Count > 0)
                xmlPath = Path.GetFullPath(positionals[0]);
            else
                xmlPath = Path.Combine(baseDir, useTest ? "ConversionTask20.Test.xml" : "ConversionTask20.xml");

            if (!File.Exists(xmlPath))
            {
                Console.Error.WriteLine($"Task file not found: {xmlPath}");
                PrintUsage();
                return 1;
            }

            var taskFileDirectory = Path.GetDirectoryName(Path.GetFullPath(xmlPath));
            if (string.IsNullOrEmpty(taskFileDirectory))
                taskFileDirectory = baseDir;

            if (seedTestAssets)
            {
                try
                {
                    TestDataSeeder.Seed(taskFileDirectory);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    return 2;
                }

                if (!flags.Contains("--run-after-seed"))
                    return 0;
            }

            try
            {
                new ConversionRunner().RunAll(xmlPath, dryRun);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 2;
            }

            return 0;
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  Production task file (copied next to exe):");
            Console.Error.WriteLine("    ImageConverterPoc [--dry-run] [full\\path\\to\\ConversionTask20.xml]");
            Console.Error.WriteLine("  Local test tasks (ConversionTask20.Test.xml next to exe):");
            Console.Error.WriteLine("    ImageConverterPoc --test [--dry-run]");
            Console.Error.WriteLine("  Create sample inputs under TestData\\ (next to the test XML):");
            Console.Error.WriteLine("    ImageConverterPoc --test --seed-test-assets");
            Console.Error.WriteLine("  Seed and run conversion in one go:");
            Console.Error.WriteLine("    ImageConverterPoc --test --seed-test-assets --run-after-seed");
        }
    }
}
