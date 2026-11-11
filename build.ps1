<#
.SYNOPSIS
    Build script for wb (Web Browser for .NET) using Windows built-in csc.exe.

.DESCRIPTION
    Compiles the wb web browser using the native Windows .NET Framework C# compiler
    (csc.exe) without requiring Visual Studio or the dotnet CLI SDK.
    By default, builds standalone setup wizard installer packages for both x86 and x64 architectures.

.PARAMETER Platform
    Target architecture (x64, x86 or All). Defaults to All.

.PARAMETER Configuration
    Build configuration (Release or Debug). Defaults to Release.

.PARAMETER BrowserOnly
    If set, only compiles unpackaged browser binaries in the build folder without packaging installers.

.PARAMETER NoInstaller
    Alias for BrowserOnly. Skips building the standalone setup wizard installers.

.PARAMETER Run
    If set, launches the browser after a successful build.

.PARAMETER Clean
    If set, cleans build artifacts before building.

.PARAMETER Test
    If set, executes the automated test suite after compilation.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Platform x64
    .\build.ps1 -Platform x86
    .\build.ps1 -BrowserOnly
    .\build.ps1 -Clean -Test
#>

[CmdletBinding()]
param(
    [ValidateSet("x64", "x86", "All")]
    [string]$Platform = "All",

    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [switch]$BrowserOnly,
    [switch]$NoInstaller,
    [switch]$Run,
    [switch]$Clean,
    [switch]$Test
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $ScriptDir

$TargetPlatforms = if ($Platform -in @("All")) {
    @("x86", "x64")
}
else {
    @($Platform)
}

$BuildInstaller = -not ($BrowserOnly -or $NoInstaller)

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "   wb Build Pipeline" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Target Architecture:  $($TargetPlatforms -join ', ')"
Write-Host "  Build Configuration:  $Configuration"
Write-Host "  Build Target:         $(if ($BuildInstaller) { 'Installer Packages' } else { 'Browser Binaries (Unpackaged)' })"
Write-Host "============================================================" -ForegroundColor Cyan

# 1. Check for NuGet and CefSharp packages
$PackagesDir = Join-Path $ScriptDir "packages"
$BinDir = Join-Path $ScriptDir "bin"
if (-not (Test-Path $BinDir)) {
    New-Item -ItemType Directory -Path $BinDir -Force | Out-Null
}

# Clean any legacy un-suffixed wb-setup.exe in bin\
$LegacyTopSetup = Join-Path $BinDir "wb-setup.exe"
if (Test-Path $LegacyTopSetup) {
    Remove-Item $LegacyTopSetup -Force -ErrorAction SilentlyContinue
}

if ($Clean) {
    Get-ChildItem -Path $BinDir -Filter "wb-setup-*.exe" -File -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem -Path $BinDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -in @("x64", "x86", "tests") } | Remove-Item -Recurse -Force
}

$NugetPath = Join-Path $ScriptDir "nuget.exe"
if (-not (Test-Path $NugetPath)) {
    Write-Host "  [NuGet]       Downloading nuget.exe..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $NugetPath -UseBasicParsing
}

# Locate 7-Zip executable for extracting package archives
$7zExe = @(
    "$env:ProgramFiles\7-Zip\7z.exe",
    "${env:ProgramFiles(x86)}\7-Zip\7z.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $7zExe) {
    $cmd = Get-Command "7z.exe" -ErrorAction SilentlyContinue
    if ($cmd) { $7zExe = $cmd.Source }
}

