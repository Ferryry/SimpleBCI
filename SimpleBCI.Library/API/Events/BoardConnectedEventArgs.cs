using SimpleBCI.API;

namespace SimpleBCI.Library.API.Events
{
    public sealed class BoardConnectedEventArgs : EventArgs
    {
        public ImmutableBoardInformation BoardInfo { get; }

        public BoardConnectedEventArgs(ImmutableBoardInformation boardInfo)
        {
            BoardInfo = boardInfo;
        }
    }
}
