namespace SimpleBCI
{
    public sealed class SimpleBCIParameters
    {
        private readonly string[]? channelLayout;

        public string? SerialPort { get; }
        public string? SerialNumber { get; }
        public string? AdditionalParams { get; }
        public string[]? ChannelLayout =>
            channelLayout is null ? null : (string[])channelLayout.Clone();

        public SimpleBCIParameters() : this(null) { }

        public SimpleBCIParameters(string? serialPort)
            : this(serialPort, null) { }

        public SimpleBCIParameters(string? serialPort, string? serialNumber)
            : this(serialPort, serialNumber, null) { }

        public SimpleBCIParameters(
            string? serialPort,
            string? serialNumber,
            string? additionalParams)
            : this(serialPort, serialNumber, additionalParams, null) { }

        public SimpleBCIParameters(
            string? serialPort,
            string? serialNumber,
            string? additionalParams,
            string[]? channelLayout)
        {
            SerialPort = serialPort;
            SerialNumber = serialNumber;
            AdditionalParams = additionalParams;
            this.channelLayout = channelLayout is null
                ? null
                : (string[])channelLayout.Clone();
        }
    }
}