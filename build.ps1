param(
    [string] $Configuration = "Release",
    [string] $GamePath = "",
    [switch] $Deploy,
    [switch] $Package,
    [switch] $SkipPostProcess
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $projectRoot "TextureUnifier.csproj"

function Get-FullPath([string] $Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-ChildPath([string] $Path, [string] $Parent) {
    $fullPath = Get-FullPath $Path
    $fullParent = (Get-FullPath $Parent).TrimEnd('\') + '\'
    if (!$fullPath.StartsWith($fullParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside expected parent. Path: $fullPath Parent: $fullParent"
    }
}

function Remove-DirectoryIfExists([string] $Path, [string] $RequiredParent) {
    Assert-ChildPath $Path $RequiredParent
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Get-SteamLibraryPaths {
    $libraryFiles = @(
        "C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf",
        "C:\Program Files\Steam\steamapps\libraryfolders.vdf"
    )

    foreach ($libraryFile in $libraryFiles) {
        if (!(Test-Path -LiteralPath $libraryFile)) {
            continue
        }

        Get-Content -LiteralPath $libraryFile | ForEach-Object {
            if ($_ -match '"path"\s+"([^"]+)"') {
                $matches[1] -replace '\\\\', '\'
            }
        }
    }
}

function Find-GamePath {
    $candidates = New-Object System.Collections.Generic.List[string]
    $candidates.Add("E:\SteamLibrary\steamapps\common\Cities Skylines II")
    $candidates.Add("C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II")

    foreach ($library in Get-SteamLibraryPaths) {
        $candidates.Add((Join-Path $library "steamapps\common\Cities Skylines II"))
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate "Cities2_Data\Managed\Game.dll")) {
            return $candidate
        }
    }

    throw "Could not find Cities: Skylines II. Pass -GamePath '...\Cities Skylines II'."
}

function Get-UserEnvironmentValue([string] $Name) {
    return [Environment]::GetEnvironmentVariable($Name, "User")
}

function Invoke-ModPostProcessor([string] $AssemblyPath, [string] $GamePath) {
    $postProcessorPath = Get-UserEnvironmentValue "CSII_MODPOSTPROCESSORPATH"
    if ([string]::IsNullOrWhiteSpace($postProcessorPath) -or !(Test-Path -LiteralPath $postProcessorPath)) {
        $postProcessorPath = Join-Path $GamePath "Cities2_Data\Content\Game\.ModdingToolchain\ModPostProcessor\ModPostProcessor.exe"
    }

    if (!(Test-Path -LiteralPath $postProcessorPath)) {
        throw "Could not find ModPostProcessor.exe. Open Cities: Skylines II > Options > Modding and install/update the modding toolchain."
    }

    $unityModProjectPath = Get-UserEnvironmentValue "CSII_UNITYMODPROJECTPATH"
    if ([string]::IsNullOrWhiteSpace($unityModProjectPath)) {
        $unityModProjectPath = Join-Path (Join-Path $env:USERPROFILE "AppData\LocalLow\Colossal Order\Cities Skylines II") ".cache\Modding\UnityModsProject"
    }

    if (!(Test-Path -LiteralPath $unityModProjectPath)) {
        throw "Could not find Unity mod project cache: $unityModProjectPath. Open Cities: Skylines II > Options > Modding and install/update the modding toolchain."
    }

    $managedPath = Join-Path $GamePath "Cities2_Data\Managed"
    $referenceNames = @(
        "mscorlib.dll",
        "System.dll",
        "System.Core.dll",
        "System.Xml.dll",
        "System.Xml.Linq.dll",
        "netstandard.dll",
        "Game.dll",
        "Colossal.Logging.dll",
        "Colossal.Core.dll",
        "Colossal.Mathematics.dll",
        "Colossal.IO.AssetDatabase.dll",
        "Colossal.Localization.dll",
        "Colossal.Mono.Cecil.dll",
        "Newtonsoft.Json.dll",
        "Unity.Entities.dll",
        "Unity.Collections.dll",
        "Unity.Mathematics.dll",
        "UnityEngine.dll",
        "UnityEngine.CoreModule.dll",
        "UnityEngine.ImageConversionModule.dll",
        "UnityEngine.VFXModule.dll"
    )

    $args = New-Object System.Collections.Generic.List[string]
    $args.Add("PostProcess")
    $args.Add($AssemblyPath)
    $args.Add("-u")
    $args.Add($unityModProjectPath)

    foreach ($referenceName in $referenceNames) {
        $referencePath = Join-Path $managedPath $referenceName
        if (Test-Path -LiteralPath $referencePath) {
            $args.Add("-r")
            $args.Add($referencePath)
        }
    }

    $args.Add("-p")
    $args.Add("Windows")
    $args.Add("-p")
    $args.Add("macOS")
    $args.Add("-p")
    $args.Add("Linux")
    $args.Add("-d")

    Write-Host "Post-processing mod output with $postProcessorPath"
    & $postProcessorPath @args
    if ($LASTEXITCODE -ne 0) {
        throw "ModPostProcessor failed with exit code $LASTEXITCODE"
    }
}

function Copy-ModOutputFiles([string] $SourceDirectory, [string] $DestinationDirectory) {
    New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null

    $files = Get-ChildItem -LiteralPath $SourceDirectory -File | Where-Object {
        $_.Name -eq "TextureUnifier.dll" -or
        $_.Name -eq "TextureUnifier.pdb" -or
        $_.Name -like "TextureUnifier_*"
    }

    if (!$files) {
        throw "No mod output files found in $SourceDirectory"
    }

    foreach ($file in $files) {
        Copy-Item -Force -LiteralPath $file.FullName -Destination (Join-Path $DestinationDirectory $file.Name)
    }
}

if ([string]::IsNullOrWhiteSpace($GamePath)) {
    $GamePath = Find-GamePath
}

$managedGameDll = Join-Path $GamePath "Cities2_Data\Managed\Game.dll"
if (!(Test-Path -LiteralPath $managedGameDll)) {
    throw "GamePath does not look like a Cities: Skylines II install: $GamePath"
}

dotnet build $projectPath -c $Configuration -p:GamePath="$GamePath"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

$dllPath = Join-Path $projectRoot "bin\$Configuration\TextureUnifier.dll"
if (!(Test-Path -LiteralPath $dllPath)) {
    throw "Build did not produce $dllPath"
}

if (($Deploy -or $Package) -and !$SkipPostProcess) {
    Invoke-ModPostProcessor $dllPath $GamePath
}

if ($Deploy) {
    $localLow = Join-Path $env:USERPROFILE "AppData\LocalLow\Colossal Order\Cities Skylines II"
    $modsRoot = Join-Path $localLow "Mods"
    $target = Join-Path $modsRoot "TextureUnifier"
    $staleDisabledTarget = Join-Path $modsRoot "TextureUnifier.disabled"
    $oldCacheRoot = Join-Path $localLow ".cache\Mods\mods_unmanaged"
    $oldCacheTarget = Join-Path $oldCacheRoot "TextureUnifier_1"
    $dataRoot = Join-Path $localLow "ModsData\TextureUnifier"
    $texturesRoot = Join-Path $dataRoot "Textures"
    $packsRoot = Join-Path $dataRoot "Packs"
    $importInboxRoot = Join-Path $dataRoot "ImportInbox"
    $configPath = Join-Path $dataRoot "config.json"
    $sampleConfigPath = Join-Path $projectRoot "examples\config.sample.json"

    New-Item -ItemType Directory -Force -Path $target | Out-Null
    New-Item -ItemType Directory -Force -Path $texturesRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $packsRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $importInboxRoot | Out-Null

    Copy-ModOutputFiles (Join-Path $projectRoot "bin\$Configuration") $target

    Remove-DirectoryIfExists $staleDisabledTarget $modsRoot
    Remove-DirectoryIfExists $oldCacheTarget $oldCacheRoot

    if (!(Test-Path -LiteralPath $configPath) -and (Test-Path -LiteralPath $sampleConfigPath)) {
        Copy-Item -Path $sampleConfigPath -Destination $configPath
    }

    Write-Host "Deployed TextureUnifier.dll to $target"
    Write-Host "Prepared Texture Unifier data folder at $dataRoot"
    Write-Host "Put loose textures in $texturesRoot"
    Write-Host "Put switchable texture packs in $packsRoot"
    Write-Host "Put one-off import textures in $importInboxRoot"
    Write-Host "Restart Cities: Skylines II with code mods enabled. Check Logs\TextureUnifier.Mod.log after launch."
}

if ($Package) {
    [xml] $projectXml = Get-Content -LiteralPath $projectPath
    $version = ($projectXml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
    if ([string]::IsNullOrWhiteSpace($version)) {
        $version = "0.1.0"
    }

    $distRoot = Join-Path $projectRoot "dist"
    $packageName = "TextureUnifier-v$version"
    $packageRoot = Join-Path $distRoot $packageName
    $modRoot = Join-Path $packageRoot "TextureUnifier"
    $zipPath = Join-Path $distRoot "$packageName.zip"

    New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
    Remove-DirectoryIfExists $packageRoot $distRoot
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    $examplesTarget = Join-Path $modRoot "examples"
    $sourceTarget = Join-Path $modRoot "source"
    New-Item -ItemType Directory -Force -Path $examplesTarget | Out-Null
    New-Item -ItemType Directory -Force -Path $sourceTarget | Out-Null
    Copy-ModOutputFiles (Join-Path $projectRoot "bin\$Configuration") $modRoot
    Copy-Item -Force -Path (Join-Path $projectRoot "README.md") -Destination (Join-Path $modRoot "README.md")
    Copy-Item -Force -Path (Join-Path $projectRoot "USER_GUIDE.md") -Destination (Join-Path $modRoot "USER_GUIDE.md")
    Copy-Item -Force -Path (Join-Path $projectRoot "SECURITY.md") -Destination (Join-Path $modRoot "SECURITY.md")
    Copy-Item -Force -Path (Join-Path $projectRoot "SOURCE.md") -Destination (Join-Path $modRoot "SOURCE.md")
    Copy-Item -Recurse -Force -Path (Join-Path $projectRoot "examples\*") -Destination $examplesTarget
    Copy-Item -Recurse -Force -Path (Join-Path $projectRoot "src") -Destination $sourceTarget
    Copy-Item -Force -Path $projectPath -Destination (Join-Path $sourceTarget "TextureUnifier.csproj")

    Compress-Archive -Path (Join-Path $packageRoot "TextureUnifier") -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Packaged release zip: $zipPath"
}
