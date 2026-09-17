param(
    [ValidateSet('Decisions', 'Snake', 'PixelArt', 'Driving')][string]$App = 'Decisions',
    [string]$Model = '',
    [switch]$Gpu
)
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot "$App/$App.csproj"
$launchArguments = @('run', '--project', $projectPath, '-c', 'Release', '--')
if ($Model) { $launchArguments += @('--model', [System.IO.Path]::GetFullPath($Model)) }
if ($Gpu) { $launchArguments += '--gpu' }
& dotnet @launchArguments
exit $LASTEXITCODE
