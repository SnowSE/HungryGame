namespace HungryGame;

public enum GameState
{
    Joining = 0,
    Eating = 1,
    Battle = 2,
    GameOver = 3
}

public interface IRandomService
{
    int Next(int maxValue);
}

public sealed class SystemRandomService : IRandomService
{
    public int Next(int maxValue) => Random.Shared.Next(maxValue);
}

public class GameLogic
{
    private readonly object stateLock = new();
    private readonly List<Player> players = new();
    private readonly Dictionary<string, Player> playersByToken = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Location> playerLocations = new(StringComparer.Ordinal);
    private readonly Dictionary<Location, Cell> cells = new();
    private readonly Queue<int> pillValues = new();
    private readonly Dictionary<Location, int> specialPointValues = new();
    private readonly HashSet<int> playersThatMovedThisGame = new();
    private readonly List<Location> emptyCells = new();
    private readonly Dictionary<Location, int> emptyCellIndexes = new();
    private readonly List<ScoreSnapshot> scoreHistory = new();
    private readonly Dictionary<int, double> playerEliminationTimes = new();
    private readonly ILogger<GameLogic> log;
    private readonly IRandomService random;
    private readonly TimeProvider timeProvider;

    private int remainingPills;
    private int activePlayersCount;
    private int number;
    private long gameStateValue;
    private long timerGeneration;
    private DateTimeOffset gameStartedAt;
    private double? battleStartedAt;
    private ITimer? gameTimer;

    public int MaxRows { get; private set; }
    public int MaxCols { get; private set; }
    public NewGameInfo? LastGameInfo { get; private set; }
    public event EventHandler? GameStateChanged;
    public event EventHandler? GameOver;

    public GameLogic(ILogger<GameLogic> log, IRandomService random, TimeProvider timeProvider)
    {
        this.log = log;
        this.random = random;
        this.timeProvider = timeProvider;
        gameStartedAt = timeProvider.GetUtcNow();
    }

    public DateTimeOffset lastStateChange;
    public TimeSpan stateChangeFrequency;

    public bool IsGameStarted => Interlocked.Read(ref gameStateValue) != 0;
    public GameState CurrentGameState => (GameState)Interlocked.Read(ref gameStateValue);
    public bool IsGameOver => Interlocked.Read(ref gameStateValue) == (long)GameState.GameOver;
    public DateTimeOffset? GameEndsOn { get; private set; }
    public TimeSpan? TimeLimit { get; private set; }
    public TimeSpan? TimeRemaining => GameEndsOn.HasValue ? GameEndsOn.Value - timeProvider.GetUtcNow() : null;

    public int PlayerCount
    {
        get
        {
            lock (stateLock)
            {
                return players.Count;
            }
        }
    }

    public IReadOnlyList<ScoreSnapshot> ScoreHistory
    {
        get
        {
            lock (stateLock)
            {
                return scoreHistory.ToList();
            }
        }
    }

    public double? BattleStartedAt
    {
        get
        {
            lock (stateLock)
            {
                return battleStartedAt;
            }
        }
    }

    public IReadOnlyDictionary<int, double> PlayerEliminationTimes
    {
        get
        {
            lock (stateLock)
            {
                return new Dictionary<int, double>(playerEliminationTimes);
            }
        }
    }

    public void ConfigureGame(NewGameInfo gameInfo)
    {
        if (gameInfo.NumRows < 1)
            throw new ArgumentOutOfRangeException(nameof(gameInfo.NumRows), "Number of rows must be at least 1.");
        if (gameInfo.NumColumns < 1)
            throw new ArgumentOutOfRangeException(nameof(gameInfo.NumColumns), "Number of columns must be at least 1.");
        if (gameInfo.IsTimed && (!gameInfo.TimeLimitInMinutes.HasValue || gameInfo.TimeLimitInMinutes.Value < 1))
            throw new ArgumentOutOfRangeException(nameof(gameInfo.TimeLimitInMinutes), "Timed games require a positive time limit.");

        MaxRows = gameInfo.NumRows;
        MaxCols = gameInfo.NumColumns;
        LastGameInfo = gameInfo;

        if (gameInfo.IsTimed && gameInfo.TimeLimitInMinutes.HasValue)
        {
            TimeLimit = TimeSpan.FromMinutes(gameInfo.TimeLimitInMinutes.Value);
            GameEndsOn = timeProvider.GetUtcNow() + TimeLimit.Value;
        }
        else
        {
            TimeLimit = null;
            GameEndsOn = null;
        }
    }

