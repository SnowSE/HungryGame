namespace HungryGame
{
    [Serializable]
    internal class GameAlreadyStartedException : Exception
    {
        public GameAlreadyStartedException()
        {
        }

        public GameAlreadyStartedException(string? message) : base(message)
        {
        }

        public GameAlreadyStartedException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
