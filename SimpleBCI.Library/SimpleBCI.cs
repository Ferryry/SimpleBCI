using brainflow;
using SimpleBCI.API;
using SimpleBCI.Library.API.Processing;
using SimpleBCI.Library.API.States;

namespace SimpleBCI
{
    public sealed class SimpleBCI
    {
        private readonly BoardShim board;
        private readonly MotorImageryClassifier motorImageryClassifier;
        private readonly BlinkDetector blinkDetector;
        private readonly FaaStateCalculator emotionDetector;

        /// <summary>
        /// Converts immutable board to actual brainflow id.
        /// </summary>
        /// <param name="board">Created immutabled board to be converted.</param>
        /// <returns>BoardShim instance.</returns>
        private BoardShim CreateBoard(ImmutableBoardSelection board)
        {
            BrainFlowInputParams inputParams = new BrainFlowInputParams();

            if (board.id == BoardIds.MuseAthena)
            {
                inputParams.other_info = "preset=p1041;low_latency=true";
                return new BoardShim(
                    (int)brainflow.BoardIds.MUSE_S_ATHENA_BOARD,
                    inputParams);
            }

            if (board.id == BoardIds.Synthetic)
            {
                return new BoardShim(
                    (int)brainflow.BoardIds.SYNTHETIC_BOARD,
                    inputParams);
            }

            throw new ArgumentOutOfRangeException(
                nameof(board),
                board.id,
                "The selected board is not supported.");
        }

        public SimpleBCI(ImmutableBoardSelection immutableBoard)
        {
            //BoardShim.enable_dev_board_logger();

            board = CreateBoard(immutableBoard);

            motorImageryClassifier = new MotorImageryClassifier(board);
            blinkDetector = new BlinkDetector(board);
            emotionDetector = new FaaStateCalculator(board);
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
        /// Returns and estimates the user's most likely emotional state based on EEG data.
        /// </summary>
        /// <returns>Estimated emotion state.</returns>
        public FaaState GetFaaState()
        {
            return emotionDetector.GetFaaState();
        }

        /// <summary>
        /// Returns the current blinkstate.
        /// <see cref="StartAPI"/> must be called before calling this method.
        /// </summary>
        /// <returns>Blinkstate.</returns>
        public BlinkState GetCurrentBlinkState()
        {
            return blinkDetector.GetCurrentBlinkState();
        }

        /// <summary>
        /// Returns the current brainstate classified and predicted by the local ML.
        /// <see cref="StartAPI"/> must be called before calling this method.
        /// </summary>
        /// <returns>Brainstate.</returns>
        public MotorImageryState GetMotorImageryState()
        {
            return motorImageryClassifier.GetMotorImageryState();
        }

        /// <summary>
        /// Starts the API.
        /// </summary>
        public bool StartAPI()
        {
            try
            {
                board.prepare_session();
                board.start_stream();
                emotionDetector.StartWorker();
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
            emotionDetector.StopWorker().GetAwaiter().GetResult();

            if (IsAPIRunning()) {
                board.stop_stream();
                board.release_session();
            }
        }
    }
}