    public void StartGame()
    {
        if (Interlocked.Read(ref gameStateValue) != 0 || MaxRows == 0)
            return;

        lock (stateLock)
        {
            scoreHistory.Clear();
            battleStartedAt = null;
            playerEliminationTimes.Clear();
        }

        initializeGame();

        if (LastGameInfo?.IsTimed == true && TimeLimit.HasValue)
        {
            ScheduleGameTimer(Interlocked.Increment(ref timerGeneration));
        }
    }

    public void ResetGame()
    {
        if (Interlocked.Read(ref gameStateValue) == 0)
            return;

        Interlocked.Increment(ref timerGeneration);
        resetGameCore(disposeTimer: true);
    }

    public void BootPlayer(int playerId)
    {
        lock (stateLock)
        {
            var player = players.FirstOrDefault(p => p.Id == playerId);
            if (player == null)
                return;

            if (player.Token != null && playerLocations.TryGetValue(player.Token, out var location))
            {
                playerLocations.Remove(player.Token);
                var cell = cells[location];
                cells[location] = cell with { OccupiedBy = null, IsPillAvailable = true };
                AddEmptyCell(location);
                remainingPills++;
                activePlayersCount--;
            }

            RemovePlayerFromRoster(player);
            log.LogInformation("Booted player {playerName} (ID: {playerId})", player.Name, playerId);
        }

        raiseStateChange();
    }

    public void ClearAllPlayers()
    {
        lock (stateLock)
        {
            foreach (var player in players)
            {
                if (player.Token != null && playerLocations.TryGetValue(player.Token, out var location))
                {
                    var cell = cells[location];
                    cells[location] = cell with { OccupiedBy = null, IsPillAvailable = true };
                    AddEmptyCell(location);
                    remainingPills++;
                }
            }

            players.Clear();
            playersByToken.Clear();
            playerLocations.Clear();
            playersThatMovedThisGame.Clear();
            activePlayersCount = 0;
            log.LogInformation("Cleared all players");
        }

        raiseStateChange();
    }

    public Cell GetCell(int row, int col)
    {
        lock (stateLock)
        {
            return cells[new Location(row, col)];
        }
    }

    public string JoinPlayer(string playerName)
    {
        var token = Guid.NewGuid().ToString();
        log.LogInformation("{playerName} wants to join", playerName);

        lock (stateLock)
        {
            var id = Interlocked.Increment(ref number);
            var joinedPlayer = new Player { Id = id, Name = playerName, Token = token };

            players.Add(joinedPlayer);
            playersByToken[token] = joinedPlayer;
            markPlayerAsActive(joinedPlayer);

            if (gameAlreadyInProgress)
            {
                if (emptyCells.Count == 0)
                    throw new NoAvailableSpaceException("there is no available space");

                var newLocation = emptyCells[random.Next(emptyCells.Count)];
                var origCell = cells[newLocation];
                cells[newLocation] = origCell with { OccupiedBy = joinedPlayer, IsPillAvailable = false };
                playerLocations[token] = newLocation;
                RemoveEmptyCell(newLocation);

                if (origCell.IsPillAvailable)
                    remainingPills--;

                activePlayersCount++;
            }
        }

        raiseStateChange();
        return token;
    }

    public IReadOnlyList<RedactedPlayer> GetPlayersByScoreDescending()
    {
        lock (stateLock)
        {
            return players
                .OrderByDescending(p => p.Score)
                .ThenBy(p => p.Id)
                .Select(p => new RedactedPlayer(p))
                .ToList();
        }
    }

    public IReadOnlyList<RedactedPlayer> GetPlayersByGameOverRank()
    {
        lock (stateLock)
        {
            return players
                .OrderByDescending(p => p.Score > 0 ? 1 : 0)
                .ThenByDescending(p => p.Score)
                .ThenByDescending(p => playerEliminationTimes.TryGetValue(p.Id, out var eliminatedAt) ? eliminatedAt : -1)
                .ThenBy(p => p.Id)
                .Select(p => new RedactedPlayer(p))
                .ToList();
        }
    }

