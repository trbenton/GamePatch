using System;
using PatchCore.Utility;

namespace GamePatchGenerator
{
    public class ConsoleLogger : ILogger
    {
        public void LogMessage(string message)
        {
            Console.WriteLine(message);
        }
    }
}
