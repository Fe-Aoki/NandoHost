param([string]$DotnetPath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$dotnet = Resolve-Dotnet $DotnetPath
$testOutput = Join-Path $script:ProjectRoot 'artifacts/tests'
Push-Location $script:ProjectRoot
try {
    Invoke-Dotnet -Executable $dotnet -Arguments @('restore', 'tests/NandoHost.Tests/NandoHost.Tests.csproj', '--configfile', 'NuGet.Config', '-m:1', '-nr:false')
    Invoke-Dotnet -Executable $dotnet -Arguments @('publish', 'tests/NandoHost.Tests/NandoHost.Tests.csproj', '-c', 'Release', '--no-restore', '-o', $testOutput)
    & (Join-Path $testOutput 'NandoHost.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw "Testes falharam: $LASTEXITCODE" }
} finally { Pop-Location }
