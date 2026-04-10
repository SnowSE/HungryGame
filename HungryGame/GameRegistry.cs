using System.Collections.Concurrent;

namespace HungryGame;

public class GameInstance
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string CreatorToken { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; set; }
    public GameLogic Game { get; init; } = null!;
}

public interface IGameIdStrategy
{
    string GenerateId(IEnumerable<string> existingIds);
}

public class ShortRandomIdStrategy : IGameIdStrategy
{
    // Visually unambiguous in Press Start 2P font: no 0, 1, 5, I, L, O, S
    private const string Charset = "ABCDEFGHJKMNPQRTUVWXYZ2346789";
    private const int GrowthThreshold = 100;
    private readonly Random _rng = new();

    public string GenerateId(IEnumerable<string> existingIds)
    {
        var existing = new HashSet<string>(existingIds);
        int length = 3;
        while (true)
        {
            long capacity = (long)Math.Pow(Charset.Length, length);
            long used = existing.Count(id => id.Length == length);
            if (capacity - used < GrowthThreshold)
            {
                length++;
                continue;
            }

            string id;
            do
            {
                id = new string(Enumerable.Range(0, length)
                    .Select(_ => Charset[_rng.Next(Charset.Length)])
                    .ToArray());
            } while (existing.Contains(id));

            return id;
        }
    }
}

public class GameRegistry
{
    private readonly ConcurrentDictionary<string, GameInstance> _games = new();
    private readonly IGameIdStrategy _idStrategy;
    private readonly ILogger<GameLogic> _gameLogger;
    private readonly IRandomService _random;

    public GameRegistry(IGameIdStrategy idStrategy, ILogger<GameLogic> gameLogger, IRandomService random)
    {
        _idStrategy = idStrategy;
        _gameLogger = gameLogger;
        _random = random;
    }

    public GameInstance CreateGame(string name, string creatorToken, int numRows, int numCols,
        bool isTimed = false, int? timeLimitMinutes = null)
    {
        var id = _idStrategy.GenerateId(_games.Keys);
        var game = new GameLogic(_gameLogger, _random);
        game.ConfigureGame(new NewGameInfo
        {
            NumRows = numRows,
            NumColumns = numCols,
            IsTimed = isTimed,
            TimeLimitInMinutes = timeLimitMinutes
        });
        var instance = new GameInstance
        {
            Id = id,
            Name = name,
            CreatorToken = creatorToken,
            CreatedAt = DateTime.UtcNow,
            Game = game,
        };
        _games[id] = instance;
        instance.Game.GameOver += (_, _) =>
        {
            instance.CompletedAt = DateTime.UtcNow;
        };
        return instance;
    }

    public GameInstance? GetGame(string id) =>
        _games.TryGetValue(id, out var instance) ? instance : null;

    public IEnumerable<GameInstance> AllGames() => _games.Values;

    public void RemoveGame(string id) => _games.TryRemove(id, out _);
}