# Ensure CefSharp H.264 packages are installed and extracted for each target architecture
foreach ($arch in $TargetPlatforms) {
    $ArchH264Dir = Join-Path $PackagesDir "CefSharp.H264.$arch"

    # If already extracted into a 'cef' subfolder, flatten to package root
    $SubCefDir = Join-Path $ArchH264Dir "cef"
    if (Test-Path $SubCefDir) {
        Get-ChildItem -Path $SubCefDir | Move-Item -Destination $ArchH264Dir -Force
        Remove-Item -Path $SubCefDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    if (-not (Test-Path (Join-Path $ArchH264Dir "libcef.dll"))) {
        Write-Host "  [Package]     Downloading CefSharp H.264 ($arch)..." -ForegroundColor Yellow
        & $NugetPath install "CefSharp.H264.$arch" -OutputDirectory $PackagesDir -ExcludeVersion

        # Extract embedded cef.<arch>.7z archive if present
        $Embedded7z = Join-Path $ArchH264Dir "doc\cef.$arch.7z"
        if (Test-Path $Embedded7z) {
            if ($7zExe) {
                Write-Host "  [Extract]     Extracting H.264 CEF runtime ($arch)..." -ForegroundColor Cyan
                & $7zExe x $Embedded7z "-o$ArchH264Dir" -y | Out-Null
                if (Test-Path $SubCefDir) {
                    Get-ChildItem -Path $SubCefDir | Move-Item -Destination $ArchH264Dir -Force
                    Remove-Item -Path $SubCefDir -Recurse -Force -ErrorAction SilentlyContinue
                }
            }
            else {
                Write-Warning "7-Zip not found to extract $Embedded7z. Please extract it to $ArchH264Dir."
            }
        }
    }
}

# Stop any running browser instances before modifying files
Get-Process -Name "wb", "wb.sub", "wb.subprocess", "wb.BrowserSubprocess", "ChromeDotNet", "CefSharp.BrowserSubprocess" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

# Clean any stale lockfiles left behind by terminated instances
$ProfileDir = Join-Path $env:LOCALAPPDATA "wb\profile"
if (Test-Path $ProfileDir) {
    Get-ChildItem -Path $ProfileDir -Recurse -Filter "LOCK" -ErrorAction SilentlyContinue | Where-Object { $_.Length -eq 0 } | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $ProfileDir -Filter "Singleton*" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
}

# Gather Source Files
$SourceFiles = Get-ChildItem -Path (Join-Path $ScriptDir "src") -Filter "*.cs" -Recurse |
Where-Object { $_.FullName -notmatch '[\\/]src[\\/]Subprocess[\\/]' } |
Select-Object -ExpandProperty FullName

if ($SourceFiles.Count -eq 0) {
    Write-Error "No C# source files found in src directory."
    exit 1
}

$ManifestPath = Join-Path $ScriptDir "app.manifest"
$AppConfig = Join-Path $ScriptDir "app.config"
$AssetsDir = Join-Path $ScriptDir "assets"
$IconPath = Join-Path $AssetsDir "icons\wb.ico"
$LogoPngPath = Join-Path $AssetsDir "icons\wb_logo.png"
$SvgPath = Join-Path $AssetsDir "icons\wb_icon.svg"
$BuiltExecutables = @()
$BuiltInstallers = @()

# Ensure brand icon (wb.ico) exists; auto-generate from wb_logo.png if missing
if (-not (Test-Path $IconPath) -and (Test-Path $LogoPngPath)) {
    Write-Host "  [Icon]        Generating wb.ico from wb_logo.png..." -ForegroundColor Cyan
    try {
        Add-Type -AssemblyName System.Drawing
        $srcBmp = [System.Drawing.Bitmap]::FromFile($LogoPngPath)
        $sizes = @(16, 24, 32, 48, 64, 128, 256)
        $ms = New-Object System.IO.MemoryStream
        $bw = New-Object System.IO.BinaryWriter($ms)

        # 1. ICONDIR header (6 bytes)
        $bw.Write([uint16]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]$sizes.Count)

        $imagesData = @()
        foreach ($sz in $sizes) {
            $resized = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $g = [System.Drawing.Graphics]::FromImage($resized)
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

            $aspect = [float]$srcBmp.Width / [float]$srcBmp.Height
            $targetW = [float]($sz * 0.94)
            $targetH = [float]($targetW / $aspect)
            if ($targetH -gt ($sz * 0.94)) {
                $targetH = [float]($sz * 0.94)
                $targetW = [float]($targetH * $aspect)
            }
            $x = [float](($sz - $targetW) / 2.0)
            $y = [float](($sz - $targetH) / 2.0)

            $g.DrawImage($srcBmp, (New-Object System.Drawing.RectangleF($x, $y, $targetW, $targetH)))
            $g.Dispose()

            $imgMs = New-Object System.IO.MemoryStream
            $resized.Save($imgMs, [System.Drawing.Imaging.ImageFormat]::Png)
            $resized.Dispose()
            $imagesData += , $imgMs.ToArray()
            $imgMs.Dispose()
        }
        $srcBmp.Dispose()

        # 2. ICONDIRENTRY (16 bytes each)
        $offset = 6 + ($sizes.Count * 16)
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $sz = $sizes[$i]
            $data = $imagesData[$i]
            $w = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }
            $h = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }

            $bw.Write([byte]$w)
            $bw.Write([byte]$h)
            $bw.Write([byte]0)
            $bw.Write([byte]0)
            $bw.Write([uint16]1)
            $bw.Write([uint16]32)
            $bw.Write([uint32]$data.Length)
            $bw.Write([uint32]$offset)
            $offset += $data.Length
        }

        # 3. Write image payload data
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $bw.Write($imagesData[$i])
        }

        [System.IO.File]::WriteAllBytes($IconPath, $ms.ToArray())
        $bw.Dispose()
        $ms.Dispose()
        Write-Host "  [Icon]        wb.ico generated successfully." -ForegroundColor Green
    }
    catch {
        Write-Warning "Could not auto-generate wb.ico: $_"
    }
}

