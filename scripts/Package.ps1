param([string]$DotnetPath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$dotnet = Resolve-Dotnet $DotnetPath
[xml]$project = Get-Content -LiteralPath (Join-Path $script:ProjectRoot 'src/NandoHost.App/NandoHost.App.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versão inválida no projeto.' }
$dist = Join-Path $script:ProjectRoot 'dist'
$target = Join-Path $dist "NandoHost-$version-Windows.zip"
if (Test-Path -LiteralPath $target) { throw 'O pacote já existe. Preserve-o ou altere a versão antes de gerar outro.' }
$stage = Join-Path $script:ProjectRoot ('artifacts/package-' + [guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $stage 'NandoHost'
$appDestination = Join-Path $packageRoot 'app'
& (Join-Path $PSScriptRoot 'Build.ps1') -DotnetPath $dotnet -Destination $appDestination
Copy-Item -LiteralPath (Join-Path $script:ProjectRoot 'licenses') -Destination (Join-Path $packageRoot 'licenses') -Recurse
Copy-Item -LiteralPath (Join-Path $script:ProjectRoot 'THIRD_PARTY_NOTICES.md') -Destination $packageRoot
@"
# NandoHost $version

Instale o .NET Desktop Runtime 10 para Windows e abra app/NandoHost.exe.
Os servidores e mundos ficam em app/Data, criado no primeiro uso.
Escolha Português (Brasil) ou English no cabeçalho da biblioteca ou na lateral dentro do jogo. A preferência fica salva em Data/ui-settings.json.
Para atualizar, feche o programa e copie a pasta Data da versão anterior para app/Data.
Guarde um backup antes da atualização. Não abra duas cópias usando os mesmos dados.

Código e documentação de desenvolvimento: https://github.com/Fe-Aoki/NandoHost
O repositório é público; o aplicativo continua em desenvolvimento.
"@ | Set-Content -LiteralPath (Join-Path $packageRoot 'LEIA-ME.md') -Encoding UTF8
if (Test-Path -LiteralPath (Join-Path $appDestination 'Data')) { throw 'Pacote contém dados de servidor.' }
New-Item -ItemType Directory -Path $dist -Force | Out-Null
Compress-Archive -Path $packageRoot -DestinationPath $target -CompressionLevel Optimal
Write-Output "Pacote gerado em $target"
