using brainflow;
using System.Collections.ObjectModel;

namespace SimpleBCI.API
{
    public sealed class ImmutableBoardInformation
    {
        public int samplingRate { get; }
        public ReadOnlyDictionary<string, int> channels { get; }
        public int preset { get; }

        /// <summary>
        /// Creates an immutable board information object to obtaim important board information-
        /// </summary>
        /// <param name="id">Board ID</param>
        public ImmutableBoardInformation(int boardId, int preset)
        {
            this.preset = preset;
            this.samplingRate = BoardShim.get_sampling_rate(boardId, preset);

            int[] eegIndices = BoardShim.get_eeg_channels(boardId, preset);
            string[] eegNames = BoardShim.get_eeg_names(boardId, preset);

            if (eegIndices.Length != eegNames.Length)
            {
                throw new InvalidOperationException("BrainFlow returned mismatched EEG names and indices.");
            }

            var channels = new Dictionary<string, int>();
            for (int i = 0; i < eegIndices.Length; i++)
            {
                channels.Add(eegNames[i], eegIndices[i]);
            }

            this.channels = new ReadOnlyDictionary<string, int>(channels);
        }
    }
}