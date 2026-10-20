namespace SimpleBCI
{
    /// <summary>
    /// Configuration for one NeuroPawn Knight hardware channel.
    /// </summary>
    public sealed record NeuroPawnChannelConfiguration(int ChannelNumber, int Gain = 12);
}
