namespace HungryGame;

public record MoveRequest(string Direction, string UserToken);
public record Cell(Location Location, bool IsPillAvailable, Player? OccupiedBy);
public class RedactedCell
{
    public RedactedCell(Cell c)
    {
        Location = c.Location;
        IsPillAvailable = c.IsPillAvailable;
        if (c.OccupiedBy != null)
        {
            OccupiedBy = new RedactedPlayer(c.OccupiedBy);
        }
    }
    public Location Location { get; }
    public bool IsPillAvailable { get; }
    public RedactedPlayer? OccupiedBy { get; }
}
public record Location(int Row, int Column);
public enum Direction { Up, Down, Left, Right, Undefined };

public class SharedStateClass
{
    public bool IsAdmin { get; set; }
    public string? AdminPassword { get; set; }
    public string? UserToken { get; set; }
    public bool IsCreator { get; set; }
    public bool CanManage => IsAdmin || IsCreator;
}

public record MoveResult(Location NewLocation, bool AteAPill);

public record CreateGameRequest(
    string Name,
    int NumRows,
    int NumCols,
    string CreatorToken,
    bool IsTimed,
    int? TimeLimitMinutes,
    string? AdminToken);

public record AuthRequest(string? CreatorToken, string? AdminToken);
public record BootRequest(int PlayerId, string? CreatorToken, string? AdminToken);
public record AdminLoginRequest(string Password);
public record AdminLogoutRequest(string AdminToken);