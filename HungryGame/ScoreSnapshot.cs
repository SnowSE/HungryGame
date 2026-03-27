// HungryGame/ScoreSnapshot.cs
namespace HungryGame;

public record ScoreSnapshot(
    int PlayerId,
    string? PlayerName,
    int Score,
    double ElapsedSeconds,
    GameState Phase  // only Eating or Battle are ever recorded
);
