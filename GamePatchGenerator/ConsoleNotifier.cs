using System;
using PatchCore.Utility;

namespace GamePatchGenerator
{
    public class ConsoleNotifier : INotifier
    {
        public void ShowNotification(string message)
        {
            Console.WriteLine(message);
        }
    }
}