# 2. Compile and Deploy each target architecture
foreach ($CurrentArch in $TargetPlatforms) {
    Write-Host ""
    Write-Host "------------------------------------------------------------" -ForegroundColor Cyan
    Write-Host ">> Target Architecture: $CurrentArch ($Configuration)" -ForegroundColor Cyan
    Write-Host "------------------------------------------------------------" -ForegroundColor Cyan

    # Locate appropriate csc.exe compiler
    $CscPath = if ($CurrentArch -eq "x64") {
        "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    }
    else {
        "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    }

    if (-not (Test-Path $CscPath)) {
        $CscCandidate = Get-ChildItem -Path "C:\Windows\Microsoft.NET" -Filter "csc.exe" -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName

        if ($CscCandidate) {
            $CscPath = $CscCandidate
        }
        else {
            Write-Error "Could not find built-in csc.exe in C:\Windows\Microsoft.NET."
            exit 1
        }
    }

    Write-Host "  [Compiler]    $CscPath"

    # Output directory for staging browser binaries
    $OutputDir = Join-Path $ScriptDir "bin\$CurrentArch\$Configuration"
    if ($Clean -and (Test-Path $OutputDir)) {
        Write-Host "  [Clean]       Removing $OutputDir" -ForegroundColor Yellow
        Remove-Item -Path $OutputDir -Recurse -Force
    }

    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }

    # Resolve Assembly References & Detect CefSharp Version
    $CurrentH264PackageDir = Join-Path $PackagesDir "CefSharp.H264.$CurrentArch"
    if (Test-Path (Join-Path $CurrentH264PackageDir "CefSharp.dll")) {
        Write-Host "  [CefSharp]    H.264 runtime ($CurrentArch, MP4/AAC enabled)" -ForegroundColor Green
        $RefCefSharpCommon = Join-Path $CurrentH264PackageDir "CefSharp.dll"
        $RefCefSharpCore = Join-Path $CurrentH264PackageDir "CefSharp.Core.dll"
        $RefCefSharpWinForms = Join-Path $CurrentH264PackageDir "CefSharp.WinForms.dll"
    }
    else {
        $RefCefSharpCommon = Join-Path $PackagesDir "CefSharp.Common\lib\net462\CefSharp.dll"
        $RefCefSharpCore = Join-Path $PackagesDir "CefSharp.Common\lib\net462\CefSharp.Core.dll"
        $RefCefSharpWinForms = Join-Path $PackagesDir "CefSharp.WinForms\lib\net462\CefSharp.WinForms.dll"
    }

    $CefSharpVersion = $null
    if (Test-Path $RefCefSharpCommon) {
        $CefSharpVersion = (Get-Item $RefCefSharpCommon).VersionInfo.FileVersion
        if (-not $CefSharpVersion) {
            $CefSharpVersion = [System.Reflection.AssemblyName]::GetAssemblyName($RefCefSharpCommon).Version.ToString()
        }
    }
    if (-not $CefSharpVersion) {
        $CefSharpVersion = "133.4.21.0"
    }

    $WbBaseVersion = "1.0.0"
    $WbFullVersion = "$WbBaseVersion-$CefSharpVersion"
    Write-Host "  [Version]     wb $WbBaseVersion (CEF $CefSharpVersion)" -ForegroundColor Green

    # Generate AssemblyInfo.cs with matching version metadata
    $PropsDir = Join-Path $ScriptDir "src\Properties"
    if (-not (Test-Path $PropsDir)) {
        New-Item -ItemType Directory -Path $PropsDir -Force | Out-Null
    }

    $AssemblyInfoPath = Join-Path $PropsDir "AssemblyInfo.cs"
    $AssemblyInfoContent = @"
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("wb")]
[assembly: AssemblyDescription("Lightweight web browser with built-in uBlock protection")]
[assembly: AssemblyCompany("ikmalsaid")]
[assembly: AssemblyProduct("wb")]
[assembly: AssemblyCopyright("Copyright 2026 ikmalsaid")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("$WbFullVersion")]
"@
    Set-Content -Path $AssemblyInfoPath -Value $AssemblyInfoContent -Encoding UTF8

    # Compiler References
    $CscDir = Split-Path -Parent $CscPath
    $WpfDir = Join-Path $CscDir "WPF"
    $RefPresentationCore = Join-Path $WpfDir "PresentationCore.dll"
    $RefWindowsBase = Join-Path $WpfDir "WindowsBase.dll"

    $References = @(
        "System.dll",
        "System.Core.dll",
        "System.Drawing.dll",
        "System.Windows.Forms.dll",
        "System.Data.dll",
        "System.Web.Extensions.dll",
        "System.Xaml.dll",
        $RefPresentationCore,
        $RefWindowsBase,
        $RefCefSharpCommon,
        $RefCefSharpCore,
        $RefCefSharpWinForms
    )

    $RefArgs = $References | ForEach-Object { "/r:`"$_`"" }

    # Compiler Arguments for wb.exe
    $OutputExe = Join-Path $OutputDir "wb.exe"

    $CscArgs = @(
        "/nologo",
        "/t:winexe",
        "/platform:$CurrentArch",
        "/out:`"$OutputExe`""
    )

    if ($Configuration -eq "Release") {
        $CscArgs += "/optimize+"
        $CscArgs += "/debug:pdbonly"
        $CscArgs += "/filealign:512"
        if ($CurrentArch -eq "x64") {
            $CscArgs += "/highentropyva+"
        }
    }
    else {
        $CscArgs += "/optimize-"
        $CscArgs += "/debug:full"
        $CscArgs += "/define:DEBUG;TRACE"
    }

    if (Test-Path $ManifestPath) {
        $CscArgs += "/win32manifest:`"$ManifestPath`""
    }

    if (Test-Path $IconPath) {
        $CscArgs += "/win32icon:`"$IconPath`""
    }

    $CscArgs += $RefArgs
    $CscArgs += ($SourceFiles | ForEach-Object { "`"$_`"" })

    Write-Host "  [Compiling]   wb.exe ($CurrentArch)..." -ForegroundColor Cyan
    $CompileProcess = Start-Process -FilePath $CscPath -ArgumentList $CscArgs -NoNewWindow -Wait -PassThru

    if ($CompileProcess.ExitCode -ne 0) {
        Write-Error "Compilation of wb.exe ($CurrentArch) failed with exit code $($CompileProcess.ExitCode)."
        exit $CompileProcess.ExitCode
    }

    $BuiltExecutables += $OutputExe

    # Compile wb.sub.exe (Browser Subprocess)
    Write-Host "  [Compiling]   wb.sub.exe ($CurrentArch)..." -ForegroundColor Cyan
    $SubprocessSource = Join-Path $ScriptDir "src\Subprocess\Program.cs"
    $CoreDir = Join-Path $OutputDir "core"
    if (-not (Test-Path $CoreDir)) {
        New-Item -ItemType Directory -Path $CoreDir -Force | Out-Null
    }
    $SubprocessExe = Join-Path $CoreDir "wb.sub.exe"

    $SubprocessRefCore = if (Test-Path (Join-Path $CurrentH264PackageDir "CefSharp.BrowserSubprocess.Core.dll")) {
        Join-Path $CurrentH264PackageDir "CefSharp.BrowserSubprocess.Core.dll"
    }
    else {
        Join-Path $PackagesDir "CefSharp.Common\CefSharp\$CurrentArch\CefSharp.BrowserSubprocess.Core.dll"
    }

    $SubprocessArgs = @(
        "/nologo",
        "/t:winexe",
        "/platform:$CurrentArch",
        "/out:`"$SubprocessExe`""
    )

    if ($Configuration -eq "Release") {
        $SubprocessArgs += "/optimize+"
        $SubprocessArgs += "/debug:pdbonly"
        $SubprocessArgs += "/filealign:512"
        if ($CurrentArch -eq "x64") {
            $SubprocessArgs += "/highentropyva+"
        }
    }
    else {
        $SubprocessArgs += "/optimize-"
        $SubprocessArgs += "/debug:full"
        $SubprocessArgs += "/define:DEBUG;TRACE"
    }

    if (Test-Path $ManifestPath) {
        $SubprocessArgs += "/win32manifest:`"$ManifestPath`""
    }

    if (Test-Path $IconPath) {
        $SubprocessArgs += "/win32icon:`"$IconPath`""
    }

    $SubprocessArgs += "/r:`"System.dll`""
    $SubprocessArgs += "/r:`"System.Core.dll`""
    $SubprocessArgs += "/r:`"$RefCefSharpCommon`""
    $SubprocessArgs += "/r:`"$SubprocessRefCore`""
    $SubprocessArgs += "`"$SubprocessSource`""

    $SubprocessCompileProcess = Start-Process -FilePath $CscPath -ArgumentList $SubprocessArgs -NoNewWindow -Wait -PassThru

    if ($SubprocessCompileProcess.ExitCode -ne 0) {
        Write-Error "wb.sub ($CurrentArch) compilation failed with exit code $($SubprocessCompileProcess.ExitCode)."
        exit $SubprocessCompileProcess.ExitCode
    }

    # Deploy CEF runtime binaries into core\
    Write-Host "  [Deploying]   CEF runtime binaries and dependencies..." -ForegroundColor Cyan

    if (Test-Path (Join-Path $CurrentH264PackageDir "libcef.dll")) {
        Get-ChildItem -Path $CurrentH264PackageDir | Where-Object { $_.Name -notin @("cef", "doc", ".signature.p7s") -and $_.Extension -ne ".nupkg" } | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination $CoreDir -Recurse -Force
        }
    }
    else {
        # Managed CefSharp DLLs
        Copy-Item $RefCefSharpCommon $CoreDir -Force
        Copy-Item $RefCefSharpCore $CoreDir -Force
        Copy-Item $RefCefSharpWinForms $CoreDir -Force

        # CefSharp Subprocess & Runtime (Architecture-specific)
        $CefSharpPlatformDir = Join-Path $PackagesDir "CefSharp.Common\CefSharp\$CurrentArch"
        if (Test-Path $CefSharpPlatformDir) {
            Copy-Item "$CefSharpPlatformDir\*" $CoreDir -Force
        }

        # CEF native binaries (Architecture-specific)
        $CefRuntimeNativeDir = Join-Path $PackagesDir "chromiumembeddedframework.runtime.win-$CurrentArch\runtimes\win-$CurrentArch\native"
        if (Test-Path $CefRuntimeNativeDir) {
            Copy-Item "$CefRuntimeNativeDir\*" $CoreDir -Force
        }

        # CEF locales
        $CefLocalesDir = Join-Path $PackagesDir "chromiumembeddedframework.runtime.win-$CurrentArch\CEF\win-$CurrentArch\locales"
        if (Test-Path $CefLocalesDir) {
            Copy-Item $CefLocalesDir $CoreDir -Recurse -Force
        }
    }

    # Custom / Codec-enabled CEF native binaries override (if provided)
    $CustomCefDir = Join-Path $PackagesDir "custom_cef\win-$CurrentArch"
    if (Test-Path $CustomCefDir) {
        Copy-Item "$CustomCefDir\*" $CoreDir -Force
    }

    # Configure wb runtime components in core\
    Write-Host "  [Deploying]   wb runtime components..." -ForegroundColor Cyan
    $CefSharpRenames = @{
        "CefSharp.dll"                        = "wb.dll"
        "CefSharp.Core.dll"                   = "wb.Core.dll"
        "CefSharp.WinForms.dll"               = "wb.WinForms.dll"
        "CefSharp.Core.Runtime.dll"           = "wb.Core.Runtime.dll"
        "CefSharp.BrowserSubprocess.Core.dll" = "wb.sub.core.dll"
    }

    foreach ($orig in $CefSharpRenames.Keys) {
        $srcFile = Join-Path $CoreDir $orig
        $dstFile = Join-Path $CoreDir $CefSharpRenames[$orig]
        if (Test-Path $srcFile) {
            Move-Item -Path $srcFile -Destination $dstFile -Force
        }
    }

    # Remove unused/extraneous CefSharp files and defaults
    Get-ChildItem -Path $CoreDir -Filter "CefSharp.*" -File | Remove-Item -Force
    Get-ChildItem -Path $CoreDir -Filter "*BrowserSubprocess*" -File | Remove-Item -Force
    Get-ChildItem -Path $CoreDir -Filter "*.nupkg" -File -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem -Path $CoreDir -Filter ".signature.p7s" -File -ErrorAction SilentlyContinue | Remove-Item -Force

    $PackageArtifactDirs = @("cef", "doc")
    foreach ($pad in $PackageArtifactDirs) {
        $padPath = Join-Path $CoreDir $pad
        if (Test-Path $padPath) {
            Remove-Item -Path $padPath -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    # App Config for main executable and subprocess executable
    if (Test-Path $AppConfig) {
        Copy-Item $AppConfig (Join-Path $OutputDir "wb.exe.config") -Force
        Copy-Item $AppConfig (Join-Path $CoreDir "wb.sub.exe.config") -Force
    }

    # Remove obsolete Chromium crash_reporter.cfg to prevent Chromium from creating unwanted User Data\Crashpad
    $CrashpadCfgRoot = Join-Path $OutputDir "crash_reporter.cfg"
    $CrashpadCfgCore = Join-Path $CoreDir "crash_reporter.cfg"
    if (Test-Path $CrashpadCfgRoot) {
        Remove-Item $CrashpadCfgRoot -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path $CrashpadCfgCore) {
        Remove-Item $CrashpadCfgCore -Force -ErrorAction SilentlyContinue
    }

    # Move debug symbols to core\
    $SubPdb = Join-Path $OutputDir "wb.sub.pdb"
    if (Test-Path $SubPdb) {
        Move-Item -Path $SubPdb -Destination (Join-Path $CoreDir "wb.sub.pdb") -Force
    }
    $WbPdb = Join-Path $OutputDir "wb.pdb"
    if (Test-Path $WbPdb) {
        Move-Item -Path $WbPdb -Destination (Join-Path $CoreDir "wb.pdb") -Force
    }

    # Deploy application assets into core\assets
    if (Test-Path $AssetsDir) {
        $TargetAssets = Join-Path $CoreDir "assets"
        if (Test-Path $TargetAssets) {
            Remove-Item -Path $TargetAssets -Recurse -Force -ErrorAction SilentlyContinue
        }
        Copy-Item -Path $AssetsDir -Destination $CoreDir -Recurse -Force

        $DuplicateImagesDir = Join-Path $CoreDir "assets\images\images"
        if (Test-Path $DuplicateImagesDir) {
            Remove-Item -Path $DuplicateImagesDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    # Clean legacy root files in OutputDir, including any old setup binaries so they are never in the normal build folder
    $LegacyRootFiles = @(
        "chrome_elf.dll", "d3dcompiler_47.dll", "dxcompiler.dll", "dxil.dll",
        "icudtl.dat", "libEGL.dll", "libGLESv2.dll", "libcef.dll", "resources.pak",
        "v8_context_snapshot.bin", "vk_swiftshader.dll", "vk_swiftshader_icd.json", "vulkan-1.dll",
        "wb.Core.Runtime.dll", "wb.Core.dll", "wb.WinForms.dll", "wb.dll",
        "wb.sub.exe", "wb.sub.core.dll", "wb.sub.exe.config", "wb.sub.pdb",
        "wb-setup.exe", "temp_payload.zip"
    )
    foreach ($f in $LegacyRootFiles) {
        $p = Join-Path $OutputDir $f
        if (Test-Path $p) { Remove-Item $p -Force -ErrorAction SilentlyContinue }
    }
    $LegacyRootPaks = Get-ChildItem -Path $OutputDir -Filter "*.pak" -File -ErrorAction SilentlyContinue
    foreach ($p in $LegacyRootPaks) { Remove-Item $p.FullName -Force -ErrorAction SilentlyContinue }
    $LegacyRootLocales = Join-Path $OutputDir "locales"
    if (Test-Path $LegacyRootLocales) { Remove-Item $LegacyRootLocales -Recurse -Force -ErrorAction SilentlyContinue }
    $LegacyRootAssets = Join-Path $OutputDir "assets"
    if (Test-Path $LegacyRootAssets) { Remove-Item $LegacyRootAssets -Recurse -Force -ErrorAction SilentlyContinue }

    # If building browser only (without installer), report output executable
    if (-not $BuildInstaller) {
        $relExe = $OutputExe.Replace($ScriptDir, "").TrimStart('\', '/')
        Write-Host "  [Success]     $relExe" -ForegroundColor Green
    }

    # ------------------------------------------------------------
    # Installer Packaging (installer\)
    # ------------------------------------------------------------
    if ($BuildInstaller) {
        $InstallerDir = Join-Path $ScriptDir "installer"
        $InstallerAssetsDir = Join-Path $InstallerDir "assets\icons"

        if (-not (Test-Path $InstallerDir)) {
            Write-Host "  [Notice]      Installer folder not found ($InstallerDir). Skipping installer packaging." -ForegroundColor Yellow
        }
        else {
            # Build installer directly into bin\ directory (never into the normal build folder)
            $TopLevelSetupExe = Join-Path $BinDir "wb-setup-$CurrentArch.exe"
            $TempZipPath = Join-Path $BinDir "temp_payload_$CurrentArch.zip"

            if (Test-Path $TempZipPath) { Remove-Item $TempZipPath -Force }
            if (Test-Path $TopLevelSetupExe) { Remove-Item $TopLevelSetupExe -Force }

            Add-Type -AssemblyName System.IO.Compression.FileSystem

            # Create temporary staging directory for payload packaging
            $PayloadStagingDir = Join-Path $BinDir "temp_payload_$CurrentArch"
            if (Test-Path $PayloadStagingDir) { Remove-Item $PayloadStagingDir -Recurse -Force }
            New-Item -ItemType Directory -Path $PayloadStagingDir -Force | Out-Null

            # Copy all files from OutputDir into staging directory, excluding setup exes, zips, cache and logs
            Get-ChildItem -Path $OutputDir | Where-Object {
                $_.Name -notlike "*setup*" -and
                $_.Name -notlike "*.zip" -and
                $_.Name -notlike "*.log" -and
                $_.Name -ne "cache"
            } | ForEach-Object {
                Copy-Item -Path $_.FullName -Destination $PayloadStagingDir -Recurse -Force
            }

            Write-Host "  [Packaging]   Archiving payload into installer package..." -ForegroundColor Cyan
            [System.IO.Compression.ZipFile]::CreateFromDirectory($PayloadStagingDir, $TempZipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
            Remove-Item -Path $PayloadStagingDir -Recurse -Force -ErrorAction SilentlyContinue

            Write-Host "  [Compiling]   wb-setup-$CurrentArch.exe..." -ForegroundColor Cyan
            $SetupCscArgs = @(
                "/nologo",
                "/t:winexe",
                "/platform:$CurrentArch",
                "/optimize+",
                "/out:`"$TopLevelSetupExe`"",
                "/resource:`"$TempZipPath`",wb.payload.zip",
                "/resource:`"$LogoPngPath`",wb.logo.png",
                "/r:System.dll",
                "/r:System.Core.dll",
                "/r:System.Drawing.dll",
                "/r:System.Windows.Forms.dll",
                "/r:Microsoft.CSharp.dll",
                "/r:System.IO.Compression.dll",
                "/r:System.IO.Compression.FileSystem.dll"
            )

            if (Test-Path $SvgPath) {
                $SetupCscArgs += "/resource:`"$SvgPath`",wb.icon.svg"
            }

            $SetupIcons = @(
                "download-simple.svg",
                "rocket-launch.svg",
                "folder-simple.svg",
                "trash.svg",
                "check.svg"
            )
            foreach ($ico in $SetupIcons) {
                $icoPath = Join-Path $InstallerAssetsDir $ico
                if (-not (Test-Path $icoPath)) {
                    $icoPath = Join-Path $IconsDir $ico
                }
                if (Test-Path $icoPath) {
                    $SetupCscArgs += "/resource:`"$icoPath`",icon.$ico"
                }
            }

            if (Test-Path $IconPath) {
                $SetupCscArgs += "/win32icon:`"$IconPath`""
                $SetupCscArgs += "/resource:`"$IconPath`",wb.icon.ico"
            }

            if (Test-Path $ManifestPath) {
                $SetupCscArgs += "/win32manifest:`"$ManifestPath`""
            }

            # Explicitly set main entry class and compile all modular installer sources
            $SetupCscArgs += "/main:wb.Installer.Program"
            $InstallerSources = Get-ChildItem -Path $InstallerDir -Filter "*.cs" -Recurse | Sort-Object FullName
            foreach ($srcFile in $InstallerSources) {
                $SetupCscArgs += "`"$($srcFile.FullName)`""
            }

            $SetupCompileProcess = Start-Process -FilePath $CscPath -ArgumentList $SetupCscArgs -NoNewWindow -Wait -PassThru
            Remove-Item -Path $TempZipPath -Force -ErrorAction SilentlyContinue

            if ($SetupCompileProcess.ExitCode -ne 0) {
                Write-Error "Compilation of wb-setup-$CurrentArch.exe failed with exit code $($SetupCompileProcess.ExitCode)."
                exit $SetupCompileProcess.ExitCode
            }

            $BuiltInstallers += $TopLevelSetupExe

            $relInst = $TopLevelSetupExe.Replace($ScriptDir, "").TrimStart('\', '/')
            Write-Host "  [Success]     $relInst" -ForegroundColor Green

            # Default build creates standalone installer packages only; clean up intermediate unpackaged release folder
            if ($BuildInstaller -and -not $Run) {
                if (Test-Path $OutputDir) {
                    Remove-Item -Path $OutputDir -Recurse -Force -ErrorAction SilentlyContinue
                }
                $ArchFolder = Join-Path $BinDir $CurrentArch
                if (Test-Path $ArchFolder) {
                    $remainingFiles = Get-ChildItem -Path $ArchFolder -Recurse -File -ErrorAction SilentlyContinue
                    if (-not $remainingFiles) {
                        Remove-Item -Path $ArchFolder -Recurse -Force -ErrorAction SilentlyContinue
                    }
                }
            }
        }
    }
}

