param([string]$DotnetPath, [string]$Destination)
. (Join-Path $PSScriptRoot 'Common.ps1')
$dotnet = Resolve-Dotnet $DotnetPath
if (-not $Destination) { $Destination = Join-Path $script:ProjectRoot 'artifacts/app' }
$Destination = [System.IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath (Join-Path $Destination 'Data')) {
    throw 'A pasta de destino contém dados de um aplicativo em uso. Escolha outra pasta.'
}
Push-Location $script:ProjectRoot
try {
    Invoke-Dotnet -Executable $dotnet -Arguments @('restore', 'NandoHost.slnx', '--configfile', 'NuGet.Config', '-m:1', '-nr:false')
    Invoke-Dotnet -Executable $dotnet -Arguments @('publish', 'src/NandoHost.App/NandoHost.App.csproj', '-c', 'Release', '--no-restore', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $Destination)
    Write-Output "Aplicativo gerado em $Destination"
} finally { Pop-Location }
