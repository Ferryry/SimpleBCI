using SimpleBCI.API;

namespace SimpleBCI
{
    /// <summary>
    /// Enables selected NeuroPawn Knight channels after streaming starts.
    /// Channel numbers are one-based hardware numbers (1 through 8).
    /// </summary>
    public sealed class NeuroPawnKnightConfiguration : IBoardConfiguration
    {
        private static readonly int[] SupportedGains = [1, 2, 3, 4, 6, 8, 12];
        private readonly NeuroPawnChannelConfiguration[] channels;

        public BoardType BoardType => BoardType.NeuropawnKnightBoard;

        public IReadOnlyList<NeuroPawnChannelConfiguration> Channels =>
            Array.AsReadOnly((NeuroPawnChannelConfiguration[])channels.Clone());

        public TimeSpan StabilizationDelay { get; }
        public TimeSpan CommandDelay { get; }

        public NeuroPawnKnightConfiguration(
            IEnumerable<NeuroPawnChannelConfiguration> channels,
            TimeSpan? stabilizationDelay = null,
            TimeSpan? commandDelay = null)
        {
            ArgumentNullException.ThrowIfNull(channels);
            this.channels = channels.ToArray();
            if (this.channels.Length == 0)
            {
                throw new ArgumentException("At least one NeuroPawn channel must be configured.", nameof(channels));
            }

            var configuredChannels = new HashSet<int>();
            foreach (NeuroPawnChannelConfiguration channel in this.channels)
            {
                if (channel is null)
                {
                    throw new ArgumentException("Channel configuration entries must not be null.", nameof(channels));
                }

                if (channel.ChannelNumber is < 1 or > 8)
                {
                    throw new ArgumentOutOfRangeException(nameof(channels), "NeuroPawn channel numbers must be in the range 1 through 8.");
                }

                if (!configuredChannels.Add(channel.ChannelNumber))
                {
                    throw new ArgumentException($"NeuroPawn channel {channel.ChannelNumber} is configured more than once.", nameof(channels));
                }

                if (!SupportedGains.Contains(channel.Gain))
                {
                    throw new ArgumentOutOfRangeException(nameof(channels), $"Gain {channel.Gain} is not supported by the NeuroPawn Knight board.");
                }
            }

            StabilizationDelay = ValidateDelay(stabilizationDelay ?? TimeSpan.FromSeconds(2), nameof(stabilizationDelay));
            CommandDelay = ValidateDelay(commandDelay ?? TimeSpan.FromMilliseconds(500), nameof(commandDelay));
        }

        public void Apply(IBoardConfigurationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Wait(StabilizationDelay);

            foreach (NeuroPawnChannelConfiguration channel in channels)
            {
                context.SendCommand($"chon_{channel.ChannelNumber}_{channel.Gain}");
                context.Wait(CommandDelay);
            }
        }

        private static TimeSpan ValidateDelay(TimeSpan delay, string parameterName)
        {
            if (delay < TimeSpan.Zero || delay > TimeSpan.FromMinutes(1))
            {
                throw new ArgumentOutOfRangeException(parameterName, "Configuration delays must be between zero and one minute.");
            }

            return delay;
        }
    }
}