    public MoveResult? Move(string playerToken, Direction direction)
    {
        if (string.IsNullOrWhiteSpace(playerToken))
            throw new ArgumentNullException(nameof(playerToken));

        playerToken = playerToken.Replace("\"", "");

        MoveResult moveResult;

        lock (stateLock)
        {
            if (!playersByToken.TryGetValue(playerToken, out var player))
                throw new PlayerNotFoundException();

            if (!playerLocations.TryGetValue(playerToken, out var currentLocation))
                throw new InvalidMoveException("Player is not currently on the board");

            markPlayerAsActive(player);

            var currentPlayer = cells[currentLocation].OccupiedBy;
            if (currentPlayer == null)
                throw new InvalidMoveException("Player is not currently on the board");

            if (CurrentGameState != GameState.Eating && CurrentGameState != GameState.Battle)
                return new MoveResult(currentLocation, false);

            var newLocation = direction switch
            {
                Direction.Up => currentLocation with { Row = currentLocation.Row - 1 },
                Direction.Down => currentLocation with { Row = currentLocation.Row + 1 },
                Direction.Left => currentLocation with { Column = currentLocation.Column - 1 },
                Direction.Right => currentLocation with { Column = currentLocation.Column + 1 },
                _ => throw new DirectionNotRecognizedException()
            };

            if (!cells.ContainsKey(newLocation))
            {
                moveResult = new MoveResult(currentLocation, false);
            }
            else
            {
                var otherPlayer = cells[newLocation].OccupiedBy;
                if (otherPlayer == null)
                {
                    moveResult = movePlayer(player, currentLocation, newLocation);
                }
                else if (CurrentGameState == GameState.Battle)
                {
                    moveResult = attack(currentPlayer, currentLocation, newLocation, otherPlayer);
                }
                else
                {
                    moveResult = new MoveResult(currentLocation, false);
                }
            }
        }

        raiseStateChange();
        return moveResult;
    }

    public IEnumerable<RedactedCell> GetBoardState()
    {
        if (CurrentGameState == GameState.Joining)
            return Array.Empty<RedactedCell>();

        lock (stateLock)
        {
            return cells.Values.Select(c => new RedactedCell(c)).ToList();
        }
    }

    private bool gameAlreadyInProgress => Interlocked.Read(ref gameStateValue) != 0;

    private void raiseStateChange()
    {
        var now = timeProvider.GetUtcNow();
        if (lastStateChange + stateChangeFrequency < now)
        {
            GameStateChanged?.Invoke(this, EventArgs.Empty);
            lastStateChange = now;
        }
    }

    private void initializeGame()
    {
        lock (stateLock)
        {
            if (players.Count > MaxRows * MaxCols)
                throw new TooManyPlayersToStartGameException("too many players");

            var playersThatNeverMoved = players
                .Where(p => !playersThatMovedThisGame.Contains(p.Id))
                .ToList();

            foreach (var player in playersThatNeverMoved)
                RemovePlayerFromRoster(player);

            playersThatMovedThisGame.Clear();

            cells.Clear();
            playerLocations.Clear();
            emptyCells.Clear();
            emptyCellIndexes.Clear();
            remainingPills = MaxRows * MaxCols;
            activePlayersCount = players.Count;

            foreach (var location in from r in Enumerable.Range(0, MaxRows)
                                     from c in Enumerable.Range(0, MaxCols)
                                     select new Location(r, c))
            {
                cells[location] = new Cell(location, true, null);
                AddEmptyCell(location);
            }

            foreach (var player in players)
            {
                var newLocation = new Location(random.Next(MaxRows), random.Next(MaxCols));
                var addToRowIfConflict = true;

                while (cells[newLocation].OccupiedBy != null)
                {
                    var newRow = newLocation.Row;
                    var newCol = newLocation.Column;

                    if (addToRowIfConflict)
                        newRow++;
                    else
                        newCol++;

                    if (newRow >= MaxRows)
                        newRow = 0;

                    if (newCol >= MaxCols)
                        newCol = 0;

                    newLocation = new Location(newRow, newCol);
                    addToRowIfConflict = !addToRowIfConflict;
                }

                cells[newLocation] = cells[newLocation] with { OccupiedBy = player, IsPillAvailable = false };
                playerLocations[player.Token!] = newLocation;
                RemoveEmptyCell(newLocation);
                remainingPills--;
                player.Score = 0;
            }

            pillValues.Clear();
            for (var i = 1; i <= MaxRows * MaxCols; i++)
                pillValues.Enqueue(i);

            stateChangeFrequency = players.Count > 20 || pillValues.Count > 10_000
                ? TimeSpan.FromMilliseconds(200)
                : TimeSpan.FromMilliseconds(50);

            gameStartedAt = timeProvider.GetUtcNow();
            Interlocked.Increment(ref gameStateValue);

            foreach (var player in players)
            {
                scoreHistory.Add(new ScoreSnapshot(player.Id, player.Name, 0, 0.0, GameState.Eating));
            }
        }

        raiseStateChange();
    }

