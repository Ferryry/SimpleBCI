using SimpleBCI.API;

namespace SimpleBCI
{
    /// <summary>
    /// Supplies board-specific startup configuration without exposing BrainFlow.
    /// </summary>
    public interface IBoardConfiguration
    {
        BoardType BoardType { get; }
        void Apply(IBoardConfigurationContext context);
    }

    /// <summary>
    /// Sends a board command and optionally waits during board configuration.
    /// </summary>
    public interface IBoardConfigurationContext
    {
        void SendCommand(string command);
        void Wait(TimeSpan duration);
    }
}
