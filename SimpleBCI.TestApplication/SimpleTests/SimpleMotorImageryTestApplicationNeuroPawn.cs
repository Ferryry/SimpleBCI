using SimpleBCI.API;
using SimpleBCI.API.States;

namespace SimpleBCI.TestApplication.SimpleTests
{
    internal class SimpleMotorImageryTestApplicationNeuroPawn : ISimpleTestApplication
    {
        public static string name = "Motor Imagery Test (NeuroPawn | 8 motor electrodes)";
        private const int CalibrationTrials = 10;
        private readonly BciClient instance;
        private volatile bool quit;

        public SimpleMotorImageryTestApplicationNeuroPawn()
        {
            string serial = "COM4";
            ChannelInput[] channels =
            {
                new(0, ElectrodePosition.Fc3), // NeuroPawn input 1
                new(1, ElectrodePosition.Fc4), // NeuroPawn input 2
                new(2, ElectrodePosition.C5),  // NeuroPawn input 3
                new(3, ElectrodePosition.C3),  // NeuroPawn input 4
                new(4, ElectrodePosition.C4),  // NeuroPawn input 5
                new(5, ElectrodePosition.C6),  // NeuroPawn input 6
                new(6, ElectrodePosition.Cp3), // NeuroPawn input 7
                new(7, ElectrodePosition.Cp4), // NeuroPawn input 8
            };

            instance = new BciClient(
                new SimpleBCIBoardSelection(BoardType.NeuropawnKnightBoard),
                new SimpleBCIParameters(
                    serial,
                    null,
                    null,
                    channels,
                    new NeuroPawnKnightConfiguration(
                        Enumerable.Range(1, 8).Select(ch => new NeuroPawnChannelConfiguration(ch, Gain: 1)))));
        }

        /// <summary>One trial: get ready (1 s), imagine (4 s), then the caller reads the result.</summary>
        private static void RunCue(string text)
        {
            Console.WriteLine("Get ready... keep your hands still and look at the screen.");
            Thread.Sleep(1000);
            Console.WriteLine(text);
            Thread.Sleep(4000); // the model looks at 0.5-4.0 s after this cue
        }

        public void Run()
        {
            try
            {
                instance.Start();
                Console.WriteLine("Starting... (6 s)");
                Thread.Sleep(6000); // the board needs 5.5 s of data before the first trial

                Console.WriteLine($"\n=== Calibration: {CalibrationTrials} trials ===");
                for (int i = 0; i < CalibrationTrials; i++)
                {
                    string hand = i % 2 == 0 ? "LEFT" : "RIGHT";
                    RunCue($"[{i + 1}/{CalibrationTrials}] Imagine squeezing your {hand} hand, again and again.");
                    instance.AddMotorImageryCalibrationTrial();
                    Console.WriteLine("Rest.\n");
                    Thread.Sleep(2500);
                }

                instance.FinishMotorImageryCalibration();
                Console.WriteLine("Calibration done. Now choose LEFT or RIGHT yourself in every trial. Press Q to quit.\n");

                var input = new Thread(() =>
                {
                    while (!quit)
                    {
                        if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
                        {
                            quit = true;
                        }

                        Thread.Sleep(10);
                    }
                });
                input.Start();

                while (!quit)
                {
                    RunCue("Imagine your LEFT or your RIGHT hand now.");
                    MotorImageryState state = instance.GetMotorImageryState();
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] Prediction: {state}\n");
                    Thread.Sleep(2500);
                }

                input.Join();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An exception occurred: {ex.Message}");
            }

            instance.Stop();
        }
    }
}
