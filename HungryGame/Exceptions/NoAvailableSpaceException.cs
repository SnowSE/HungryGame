namespace HungryGame
{
    [Serializable]
    public class NoAvailableSpaceException : Exception
    {
        public NoAvailableSpaceException()
        {
        }

        public NoAvailableSpaceException(string? message) : base(message)
        {
        }

        public NoAvailableSpaceException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
