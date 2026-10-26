using SimpleBCI.API;


namespace SimpleBCI.Library.API.Events
{
    public sealed class ApiReadyEventArgs : EventArgs
    {
        public DateTime ReadyAt { get; } = DateTime.UtcNow;
    }
}
