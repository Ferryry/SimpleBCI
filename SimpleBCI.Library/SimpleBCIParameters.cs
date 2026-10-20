namespace SimpleBCI
{
    public sealed class SimpleBCIParameters
    {
        private readonly ChannelInput[]? channelLayout;

        public string? SerialPort { get; }
        public string? SerialNumber { get; }
        public string? AdditionalParams { get; }
        public IBoardConfiguration? BoardConfiguration { get; }
        public ChannelInput[]? ChannelLayout =>
            channelLayout is null ? null : (ChannelInput[])channelLayout.Clone();

        public SimpleBCIParameters() : this((string?)null) { }

        public SimpleBCIParameters(string? serialPort)
            : this(serialPort, null) { }

        public SimpleBCIParameters(string? serialPort, string? serialNumber)
            : this(serialPort, serialNumber, null) { }

        public SimpleBCIParameters(
            string? serialPort,
            string? serialNumber,
            string? additionalParams)
            : this(serialPort, serialNumber, additionalParams, (ChannelInput[]?)null) { }

        public SimpleBCIParameters(
            string? serialPort,
            string? serialNumber,
            string? additionalParams,
            ChannelInput[]? channelLayout)
            : this(serialPort, serialNumber, additionalParams, channelLayout, null)
        {
        }

        public SimpleBCIParameters(
            string? serialPort,
            string? serialNumber,
            string? additionalParams,
            ChannelInput[]? channelLayout,
            IBoardConfiguration? boardConfiguration)
        {
            SerialPort = serialPort;
            SerialNumber = serialNumber;
            AdditionalParams = additionalParams;
            BoardConfiguration = boardConfiguration;
            this.channelLayout = channelLayout is null
                ? null
                : (ChannelInput[])channelLayout.Clone();
        }
    }
}
