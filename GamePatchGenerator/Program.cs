using System;
using System.Collections.Generic;
using System.Linq;
using PatchCore.Files;
using PatchCore.Storage;

namespace GamePatchGenerator
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var newBinaryPath = GetArgument(args, "-i");
            Console.WriteLine("Beginning patch generation.");
            try
            {
                var generator = new PatchGenerator(newBinaryPath, new S3Storage(), new ConsoleLogger(), new ConsoleProgressTracker(), new ConsoleNotifier());
                generator.Generate();
                Console.WriteLine("Patch is done generating.");
            }
            catch (Exception e)
            {
                Console.WriteLine("Patch generation failed.");
                Console.WriteLine(e);
                Console.WriteLine(e.StackTrace);
            }
            Console.ReadKey();
        }

        private static string GetArgument(IEnumerable<string> args, string option)
        {
            return args.SkipWhile(i => i != option).Skip(1).Take(1).FirstOrDefault();
        }
    }
}
