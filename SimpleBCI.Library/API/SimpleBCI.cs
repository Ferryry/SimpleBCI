using brainflow;
using SimpleBCI.API;

namespace SimpleBCI
{
    public sealed class SimpleBCI
    {
        private readonly BoardShim board;
        private readonly Classifier classifier;

        /// private bool enableRestAPI; // Needs to be implemented!
        /// private bool enableMQTT;    // Needs to be implemented!

        /// <summary>
        /// Converts immutable board to actual brainflow id.
        /// </summary>
        /// <param name="board">Created immutabled board to be converted.</param>
        /// <returns>BoardShim instance.</returns>
        private BoardShim CreateBoard(ImmutableBoardSelection board)
        {
            BrainFlowInputParams inputParams = new BrainFlowInputParams();

            if (board.id.Equals(BoardIds.MuseAthena))
            {
                inputParams.other_info = "preset=p1041;low_latency=true";

                return new BoardShim((int)BoardIds.Synthetic, inputParams);
            }

            return new BoardShim((int)BoardIds.Synthetic, inputParams);
        }

        public SimpleBCI(ImmutableBoardSelection immutableBoard)
        {
            BoardShim.enable_dev_board_logger();

            board = CreateBoard(immutableBoard);

            classifier = new Classifier(board);
        }

        /// <summary>
        /// Checks current API status.
        /// </summary>
        /// <returns>Status</returns>
        public bool IsAPIRunning()
        {
            return board.is_prepared();
        }

        /// <summary>
        /// Returns the current brainstate classified and predicted by the local ML.
        /// <see cref="StartAPI"/> must be called before calling this method.
        /// </summary>
        /// <returns>Brainstate.</returns>
        public BrainState GetCurrentBrainState()
        {
            return classifier.GetCurrentBrainState();
        }

        /// <summary>
        /// Starts the API.
        /// </summary>
        public bool StartAPI()
        {
            Console.WriteLine("Starting Application...");

            try
            {
                board.prepare_session();
                board.start_stream();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Stops the API.
        /// </summary>
        public void StopAPI()
        {
            board.stop_stream();
            board.release_session();
        }
    }
}