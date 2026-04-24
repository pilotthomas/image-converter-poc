using System;
using System.IO;
using ImageMagick;
using ImageMagick.Drawing;

namespace ImageConverterPoc
{
    /// <summary>
    /// Creates small sample files under TestData\...\In next to the test task XML (for local runs).
    /// </summary>
    internal static class TestDataSeeder
    {
        public static void Seed(string taskFileDirectory)
        {
            if (string.IsNullOrWhiteSpace(taskFileDirectory))
                throw new ArgumentException("Task file directory is required.", nameof(taskFileDirectory));

            Directory.CreateDirectory(Path.Combine(taskFileDirectory, @"TestData\01-single-g4\In"));
            Directory.CreateDirectory(Path.Combine(taskFileDirectory, @"TestData\02-decollate-g4\In"));
            Directory.CreateDirectory(Path.Combine(taskFileDirectory, @"TestData\03-multipage-pdf\In"));
            Directory.CreateDirectory(Path.Combine(taskFileDirectory, @"TestData\04-unique-name\In"));
            Directory.CreateDirectory(Path.Combine(taskFileDirectory, @"TestData\05-lzw-single\In"));
            Directory.CreateDirectory(Path.Combine(taskFileDirectory, @"TestData\06-lzw-decollate\In"));

            WriteSinglePagePng(Path.Combine(taskFileDirectory, @"TestData\01-single-g4\In\sample.png"));
            WriteTwoPageTiff(Path.Combine(taskFileDirectory, @"TestData\02-decollate-g4\In\two-page.tif"));
            WriteTwoPageTiff(Path.Combine(taskFileDirectory, @"TestData\03-multipage-pdf\In\two-page.tif"));
            WriteSinglePagePng(Path.Combine(taskFileDirectory, @"TestData\04-unique-name\In\sample.png"));
            WriteSinglePagePng(Path.Combine(taskFileDirectory, @"TestData\05-lzw-single\In\sample.png"));
            WriteTwoPageTiff(Path.Combine(taskFileDirectory, @"TestData\06-lzw-decollate\In\two-page.tif"));

            Console.WriteLine("Test assets written next to task file:");
            Console.WriteLine(Path.Combine(taskFileDirectory, "TestData"));
        }

        private static void WriteSinglePagePng(string fullPath)
        {
            using (var image = new MagickImage(MagickColors.LightGray, 320, 200))
            {
                new Drawables()
                    .StrokeColor(MagickColors.Black)
                    .StrokeWidth(2)
                    .Line(20, 100, 300, 100)
                    .Draw(image);
                image.Write(fullPath, MagickFormat.Png);
            }
        }

        private static void WriteTwoPageTiff(string fullPath)
        {
            using (var pages = new MagickImageCollection())
            {
                var p1 = new MagickImage(MagickColors.White, 180, 120);
                new Drawables()
                    .FillColor(MagickColors.Black)
                    .Rectangle(40, 40, 140, 80)
                    .Draw(p1);
                pages.Add(p1);

                var p2 = new MagickImage(MagickColors.LightBlue, 180, 120);
                new Drawables()
                    .FillColor(MagickColors.DarkBlue)
                    .Circle(90, 60, 90, 100)
                    .Draw(p2);
                pages.Add(p2);

                pages.Write(fullPath, MagickFormat.Tiff);
            }
        }
    }
}
