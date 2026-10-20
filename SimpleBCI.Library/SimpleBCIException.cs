namespace SimpleBCI
{
    /// <summary>
    /// Represents an error reported by the SimpleBCI API without exposing
    /// implementation-specific exception types to callers.
    /// </summary>
    public sealed class SimpleBCIException : Exception
    {
        public SimpleBCIException(string message)
            : base(message)
        {
        }
    }
}
