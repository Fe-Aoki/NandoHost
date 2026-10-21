$ErrorActionPreference = 'Stop'
$script:ProjectRoot = Split-Path $PSScriptRoot -Parent

function Resolve-Dotnet([string]$Executable) {
    if ($Executable) { return (Resolve-Path -LiteralPath $Executable).Path }
    $command = Get-Command dotnet -ErrorAction Stop
    return $command.Source
}

function Invoke-Dotnet([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
