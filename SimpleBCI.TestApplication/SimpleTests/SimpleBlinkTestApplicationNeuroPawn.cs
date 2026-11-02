using SimpleBCI.API;
using SimpleBCI.API.States;
using System.Runtime.Serialization;

namespace SimpleBCI.TestApplication.SimpleTests
{
    internal class SimpleBlinkTestApplicationNeuroPawn : ISimpleTestApplication
    {
        public static string name = "Blink Test (NeuroPawn | {F7, F8})";
        private readonly BciClient instance;
        private CancellationTokenSource cancellationTokenSource;
        private DisplayDelay displayDelay;

        private enum DisplayDelay
        {
            HalfSecond = 0,
            OneSecond = 1,
            TwoSeconds = 2,
        }

        /// <summary>
        /// Formats the current Brainstate info and prints it.
        /// </summary>
        /// <param name="state">Current brainstate obtained by the SimpleBCI API.</param>
        private void PrintCurrentState(BlinkState state)
        {
            Console.WriteLine(String.Format(
                "{0}[{1}] Blink Prediction: {2}\n",
                "############ Prediction Update ############\n",
                DateTime.UtcNow,
                state.ToString()
            ));
        }

        /// <summary>
        /// Simple method for changing display data output.
        /// </summary>
        /// <param name="direction">Change between options: -1 or 1.</param>
        private void ChangeDisplayDelay(int direction)
        {
            displayDelay = (DisplayDelay)(
                ((int)displayDelay + direction + 3) % 3
            );
        }

        /// <summary>
        /// Receives keyboard input and performs actions.
        /// </summary>
        private void BackgroundInputThread()
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    ConsoleKey key = Console.ReadKey(true).Key;

                    switch (key)
                    {
                        case ConsoleKey.LeftArrow:
                        case ConsoleKey.A:
                            ChangeDisplayDelay(-1);
                            break;

                        case ConsoleKey.RightArrow:
                        case ConsoleKey.D:
                            ChangeDisplayDelay(1);
                            break;

                        case ConsoleKey.Q:
                            cancellationTokenSource.Cancel();
                            break;
                    }
                }

                Thread.Sleep(10);
            }
        }

        /// <summary>
        /// Runs test application.
        /// </summary>
        public void Run()
        {
            try
            {
                if (!instance.Start())
                {
                    return;
                }

                Thread.Sleep(1000);

                Thread thread = new Thread(BackgroundInputThread);
                thread.Start();

                while (!cancellationTokenSource.IsCancellationRequested)
                {
                    PrintCurrentState(instance.GetCurrentBlinkState());

                    switch (displayDelay) {
                        case DisplayDelay.HalfSecond:
                            Task.Delay(
                                TimeSpan.FromMilliseconds(500),
                                cancellationTokenSource.Token
                            ).Wait();
                            break;

                        case DisplayDelay.OneSecond:
                            Task.Delay(
                                TimeSpan.FromMilliseconds(1000),
                                cancellationTokenSource.Token
                            ).Wait();
                            break;

                        case DisplayDelay.TwoSeconds:
                            Task.Delay(
                                TimeSpan.FromMilliseconds(2000),
                                cancellationTokenSource.Token
                            ).Wait();
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An exception occurred: {ex.Message}");
            }

            instance.Stop();
        }

        public SimpleBlinkTestApplicationNeuroPawn()
        {
            string serial = "COM4";
            ChannelInput[] channels =
            {
                new(0, ElectrodePosition.Af7),
                new(1, ElectrodePosition.Af8),
                new(2, ElectrodePosition.Unspecific),
                new(3, ElectrodePosition.Unspecific),
                new(4, ElectrodePosition.Unspecific),
                new(5, ElectrodePosition.Unspecific),
                new(6, ElectrodePosition.Unspecific),
                new(7, ElectrodePosition.Unspecific),
            };
            
            instance = new BciClient(
                new SimpleBCIBoardSelection(BoardType.NeuropawnKnightBoard),
                new SimpleBCIParameters(
                    serial,
                    null,
                    null,
                    channels,
                    new NeuroPawnKnightConfiguration(
                    [
                        new(1, Gain: 12),
                        new(2, Gain: 12),
                    ])));

            cancellationTokenSource = new CancellationTokenSource();
        }
    }
}
