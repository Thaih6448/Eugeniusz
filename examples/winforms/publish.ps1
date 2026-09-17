param(
    [string]$OutputDirectory = "",
    [string]$NativeDirectory = ""
)
$ErrorActionPreference = 'Stop'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot '../../dist/winforms' }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
foreach ($app in @('Decisions', 'Snake', 'PixelArt', 'Driving')) {
    $project = Join-Path $PSScriptRoot "$app/$app.csproj"
    $arguments = @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', (Join-Path $OutputDirectory $app))
    if ($NativeDirectory) { $arguments += "-p:EugeniuszNativeDir=$([System.IO.Path]::GetFullPath($NativeDirectory))" }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Publishing $app failed with exit code $LASTEXITCODE" }
    $destination = Join-Path $OutputDirectory $app
    $repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Copy-Item -LiteralPath (Join-Path $repository 'LICENSE'), (Join-Path $repository 'THIRD_PARTY_NOTICES.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $repository 'third_party/licenses') -Destination $destination -Recurse -Force
    $assets = Get-Content -LiteralPath (Join-Path $PSScriptRoot "$app/obj/project.assets.json") -Raw | ConvertFrom-Json
    $runtime = Get-Content -LiteralPath (Join-Path $destination "Eugeniusz.$app.runtimeconfig.json") -Raw | ConvertFrom-Json
    foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
        $package = "$($framework.name.ToLowerInvariant()).runtime.win-x64/$($framework.version)"
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $packagePath = Join-Path $folder $package
            if (Test-Path -LiteralPath $packagePath) {
                foreach ($notice in (Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' })) {
                    Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $destination "licenses/$($framework.name)-$($notice.Name)") -Force
                }
                break
            }
        }
    }
}
Write-Host "Published applications: $OutputDirectory"
