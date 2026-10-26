param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "$PSScriptRoot\dist"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "PCLink.Net.csproj"

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

dotnet publish $project -c $Configuration -r $Runtime -o $Output `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $Output "PCLink.Net.exe"
Get-ChildItem $Output | Where-Object { $_.FullName -ne $exe } | Remove-Item -Recurse -Force

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Built $exe ($size MB)" -ForegroundColor Green