    private MoveResult movePlayer(Player player, Location currentLocation, Location newLocation)
    {
        var ateAPill = false;
        var destinationCell = cells[newLocation];
        if (destinationCell.IsPillAvailable)
        {
            player.Score += getPointValue(newLocation);
            RecordScore(player);
            ateAPill = true;
            remainingPills--;
        }

        cells[newLocation] = destinationCell with { OccupiedBy = player, IsPillAvailable = false };
        cells[currentLocation] = cells[currentLocation] with { OccupiedBy = null };
        playerLocations[player.Token!] = newLocation;
        RemoveEmptyCell(newLocation);
        AddEmptyCell(currentLocation);

        log.LogInformation(
            "Moving {playerName} from {oldLocation} to {newLocation} ({ateNewPill})",
            player.Name,
            currentLocation,
            newLocation,
            destinationCell.IsPillAvailable);

        changeToBattleModeIfNoMorePillsAvailable();

        return new MoveResult(newLocation, ateAPill);
    }

    private MoveResult attack(Player currentPlayer, Location currentLocation, Location newLocation, Player otherPlayer)
    {
        var elapsed = (timeProvider.GetUtcNow() - gameStartedAt).TotalSeconds;
        var minHealth = Math.Min(currentPlayer.Score, otherPlayer.Score);

        log.LogInformation("Player {currentPlayer} attacking {otherPlayer}", currentPlayer, otherPlayer);

        currentPlayer.Score -= minHealth;
        if (currentPlayer.Score <= 0)
            playerEliminationTimes[currentPlayer.Id] = elapsed;
        RecordScore(currentPlayer);

        otherPlayer.Score -= minHealth;
        if (otherPlayer.Score <= 0)
            playerEliminationTimes[otherPlayer.Id] = elapsed;
        RecordScore(otherPlayer);

        log.LogInformation("new scores: {currentPlayerScore}, {otherPlayerScore}", currentPlayer.Score, otherPlayer.Score);

        if (removePlayerIfDead(currentPlayer) || removePlayerIfDead(otherPlayer))
        {
            specialPointValues[newLocation] = (int)Math.Round(minHealth / 2.0, 0);
            checkForWinner();
        }

        return new MoveResult(currentLocation, false);
    }

    private int getPointValue(Location newLocation)
    {
        if (specialPointValues.TryGetValue(newLocation, out var specialPointValue))
        {
            specialPointValues.Remove(newLocation);
            return specialPointValue;
        }

        return pillValues.Dequeue();
    }

    private void changeToBattleModeIfNoMorePillsAvailable()
    {
        if (CurrentGameState != GameState.Eating)
            return;

        if (remainingPills != 0)
            return;

        if (activePlayersCount <= 1)
        {
            Interlocked.Exchange(ref gameStateValue, (long)GameState.GameOver);
            log.LogInformation("Only 1 player left, not going to battle mode - game over.");
            GameOver?.Invoke(this, EventArgs.Empty);
            return;
        }

        Interlocked.Increment(ref gameStateValue);
        battleStartedAt = (timeProvider.GetUtcNow() - gameStartedAt).TotalSeconds;
        log.LogInformation("No more pills available, changing game state to {gameState}", CurrentGameState);
    }

