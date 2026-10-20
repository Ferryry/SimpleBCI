namespace SimpleBCI
{
    /// <summary>
    /// Associates a board EEG input with an optional electrode position name.
    /// </summary>
    /// <param name="BoardIndex">Zero-based position in BrainFlow's EEG input list.</param>
    /// <param name="Position">Electrode position connected to this input, or Unspecific when unused.</param>
    public sealed record ChannelInput(int BoardIndex, ElectrodePosition Position);
}
