namespace HungryGame;

public class AdminTokenService
{
    private readonly HashSet<string> _tokens = new();
    private readonly object _lock = new();
    private readonly IConfiguration _config;

    public AdminTokenService(IConfiguration config)
    {
        _config = config;
    }

    public string? Login(string password)
    {
        if (password != _config["SECRET_CODE"])
            return null;

        var token = Guid.NewGuid().ToString();
        lock (_lock) { _tokens.Add(token); }
        return token;
    }

    public void Logout(string token)
    {
        lock (_lock) { _tokens.Remove(token); }
    }

    public bool IsValid(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        lock (_lock) { return _tokens.Contains(token); }
    }
}
