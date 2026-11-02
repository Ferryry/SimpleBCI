using SimpleBCI.API.States;

namespace SimpleBCI.TestApplication.SimpleTests
{
    internal sealed class SimpleFaaTestApplicationNeuroPawn : ISimpleTestApplication
    {
        public static string name = "FAA Test (NeuroPawn Knight | AF7/AF8)";

        private readonly BciClient instance = NeuroPawnTestConfiguration.CreateFrontalClient();

        public void Run()
        {
            using CancellationTokenSource stop = new();
            try
            {
                if (!instance.Start()) return;

                Console.WriteLine("FAA test running on AF7/AF8 (NeuroPawn inputs 7/8, gain 4).");
                Console.WriteLine("Press Q to quit.");

                Thread inputThread = new(() =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        if (Console.ReadKey(true).Key == ConsoleKey.Q)
                        {
                            stop.Cancel();
                            return;
                        }
                    }
                }) { IsBackground = true };
                inputThread.Start();

                while (!stop.IsCancellationRequested)
                {
                    FaaTendencyState state = instance.GetFaaTendency();
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] FAA: {state}");
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An exception occurred: {ex.Message}");
            }
            finally
            {
                instance.Stop();
            }
        }
    }
}
