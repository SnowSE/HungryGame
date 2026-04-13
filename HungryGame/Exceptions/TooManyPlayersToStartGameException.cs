namespace HungryGame
{
    [Serializable]
    internal class TooManyPlayersToStartGameException : Exception
    {
        public TooManyPlayersToStartGameException()
        {
        }

        public TooManyPlayersToStartGameException(string? message) : base(message)
        {
        }

        public TooManyPlayersToStartGameException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
