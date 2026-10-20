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
        public ReadOnlyDictionary<ElectrodePosition, int> Channels { get; }
        public int Preset { get; }

        /// <summary>
        /// Creates an immutable snapshot of the board's sampling rate and EEG channels.
        /// </summary>
        /// <param name="boardId">BrainFlow ID of the selected board.</param>
        /// <param name="preset">BrainFlow preset used to query board information.</param>
        /// <param name="parameters">
        /// SimpleBCI configuration. If it specifies a channel layout, each entry
        /// maps a board EEG input index to an optional electrode name; otherwise,
        /// names are obtained from BrainFlow.
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
            ChannelInput[]? configuredLayout = parameters.ChannelLayout;
            if (configuredLayout is null)
            {
                string[] eegNames = BoardShim.get_eeg_names(boardId, preset);
                if (eegIndices.Length != eegNames.Length)
                {
                    throw new InvalidOperationException(
                        $"BrainFlow returned {eegNames.Length} EEG names, " +
                        $"but the board exposes {eegIndices.Length} EEG inputs.");
                }

                configuredLayout = eegNames
                    .Select((name, index) => Enum.TryParse(name, true, out ElectrodePosition position)
                        ? new ChannelInput(index, position)
                        : new ChannelInput(index, ElectrodePosition.Unspecific))
                    .ToArray();
            }

            var channels = new Dictionary<ElectrodePosition, int>();
            var configuredIndices = new HashSet<int>();
            foreach (ChannelInput input in configuredLayout)
            {
                if (input is null)
                {
                    throw new ArgumentException("Channel input entries must not be null.", nameof(parameters));
                }

                if (input.BoardIndex < 0 || input.BoardIndex >= eegIndices.Length)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(parameters),
                        $"Board EEG input index {input.BoardIndex} is outside the valid range 0..{eegIndices.Length - 1}.");
                }

                if (!configuredIndices.Add(input.BoardIndex))
                {
                    throw new ArgumentException(
                        $"Board EEG input index {input.BoardIndex} is configured more than once.",
                        nameof(parameters));
                }

                ElectrodePosition position = input.Position;
                if (!Enum.IsDefined(position))
                {
                    throw new ArgumentException(
                        $"Electrode position value '{position}' is not defined.",
                        nameof(parameters));
                }

                if (position == ElectrodePosition.Unspecific)
                {
                    continue;
                }

                if (!channels.TryAdd(position, eegIndices[input.BoardIndex]))
                {
                    throw new ArgumentException(
                        $"Electrode position '{position}' is configured more than once.",
                        nameof(parameters));
                }
            }

            Channels = new ReadOnlyDictionary<ElectrodePosition, int>(channels);
        }
    }
}
