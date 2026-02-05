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
    public int Next(int maxValue);
}

public class SystemRandomService : IRandomService
{
    private readonly Random random = new();
    public int Next(int maxValue) => random.Next(maxValue);
}

public class GameLogic
{
    private readonly object lockForPlayersCellsPillValuesAndSpecialPontValues = new();
    private readonly List<Player> players = new();
    private readonly Dictionary<string, Location> playerLocations = new();
    private readonly Dictionary<Location, Cell> cells = new();
    private readonly Queue<int> pillValues = new();
    private readonly Dictionary<Location, int> specialPointValues = new();
    private readonly HashSet<Player> playersThatMovedThisGame = new();
    private readonly HashSet<Location> emptyCells = new();

    private int remainingPills = 0;
    private int activePlayersCount = 0;
    private int number = 0;
    private long gameStateValue = 0;
    private readonly IConfiguration config;
    private readonly ILogger<GameLogic> log;
    private readonly IRandomService random;

    public int MaxRows { get; private set; } = 0;
    public int MaxCols { get; private set; } = 0;
    public event EventHandler? GameStateChanged;

    public GameLogic(IConfiguration config, ILogger<GameLogic> log, IRandomService random)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.log = log;
        this.random = random;
    }

    public DateTime lastStateChange;
    public TimeSpan stateChangeFrequency;

    private void raiseStateChange()
    {
        if (lastStateChange + stateChangeFrequency < DateTime.Now)
        {
            GameStateChanged?.Invoke(this, EventArgs.Empty);
            lastStateChange = DateTime.Now;
        }
    }

    public bool IsGameStarted => Interlocked.Read(ref gameStateValue) != 0;
    public GameState CurrentGameState => (GameState)Interlocked.Read(ref gameStateValue);
    public bool IsGameOver => Interlocked.Read(ref gameStateValue) == 3;
    public DateTime? GameEndsOn { get; private set; }
    public TimeSpan? TimeLimit { get; private set; }
    public TimeSpan? TimeRemaining => GameEndsOn.HasValue ? GameEndsOn.Value - DateTime.Now : null;
    private Timer gameTimer;

    public void StartGame(NewGameInfo gameInfo)
    {
        if (gameInfo.SecretCode != config["SECRET_CODE"] || Interlocked.Read(ref gameStateValue) != 0)
        {
            return;
        }

        MaxRows = gameInfo.NumRows;
        MaxCols = gameInfo.NumColumns;

        if (gameInfo.IsTimed && gameInfo.TimeLimitInMinutes.HasValue)
        {
            var minutes = gameInfo.TimeLimitInMinutes.Value;
            TimeLimit = TimeSpan.FromMinutes(minutes);
            GameEndsOn = DateTime.Now.Add(TimeLimit.Value);
            gameTimer = new Timer(gameOverCallback, null, TimeLimit.Value, Timeout.InfiniteTimeSpan);
        }

        initializeGame();
    }

    private async void gameOverCallback(object? state)
    {
        log.LogInformation($"Timer ran out.  Game over.");
        Interlocked.Exchange(ref gameStateValue, 3);

        await Task.Delay(TimeSpan.FromSeconds(5));

        resetGame();
        if (TimeLimit.HasValue)
        {
            GameEndsOn = DateTime.Now.Add(TimeLimit.Value);
            gameTimer = new Timer(gameOverCallback, null, TimeLimit.Value, Timeout.InfiniteTimeSpan);
        }


        initializeGame();
    }

    private void initializeGame()
    {
        lock (lockForPlayersCellsPillValuesAndSpecialPontValues)
        {
            if (players.Count > MaxRows * MaxCols)
            {
                throw new TooManyPlayersToStartGameException("too many players");
            }

            var playersThatNeverMoved = players.Except(playersThatMovedThisGame).ToList();
            if (playersThatNeverMoved.Any())
            {
                players.RemoveAll(p => playersThatNeverMoved.Contains(p));
            }
            playersThatMovedThisGame.Clear();

            cells.Clear();
            playerLocations.Clear();
            emptyCells.Clear();
            remainingPills = MaxRows * MaxCols;
            activePlayersCount = players.Count;

            foreach (var location in from r in Enumerable.Range(0, MaxRows)
                                     from c in Enumerable.Range(0, MaxCols)
                                     select new Location(r, c))
            {
                cells.TryAdd(location, new Cell(location, true, null));
                emptyCells.Add(location);
            }

            foreach (var player in players)
            {
                var newLocation = new Location(random.Next(MaxRows), random.Next(MaxCols));
                bool addToRowIfConflict = true;
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
                emptyCells.Remove(newLocation);
                remainingPills--;
                player.Score = 0;
            }

            pillValues.Clear();
            for (int i = 1; i <= MaxRows * MaxCols; i++)
            {
                pillValues.Enqueue(i);
            }

            if (players.Count > 20 || pillValues.Count > 10_000)
                stateChangeFrequency = TimeSpan.FromMilliseconds(750);
            else
                stateChangeFrequency = TimeSpan.FromMilliseconds(250);

            Interlocked.Increment(ref gameStateValue);
        }

        raiseStateChange();
    }

    public void ResetGame(string secretCode)
    {
        if (secretCode != config["SECRET_CODE"] || Interlocked.Read(ref gameStateValue) == 0)
        {
            return;
        }

        resetGame();
    }

    private void resetGame()
    {
        Interlocked.Exchange(ref gameStateValue, 0);

        lock (lockForPlayersCellsPillValuesAndSpecialPontValues)
        {
            playerLocations.Clear();
            emptyCells.Clear();
            foreach (var p in players)
            {
                p.Score = 0;
            }
        }

        raiseStateChange();
    }

    public Cell GetCell(int row, int col) => cells[new Location(row, col)];

    public string JoinPlayer(string playerName)
    {
        var token = Guid.NewGuid().ToString();
        log.LogInformation("{playerName} wants to join (will be {token})", playerName, token);

        lock (lockForPlayersCellsPillValuesAndSpecialPontValues)
        {
            var id = Interlocked.Increment(ref number);
            log.LogDebug("Got lock; new user will be ID# {id}", id);

            var joinedPlayer = new Player { Id = id, Name = playerName, Token = token };
            players.Add(joinedPlayer);

            markPlayerAsActive(joinedPlayer);

            if (gameAlreadyInProgress)
            {
                if (emptyCells.Count == 0)
                {
                    throw new NoAvailableSpaceException("there is no available space");
                }
                var randomIndex = random.Next(emptyCells.Count);
                var newLocation = emptyCells.ElementAt(randomIndex);
                var origCell = cells[newLocation];
                var newCell = origCell with { OccupiedBy = joinedPlayer, IsPillAvailable = false };
                cells[newLocation] = newCell;
                playerLocations[token] = newLocation;
                emptyCells.Remove(newLocation);
                activePlayersCount++;
            }
        }

        raiseStateChange();
        return token;
    }

    private bool gameAlreadyInProgress => Interlocked.Read(ref gameStateValue) != 0;

    public IEnumerable<Player> GetPlayersByScoreDescending() =>
        players.OrderByDescending(p => p.Score);

    public MoveResult? Move(string playerToken, Direction direction)
    {
        if (string.IsNullOrWhiteSpace(playerToken))
            throw new ArgumentNullException(nameof(playerToken));

        playerToken = playerToken.Replace("\"", "");

        Player player;
        Cell cell;
        MoveResult moveResult;

        lock (lockForPlayersCellsPillValuesAndSpecialPontValues)
        {
            player = players.FirstOrDefault(p => p.Token == playerToken);
            if (player == null)
            {
                throw new PlayerNotFoundException();
            }

            if (!playerLocations.TryGetValue(playerToken, out var currentLocation))
            {
                throw new InvalidMoveException("Player is not currently on the board");
            }

            cell = cells[currentLocation];
            markPlayerAsActive(player);

            var currentPlayer = cell.OccupiedBy;
            if (currentPlayer == null)
            {
                throw new InvalidMoveException("Player is not currently on the board");
            }

            if (CurrentGameState != GameState.Eating && CurrentGameState != GameState.Battle)
            {
                return new MoveResult(currentLocation, false);
            }

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
                Player? otherPlayer = cells[newLocation].OccupiedBy;
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

    private void markPlayerAsActive(Player player)
    {
        if (!playersThatMovedThisGame.Contains(player))
            playersThatMovedThisGame.Add(player);
    }

    private MoveResult movePlayer(Player player, Location currentLocation, Location newLocation)
    {
        bool ateAPill = false;
        var origDestinationCell = cells[newLocation];
        if (origDestinationCell.IsPillAvailable)
        {
            player.Score += getPointValue(newLocation);
            ateAPill = true;
            remainingPills--;
        }
        var newDestinationCell = origDestinationCell with { OccupiedBy = player, IsPillAvailable = false };

        var origSourceCell = cells[currentLocation];
        var newSourceCell = origSourceCell with { OccupiedBy = null };

        log.LogInformation("Moving {playerName} from {oldLocation} to {newLocation} ({ateNewPill})", player.Name, currentLocation, newLocation, origDestinationCell.IsPillAvailable);

        cells[newLocation] = newDestinationCell;
        cells[currentLocation] = newSourceCell;
        playerLocations[player.Token!] = newLocation;
        emptyCells.Remove(newLocation);
        emptyCells.Add(currentLocation);

        changeToBattleModeIfNoMorePillsAvailable();

        return new MoveResult(newLocation, ateAPill);
    }

    private MoveResult attack(Player currentPlayer, Location currentLocation, Location newLocation, Player otherPlayer)
    {
        //decrease the health of both players by the min health of the players
        var minHealth = Math.Min(currentPlayer.Score, otherPlayer.Score);
        log.LogInformation("Player {currentPlayer} attacking {otherPlayer}", currentPlayer, otherPlayer);

        currentPlayer.Score -= minHealth;
        otherPlayer.Score -= minHealth;
        log.LogInformation("new scores: {currentPlayerScore}, {otherPlayerScore}", currentPlayer.Score, otherPlayer.Score);

        if (removePlayerIfDead(currentPlayer) || removePlayerIfDead(otherPlayer))
        {
            specialPointValues.TryAdd(newLocation, (int)Math.Round(minHealth / 2.0, 0));
            checkForWinner();
        }

        return new MoveResult(currentLocation, false);
    }

    private int getPointValue(Location newLocation)
    {
        int pointValue = 0;

        if (specialPointValues.ContainsKey(newLocation))
        {
            pointValue = specialPointValues[newLocation];
            specialPointValues.Remove(newLocation);
        }
        else
        {
            pointValue = pillValues.Dequeue();
        }

        return pointValue;
    }

    private void checkForWinner()
    {
        log.LogInformation("checking for winner: {activePlayers} active players", activePlayersCount);

        if (activePlayersCount <= 1)
        {
            log.LogInformation("Changing game state from {currentGameState} to {newGameState}", CurrentGameState, (CurrentGameState + 1));
            Interlocked.Increment(ref gameStateValue);
        }
    }

    private bool removePlayerIfDead(Player player)
    {
        if (player == null || player.Score > 0)
            return false;

        log.LogInformation("Removing player from board: {player}", player);
        if (playerLocations.Remove(player.Token!, out var location))
        {
            var origCell = cells[location];
            var updatedCell = origCell with { OccupiedBy = null, IsPillAvailable = true };
            cells[location] = updatedCell;
            emptyCells.Add(location);
            remainingPills++;
            activePlayersCount--;
            return true;
        }
        return false;
    }

    public IEnumerable<RedactedCell> GetBoardState()
    {
        if (CurrentGameState == GameState.Joining)
            return new RedactedCell[] { };

        lock (lockForPlayersCellsPillValuesAndSpecialPontValues)
        {
            return cells.Values.Select(c => new RedactedCell(c)).ToList();
        }
    }

    private void changeToBattleModeIfNoMorePillsAvailable()
    {
        if (CurrentGameState != GameState.Eating)
            return;

        if (remainingPills == 0)
        {
            if (activePlayersCount <= 1)
            {
                Interlocked.Exchange(ref gameStateValue, 3);//game over
                log.LogInformation("Only 1 player left, not going to battle mode - game over.");
            }
            else
            {
                Interlocked.Increment(ref gameStateValue);
                log.LogInformation("No more pills available, changing game state to {gameState}", CurrentGameState);
            }
        }
    }
}
