using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ParallelizeTests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var rootFolder = args.Length > 0 ? args[0] : Environment.CurrentDirectory;

            var files = Directory.EnumerateFiles(Path.Combine(rootFolder, "tests"), "*.csproj", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path) is not "ArgumentsPrinter.csproj" and not "TestUtilities.csproj")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var newContent = new XDocument();
            var itemGroupElement = new XElement("ItemGroup");
            newContent.Add(new XElement("Project", itemGroupElement));

            var itemsPerChunk = (int)Math.Ceiling(files.Length / 5d);  // Keep in sync with the CI script
            for (var i = 0; i < files.Length; i++)
            {
                var bucketStr = ((i / itemsPerChunk) + 1).ToString(CultureInfo.InvariantCulture);
                itemGroupElement.Add(new XElement("ProjectReference",
                    new XAttribute("Include", Path.GetRelativePath(Path.Combine(rootFolder, "eng"), files[i])),
                    new XAttribute("Condition", $"$(TEST_BUCKET) == '{bucketStr}'")));
            }

            var str = newContent.ToString(SaveOptions.None);

            var path = Path.Combine(rootFolder, "eng", "build-test-parallel.props");
            if (str != File.ReadAllText(path))
            {
                File.WriteAllText(path, str);
                return 1;
            }

            return 0;
        }
    }
}
