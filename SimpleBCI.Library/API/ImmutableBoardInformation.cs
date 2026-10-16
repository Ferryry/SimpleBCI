using brainflow;
using System.Collections.ObjectModel;

namespace SimpleBCI.API
{
    /// <summary>
    /// Stores immutable board information used by SimpleBCI features.
    /// </summary>
    public sealed class ImmutableBoardInformation
    {
        public int SamplingRate { get; }
        public ReadOnlyDictionary<string, int> Channels { get; }
        public int Preset { get; }

        /// <summary>
        /// Creates an immutable snapshot of the board's sampling rate and EEG channels.
        /// </summary>
        /// <param name="boardId">BrainFlow ID of the selected board.</param>
        /// <param name="preset">BrainFlow preset used to query board information.</param>
        /// <param name="parameters">
        /// SimpleBCI configuration. If it specifies a channel layout, its names are
        /// mapped to the EEG data rows in their configured order; otherwise, names
        /// are obtained from BrainFlow.
        /// </param>
        public ImmutableBoardInformation(
            int boardId,
            int preset,
            SimpleBCIParameters parameters)
        {
            ArgumentNullException.ThrowIfNull(parameters);

            Preset = preset;
            SamplingRate = BoardShim.get_sampling_rate(boardId, preset);

            int[] eegIndices = BoardShim.get_eeg_channels(boardId, preset);
            string[] eegNames =
                parameters.ChannelLayout
                ?? BoardShim.get_eeg_names(boardId, preset);

            if (eegIndices.Length != eegNames.Length)
            {
                throw new ArgumentException(
                    $"The channel layout contains {eegNames.Length} names, " +
                    $"but the board exposes {eegIndices.Length} EEG inputs.",
                    nameof(parameters));
            }

            if (eegNames.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException(
                    "Channel names must not be null, empty, or whitespace.",
                    nameof(parameters));
            }

            if (eegNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != eegNames.Length)
            {
                throw new ArgumentException(
                    "Channel names in the layout must be unique.",
                    nameof(parameters));
            }

            var channels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < eegIndices.Length; i++)
            {
                channels.Add(eegNames[i], eegIndices[i]);
            }

            Channels = new ReadOnlyDictionary<string, int>(channels);
        }
    }
}
