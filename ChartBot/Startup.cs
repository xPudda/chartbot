using ChartBot.Brain;
using System;

namespace ChartBot
{
    public class Startup
    {
        private static void Main(string[] args)
        {
            try
            {
                Bot bot = new();

                while (Console.ReadLine().Equals("stop", StringComparison.InvariantCultureIgnoreCase))
                { }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                _ = Console.ReadLine();
            }
        }
    }
}
