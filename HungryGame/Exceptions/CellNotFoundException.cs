namespace HungryGame
{
    [Serializable]
    internal class CellNotFoundException : Exception
    {
        public CellNotFoundException()
        {
        }

        public CellNotFoundException(string? message) : base(message)
        {
        }

        public CellNotFoundException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
