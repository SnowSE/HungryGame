namespace HungryGame
{
    [Serializable]
    public class DirectionNotRecognizedException : Exception
    {
        public DirectionNotRecognizedException()
        {
        }

        public DirectionNotRecognizedException(string? message) : base(message)
        {
        }

        public DirectionNotRecognizedException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
