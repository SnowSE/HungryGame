param(
    [Parameter(Mandatory = $true, HelpMessage = "host and port of server")]
    [string]$url,
    [Parameter(Mandatory = $true, HelpMessage = "your player name")]
    [string]$name
)

$delay = 0

function invoke($path) {
    if ($script:delay -gt 0) {
        Start-Sleep -Milliseconds $script:delay
    }
    while ($true) {
        try {
            $response = iwr $path
            if ($script:delay -gt 0) {
                $script:delay = [math]::Max(0, $script:delay - 50)
            }
            return $response
        }
        catch {
            $statusCode = $null
            if ($_.Exception.Response) {
                $statusCode = [int]$_.Exception.Response.StatusCode
            }
            if ($statusCode -eq 429) {
                $script:delay = [math]::Min(500, $script:delay + 100)
                write-host "Rate limited. Slowing down to $($script:delay)ms delay."
                Start-Sleep -Milliseconds $script:delay
            }
            else {
                throw
            }
        }
    }
}

$token = invoke "$url/join?userName=$name"

write-host "Waiting for game to start..."
while ($true) {
    $state = (invoke "$url/state").Content.Trim('"')
    if ($state -ne "Joining" -and $state -ne "GameOver") {
        write-host "Game is running ($state). Let's go!"
        break
    }
    Start-Sleep -Seconds 1
}

$size = 0

while ($true) {
    $size = $size + 1

    write-host "moving right $size"
    for ($i = 0; $i -lt $size; $i++) {
        invoke "$url/move/right?token=$token" | out-null
    }

    write-host "moving down $size"
    for ($i = 0; $i -lt $size; $i++) {
        invoke "$url/move/down?token=$token" | out-null
    }

    $size = $size + 1

    write-host "moving left $size"
    for ($i = 0; $i -lt $size; $i++) {
        invoke "$url/move/left?token=$token" | out-null
    }

    write-host "moving up $size"
    for ($i = 0; $i -lt $size; $i++) {
        invoke "$url/move/up?token=$token" | out-null
    }
}