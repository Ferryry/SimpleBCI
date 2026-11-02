using SimpleBCI.API.States;

namespace SimpleBCI.TestApplication.SimpleTests
{
    internal sealed class SimpleCognitiveWorkloadTestApplicationNeuroPawn : ISimpleTestApplication
    {
        public static string name = "Cognitive Workload Test (NeuroPawn Knight | AF7/AF8)";

        private readonly BciClient instance = NeuroPawnTestConfiguration.CreateFrontalClient();

        public void Run()
        {
            using CancellationTokenSource stop = new();
            try
            {
                if (!instance.Start()) return;

                Console.WriteLine("Keep still and relaxed for baseline calibration.");
                Console.WriteLine("Using AF7/AF8 (NeuroPawn inputs 7/8, gain 4).");
                Thread.Sleep(TimeSpan.FromSeconds(4));
                for (int i = 0; i < 10; i++)
                {
                    instance.AddCognitiveWorkloadBaselineSample();
                    Console.WriteLine($"Baseline window {i + 1}/10 recorded.");
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                }

                instance.FinishCognitiveWorkloadCalibration();
                Console.WriteLine("Calibration complete. Press Q to quit.");

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
                    CognitiveWorkloadState state = instance.GetCognitiveWorkloadState();
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Estimated workload: {state}");
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
