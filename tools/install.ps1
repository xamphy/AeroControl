# Builds Aero Control (Release) and installs it to Program Files, then restarts it in the tray.
# Run elevated:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\install.ps1
$ErrorActionPreference = 'Stop'
$log = Join-Path $env:TEMP 'AeroControl-install.log'
function L($m) { $line = "$(Get-Date -f HH:mm:ss) $m"; $line; $line | Out-File $log -Append -Encoding utf8 }
"" | Out-File $log -Encoding utf8

$src = Join-Path $PSScriptRoot '..\src\AeroControl' | Resolve-Path
$dest = Join-Path $env:ProgramFiles 'AeroControl'

$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$b = dotnet build "$src\AeroControl.csproj" -c Release -nologo -v q 2>&1 | Select-Object -Last 3
L ("build: " + ($b -join ' | '))
if (-not (Test-Path "$src\bin\Release\net48\AeroControl.exe")) { L 'BUILD FAILED'; exit 1 }

Get-Process AeroControl -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item "$src\bin\Release\net48\*" $dest -Recurse -Force
L "installed to $dest"

Start-Process -FilePath "$dest\AeroControl.exe" -ArgumentList '--tray'
L 'launched'
