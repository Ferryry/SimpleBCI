using brainflow;
using SimpleBCI.API;
using static SimpleBCI.API.EEGClassifier;

namespace SimpleBCI
{
    internal class SimpleBCI
    {
        private readonly BoardShim board;
        private readonly EEGClassifier classifier;

        public SimpleBCI()
        {
            BoardShim.enable_dev_board_logger();

            BrainFlowInputParams parameters = new BrainFlowInputParams();

            board = new BoardShim((int)BoardIds.SYNTHETIC_BOARD, parameters);

            classifier = EEGClassifier.GetSingletonInstance(board);
        }

        /// <summary>
        /// Returns the current brainstate classified and predicted by the local ML.
        /// </summary>
        /// <returns>Brainstate.</returns>
        public BrainState GetCurrentBrainState()
        {
            return classifier.GetCurrentBrainState();
        }


        public void StartApplication()
        {
            Console.WriteLine("Starting Application...");

            try
            {
                board.prepare_session();
                board.start_stream();

                Task.Delay(500);

            }
            catch (Exception e)
            {
                Console.WriteLine($"{e.Message}");

                board.stop_stream();
                board.release_session();
            }
        }

        public void StopApplication()
        {
            board.stop_stream();
            board.release_session();
        }
    }
}