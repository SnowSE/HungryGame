[CmdletBinding()]
param(
    [Parameter(HelpMessage = "host and port of server")]    
    [string]$url = "http://localhost:5291",
    [Parameter(HelpMessage = "your player name")]    
    [string]$name = [DateTime]::Now.Ticks % 10000
)

function randomDirection {
    $directions = "up", "down", "right", "left"
    return $directions[(get-random -max 4)]
}

$client = [System.Net.Http.HttpClient]::new()
$delay = 0

function call($path) {
    if ($script:delay -gt 0) {
        Start-Sleep -Milliseconds $script:delay
    }
    while ($true) {
        $response = $client.GetAsync($path).GetAwaiter().GetResult()
        if ([int]$response.StatusCode -eq 429) {
            $script:delay = $script:delay + 500
            write-host "Rate limited. Slowing down to $($script:delay)ms delay."
            Start-Sleep -Milliseconds $script:delay
            continue
        }
        $response.EnsureSuccessStatusCode() | Out-Null
        if ($script:delay -gt 0) {
            $script:delay = [math]::Max(0, $script:delay - 50)
        }
        return $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    }
}

function get-gameState {
    $r = call "$url/state"
    return $r
}

$token = call "$url/join?userName=$name"
write-host "$name joined game w/token $token"

$gameState = get-gameState
while ( $gameState -eq "Joining") {
    write-verbose "Game state is $gameState...sleeping"
    Start-Sleep -Seconds 2
    $gameState = get-gameState
}

$direction = randomDirection;
$reqCount = 0;

while ($true) {
    $reqCount++;
    try {
        $moveResult = (call "$url/move/$direction/?token=$token") | ConvertFrom-Json
        if ($null -eq $moveResult -or $moveResult.ateAPill -eq $false) {
            $newDirection = randomDirection
            Write-verbose "$name didn't eat a token going $direction, so I'll try going $newDirection"
            $direction = $newDirection;
        }    
    }
    catch {
        write-error "$name got an exception.  I'm out.`n Exception details: `n$_"        
        break;
    }

    if ($reqCount -gt 0) {
        if ((get-gameState) -eq "GameOver") {
            Write-Host "Game over. Player $name ends."
            break;
        }
    }
}