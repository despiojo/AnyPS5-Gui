# Starts the published app and checks that its window comes up and stays up. The unit tests cover the
# logic; this catches what only shows in a real launch, such as a publish that left a resource behind.
param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$Work
)

$ErrorActionPreference = 'Stop'

# The app's own data goes to a scratch folder, not to the real settings of whoever runs this.
$data = Join-Path $Work 'data'
New-Item -ItemType Directory -Force $data | Out-Null
$env:ANYPS5GUI_DATA = $data

$process = Start-Process -FilePath $Exe -PassThru
try {
    $deadline = (Get-Date).AddSeconds(60)
    while ($process.MainWindowHandle -eq 0) {
        if ($process.HasExited) { throw "The app exited with code $($process.ExitCode) before showing its window." }
        if ((Get-Date) -gt $deadline) { throw 'The app showed no window within 60 seconds.' }
        Start-Sleep -Milliseconds 500
        $process.Refresh()
    }

    # A page that fails to load takes the app down a moment after the window appears.
    Start-Sleep -Seconds 5
    if ($process.HasExited) { throw "The app exited with code $($process.ExitCode) after showing its window." }

    $errors = Join-Path $data 'errors.log'
    if (Test-Path $errors) { throw "The app reported an error:`n$(Get-Content $errors -Raw)" }
    Write-Host 'The app started and its window stayed up.'
}
finally {
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
    }
}
