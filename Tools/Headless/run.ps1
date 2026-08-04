# Roda a simulacao fora do Unity, em ~3 segundos, sem instalar nada.
#
# O Unity traz um runtime .NET 6 e o compilador Roslyn junto — nao ha SDK nesta maquina e nao
# precisa haver. Como DT.Core e DT.Sim nao referenciam UnityEngine (e DefaultContent/WaveBuilder
# tambem nao), a partida inteira compila e roda aqui. Isso e o que permite medir balanceamento e
# volume sem esperar o domain reload do editor, e e o unico caminho de CI que este projeto tem.
#
#   .\Tools\Headless\run.ps1            # 8 seeds por configuracao
#   .\Tools\Headless\run.ps1 -Seeds 24  # mais amostras, mais lento
param(
    [int]$Seeds = 8,
    [string]$UnityVersion = "6000.3.21f1"
)

$ErrorActionPreference = "Stop"

$editor = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Data"
if (-not (Test-Path $editor)) {
    Write-Error "Editor nao encontrado em $editor. Passe -UnityVersion com a versao instalada."
}

$dotnet = "$editor\NetCoreRuntime\dotnet.exe"
$csc    = "$editor\DotNetSdkRoslyn\csc.dll"
$netstd = "$editor\NetStandard\ref\2.1.0\netstandard.dll"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$code = Join-Path $root "Assets\_Project\Code"
$out  = Join-Path $PSScriptRoot "bin"
New-Item -ItemType Directory -Force $out | Out-Null

# DT.Data inteiro nao entra: o resto dele e ScriptableObject e arrasta a engine junto.
$files = @()
$files += Get-ChildItem (Join-Path $code "Core"), (Join-Path $code "Sim") -Recurse -Filter *.cs |
          ForEach-Object { $_.FullName }
$files += (Join-Path $code "Data\DefaultContent.cs"), (Join-Path $code "Data\WaveBuilder.cs")
$files += (Join-Path $PSScriptRoot "Program.cs"), (Join-Path $PSScriptRoot "Verify.cs")

& $dotnet $csc -target:exe -nostdlib -noconfig -langversion:9.0 "-r:$netstd" `
    -out:"$out\DT.Headless.dll" $files
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# O compilador nao gera isto sozinho sem o SDK; sao as tres linhas que dizem ao runtime qual
# framework carregar.
'{ "runtimeOptions": { "tfm": "net6.0", "framework": { "name": "Microsoft.NETCore.App", "version": "6.0.0" } } }' |
    Out-File -Encoding utf8 "$out\DT.Headless.runtimeconfig.json"

& $dotnet "$out\DT.Headless.dll" $Seeds
exit $LASTEXITCODE