    private void checkForWinner()
    {
        log.LogInformation("checking for winner: {activePlayers} active players", activePlayersCount);

        if (activePlayersCount <= 1)
        {
            log.LogInformation(
                "Changing game state from {currentGameState} to {newGameState}",
                CurrentGameState,
                CurrentGameState + 1);
            Interlocked.Increment(ref gameStateValue);
            GameOver?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RecordScore(Player player)
    {
        var elapsed = (timeProvider.GetUtcNow() - gameStartedAt).TotalSeconds;
        scoreHistory.Add(new ScoreSnapshot(player.Id, player.Name, player.Score, elapsed, CurrentGameState));
    }

    private bool removePlayerIfDead(Player player)
    {
        if (player.Score > 0)
            return false;

        log.LogInformation("Removing player from board: {player}", player);
        if (!playerLocations.Remove(player.Token!, out var location))
            return false;

        cells[location] = cells[location] with { OccupiedBy = null, IsPillAvailable = true };
        AddEmptyCell(location);
        remainingPills++;
        activePlayersCount--;
        return true;
    }

    private void markPlayerAsActive(Player player)
    {
        playersThatMovedThisGame.Add(player.Id);
    }

    private void resetGameCore(bool disposeTimer)
    {
        if (disposeTimer)
            DisposeGameTimer();

        Interlocked.Exchange(ref gameStateValue, (long)GameState.Joining);
        GameEndsOn = null;

        lock (stateLock)
        {
            playerLocations.Clear();
            emptyCells.Clear();
            emptyCellIndexes.Clear();
            scoreHistory.Clear();
            battleStartedAt = null;
            playerEliminationTimes.Clear();

            foreach (var player in players)
                player.Score = 0;
        }

        raiseStateChange();
    }

    private void RemovePlayerFromRoster(Player player)
    {
        players.Remove(player);
        if (!string.IsNullOrWhiteSpace(player.Token))
            playersByToken.Remove(player.Token);
        playersThatMovedThisGame.Remove(player.Id);
    }

    private void AddEmptyCell(Location location)
    {
        if (emptyCellIndexes.ContainsKey(location))
            return;

        emptyCellIndexes[location] = emptyCells.Count;
        emptyCells.Add(location);
    }

    private void RemoveEmptyCell(Location location)
    {
        if (!emptyCellIndexes.TryGetValue(location, out var index))
            return;

        var lastIndex = emptyCells.Count - 1;
        var lastLocation = emptyCells[lastIndex];

        emptyCells[index] = lastLocation;
        emptyCellIndexes[lastLocation] = index;
        emptyCells.RemoveAt(lastIndex);
        emptyCellIndexes.Remove(location);
    }

    private void ScheduleGameTimer(long generation)
    {
        if (!TimeLimit.HasValue)
            return;

        DisposeGameTimer();
        GameEndsOn = timeProvider.GetUtcNow() + TimeLimit.Value;
        gameTimer = timeProvider.CreateTimer(OnTimedGameOver, generation, TimeLimit.Value, Timeout.InfiniteTimeSpan);
    }

    private void OnTimedGameOver(object? state)
    {
        if (state is not long generation || generation != Interlocked.Read(ref timerGeneration))
            return;

        _ = HandleTimedGameOverAsync(generation);
    }

    private async Task HandleTimedGameOverAsync(long generation)
    {
        if (generation != Interlocked.Read(ref timerGeneration))
            return;

        DisposeGameTimer();
        log.LogInformation("Timer ran out. Game over.");
        Interlocked.Exchange(ref gameStateValue, (long)GameState.GameOver);
        GameOver?.Invoke(this, EventArgs.Empty);
        raiseStateChange();

        await DelayAsync(TimeSpan.FromSeconds(5));

        if (generation != Interlocked.Read(ref timerGeneration))
            return;

        resetGameCore(disposeTimer: false);

        if (TimeLimit.HasValue)
            ScheduleGameTimer(Interlocked.Increment(ref timerGeneration));

        initializeGame();
    }

    private void DisposeGameTimer()
    {
        gameTimer?.Dispose();
        gameTimer = null;
    }

    private Task DelayAsync(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
            return Task.CompletedTask;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ITimer? timer = null;

        timer = timeProvider.CreateTimer(_ =>
        {
            timer?.Dispose();
            completion.TrySetResult();
        }, null, delay, Timeout.InfiniteTimeSpan);

        return completion.Task;
    }
}
