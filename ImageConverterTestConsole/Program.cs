using System;
using System.IO;
using ImageConverterPoc;

namespace ImageConverterTestConsole
{
    internal static class Program
    {
        private static int Main()
        {
            ConversionRunner.ConfigureMagickEnvironment();

            Console.WriteLine("Image converter POC — test console (not a Windows service).");
            Console.WriteLine("Output / test files are next to: " + AppDomain.CurrentDomain.BaseDirectory);
            Console.WriteLine();

            while (true)
            {
                Console.WriteLine("Choose an option:");
                Console.WriteLine("  1  Seed local test inputs (TestData\\...\\In)");
                Console.WriteLine("  2  Run local test conversion (ConversionTask20.Test.xml)");
                Console.WriteLine("  3  Dry-run local test tasks");
                Console.WriteLine("  4  Run conversion from a custom task XML path");
                Console.WriteLine("  5  Dry-run using ConversionTask20.xml next to this exe");
                Console.WriteLine("  0  Exit");
                Console.Write("> ");

                var choice = (Console.ReadLine() ?? string.Empty).Trim();
                Console.WriteLine();

                try
                {
                    switch (choice)
                    {
                        case "0":
                            return 0;
                        case "1":
                            SeedLocal();
                            break;
                        case "2":
                            RunLocal(dryRun: false);
                            break;
                        case "3":
                            RunLocal(dryRun: true);
                            break;
                        case "4":
                            RunCustomXml();
                            break;
                        case "5":
                            RunBundledProductionDryRun();
                            break;
                        default:
                            Console.WriteLine("Unknown option. Try 1–5 or 0.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                    if (ex.InnerException != null)
                        Console.WriteLine("  " + ex.InnerException.Message);
                }

                Console.WriteLine();
            }
        }

        private static string TestTaskPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ConversionTask20.Test.xml");

        private static string ProductionTaskPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ConversionTask20.xml");

        private static void SeedLocal()
        {
            var xmlPath = TestTaskPath;
            if (!File.Exists(xmlPath))
                throw new FileNotFoundException("Missing test task file next to exe.", xmlPath);

            var dir = Path.GetDirectoryName(Path.GetFullPath(xmlPath));
            if (string.IsNullOrEmpty(dir))
                throw new InvalidOperationException("Could not resolve task file directory.");

            TestDataSeeder.Seed(dir);
            Console.WriteLine("Done. You can run option 2 or 3 next.");
        }

        private static void RunLocal(bool dryRun)
        {
            var xmlPath = TestTaskPath;
            if (!File.Exists(xmlPath))
                throw new FileNotFoundException("Missing test task file next to exe.", xmlPath);

            RunLog.WriteLine(dryRun ? "Dry run (no writes):" : "Running conversion:");
            RunLog.WriteLine(xmlPath);
            new ConversionRunner().RunAll(xmlPath, dryRun);
            RunLog.WriteLine(dryRun ? "Dry run finished." : "Conversion finished.");
        }

        private static void RunCustomXml()
        {
            Console.Write("Full path to task XML: ");
            var path = (Console.ReadLine() ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path))
            {
                Console.WriteLine("Cancelled.");
                return;
            }

            path = Path.GetFullPath(path);
            if (!File.Exists(path))
                throw new FileNotFoundException("File not found.", path);

            Console.Write("Dry run? (y/N): ");
            var dry = (Console.ReadLine() ?? string.Empty).Trim().Equals("y", StringComparison.OrdinalIgnoreCase);

            RunLog.WriteLine(dry ? "Dry run:" : "Run:");
            RunLog.WriteLine(path);
            new ConversionRunner().RunAll(path, dry);
            RunLog.WriteLine(dry ? "Dry run finished." : "Conversion finished.");
        }

        private static void RunBundledProductionDryRun()
        {
            var xmlPath = ProductionTaskPath;
            if (!File.Exists(xmlPath))
                throw new FileNotFoundException("Missing ConversionTask20.xml next to exe.", xmlPath);

            RunLog.WriteLine("Dry run using bundled production template (UNC paths may be skipped):");
            RunLog.WriteLine(xmlPath);
            new ConversionRunner().RunAll(xmlPath, dryRun: true);
            RunLog.WriteLine("Dry run finished.");
        }
    }
}