# 3. Final Build Summary
Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "   Build Summary" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
if ($BuildInstaller) {
    foreach ($inst in $BuiltInstallers) {
        $rel = $inst.Replace($ScriptDir, "").TrimStart('\', '/')
        Write-Host "   Installer:    $rel" -ForegroundColor White
    }
}
else {
    foreach ($exe in $BuiltExecutables) {
        $rel = $exe.Replace($ScriptDir, "").TrimStart('\', '/')
        Write-Host "   Browser:      $rel" -ForegroundColor White
    }
}
Write-Host "============================================================" -ForegroundColor Green
Write-Host "   All requested targets built successfully!" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green

# 4. Run automated tests if requested
if ($Test) {
    Write-Host "`nRunning test suite..." -ForegroundColor Cyan
    $TestParam = if ($Platform -in @("All", "Both")) { "all" } else { $Platform }
    & (Join-Path $ScriptDir "test.bat") $TestParam
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests failed!"
        exit $LASTEXITCODE
    }
}

# 5. Run browser if requested
if ($Run) {
    $RunExe = if (Test-Path (Join-Path $ScriptDir "bin\x64\$Configuration\wb.exe")) {
        Join-Path $ScriptDir "bin\x64\$Configuration\wb.exe"
    }
    elseif (Test-Path (Join-Path $ScriptDir "bin\x86\$Configuration\wb.exe")) {
        Join-Path $ScriptDir "bin\x86\$Configuration\wb.exe"
    }
    elseif ($BuiltExecutables.Count -gt 0) {
        $BuiltExecutables | Select-Object -First 1
    }
    Write-Host "`nLaunching wb Browser ($RunExe)..." -ForegroundColor Cyan
    Start-Process -FilePath $RunExe
}
