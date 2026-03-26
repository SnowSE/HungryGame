namespace HungryGame;

using Microsoft.AspNetCore.Diagnostics;

sealed class GameExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            PlayerNotFoundException =>
                (StatusCodes.Status404NotFound,
                 "Player not found",
                 "No player was found with that token. The game may have been reset since you joined."),

            InvalidMoveException =>
                (StatusCodes.Status409Conflict,
                 "Move not allowed",
                 "The player is not currently placed on the board. The game may not have started yet."),

            GameAlreadyStartedException =>
                (StatusCodes.Status409Conflict,
                 "Game already started",
                 "Cannot join because the game is already in progress."),

            NoAvailableSpaceException =>
                (StatusCodes.Status409Conflict,
                 "Board full",
                 "There are no available spaces on the board right now."),

            TooManyPlayersToStartGameException =>
                (StatusCodes.Status409Conflict,
                 "Too many players",
                 "Too many players have joined for the current board size. Try starting with a larger board."),

            DirectionNotRecognizedException =>
                (StatusCodes.Status400BadRequest,
                 "Invalid direction",
                 exception.Message),

            InvalidGameStateException =>
                (StatusCodes.Status409Conflict,
                 "Invalid game state",
                 exception.Message),

            ArgumentNullException { ParamName: "userName" } =>
                (StatusCodes.Status400BadRequest,
                 "Missing player name",
                 "Supply either 'userName' or 'playerName' as a query parameter."),

            _ => (0, null, null)
        };

        if (status == 0) return false;

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title, Detail = detail }
        });
    }
}
