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
    public string CellIcon { get; set; } = "🌯";
    public DateTime? GameEndsOn { get; set; }
    public bool IsAdmin { get; set; }
    public string? AdminPassword { get; set; }
}

public record MoveResult(Location NewLocation, bool AteAPill);