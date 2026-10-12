using SimpleBCI.API;

namespace SimpleBCI.TestApplication
{
    internal class SimpleTestApplication
    {
        private readonly SimpleBCI instance;
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
        private void PrintCurrentState(MotorImageryState state)
        {
            Console.WriteLine(String.Format(
                "{0}[{1}] Prediction: {2}\n",
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
                instance.StartAPI();

                Thread thread = new Thread(BackgroundInputThread);
                thread.Start();

                while (!cancellationTokenSource.IsCancellationRequested)
                {
                    PrintCurrentState(instance.GetMotorImageryState());

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
                Console.WriteLine(
                    String.Format("An exception occurred!\n\n{0}",
                    ex.ToString())
                );
            }

            instance.StopAPI();
        }

        public SimpleTestApplication()
        {
            instance = new SimpleBCI(new ImmutableBoardSelection(BoardIds.MuseAthena));
            cancellationTokenSource = new CancellationTokenSource();
        }
    }
}
