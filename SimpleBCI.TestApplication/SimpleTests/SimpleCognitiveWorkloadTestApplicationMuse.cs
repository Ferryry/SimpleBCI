using SimpleBCI.API;
using SimpleBCI.API.States;

namespace SimpleBCI.TestApplication.SimpleTests
{
    internal sealed class SimpleCognitiveWorkloadTestApplicationMuse : ISimpleTestApplication
    {
        public static string name = "Cognitive Workload Test (Muse S Athena)";
        private readonly BciClient instance;

        public SimpleCognitiveWorkloadTestApplicationMuse()
        {
            instance = new BciClient(
                new SimpleBCIBoardSelection(BoardType.MuseSAthenaBoard),
                new SimpleBCIParameters(null, null, "preset=p1041;low_latency=true"));
        }

        public void Run()
        {
            using CancellationTokenSource stop = new();
            try
            {
                if (!instance.Start()) return;

                Console.WriteLine("Keep still and relaxed for baseline calibration.");
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
                    Console.WriteLine($"Estimated workload: {state}");
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
