#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[a-zA-Z][a-zA-Z0-9.-]*)?$')]
    [string] $Version = '0.1.0',
    [switch] $UseExistingDependencies
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path $PSScriptRoot
$buildRoot = Join-Path $repoRoot ".local/release-$([guid]::NewGuid().ToString('N'))"
$publishDirectory = Join-Path $buildRoot 'app'
$sourceDirectory = Join-Path $buildRoot 'source'
$packageDirectory = Join-Path $buildRoot 'packages'
$outputDirectory = Join-Path $repoRoot "artifacts/releases/v$Version"
$npmCommand = if ($IsWindows) { 'npm.cmd' } else { 'npm' }

function Invoke-Checked {
    param([string] $Command, [string[]] $Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}

function Remove-BuildDirectory {
    param([string] $Path)
    $resolved = [IO.Path]::GetFullPath($Path)
    $boundary = [IO.Path]::GetFullPath($buildRoot) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($boundary, [StringComparison]::Ordinal) -or
        ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing to remove a directory outside this build: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

function Copy-SourceTree {
    param([string] $Directory, [string] $Destination)
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Source link is not allowed: $($entry.FullName)" }
        if ($entry.PSIsContainer) {
            if ($entry.Name -in @('bin', 'obj', 'node_modules', 'dist', 'wwwroot', 'coverage', 'TestResults', 'test-results', 'playwright-report')) { continue }
            Copy-SourceTree $entry.FullName (Join-Path $Destination $entry.Name)
        }
        elseif ($entry.Extension -in @('.cs', '.csproj', '.sln', '.ts', '.tsx', '.css', '.html', '.svg', '.md', '.sh', '.ps1', '.yml') -or
            $entry.Name -in @('packages.lock.json', 'appsettings.json', 'package.json', 'package-lock.json', 'tsconfig.json', '.prettierrc.json', 'dotnet-tools.json', 'SharpCompress.LICENSE.txt', 'EmptyStream.7z')) {
            Copy-Item -LiteralPath $entry.FullName -Destination $Destination
        }
    }
}

function Test-PublishedApplication {
    $probeRoot = Join-Path $buildRoot 'smoke'
    foreach ($folder in @('config', 'downloads', 'roms')) { [IO.Directory]::CreateDirectory((Join-Path $probeRoot $folder)) | Out-Null }
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    $baseUrl = "http://127.0.0.1:$port"
    $startInfo = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
    $startInfo.ArgumentList.Add((Join-Path $publishDirectory 'Ludus.Api.dll'))
    $startInfo.WorkingDirectory = $publishDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    # Do not inherit development-instance paths or security settings into the probe.
    foreach ($key in @($startInfo.Environment.Keys)) {
        if ($key -match '^(Ludus__|ASPNETCORE_|DOTNET_ENVIRONMENT$)') { $startInfo.Environment.Remove($key) | Out-Null }
    }
    $startInfo.Environment['ASPNETCORE_ENVIRONMENT'] = 'Production'
    $startInfo.Environment['ASPNETCORE_URLS'] = $baseUrl
    $startInfo.Environment['Ludus__ConfigDirectory'] = Join-Path $probeRoot 'config'
    $startInfo.Environment['Ludus__DownloadDirectory'] = Join-Path $probeRoot 'downloads'
    $startInfo.Environment['Ludus__RomDirectory'] = Join-Path $probeRoot 'roms'
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $started = $false
    try {
        if (-not $process.Start()) { throw 'Could not start the published application.' }
        $started = $true
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $ready = $false
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            if ($process.HasExited) { throw "Published application exited with $($process.ExitCode)." }
            try {
                $health = Invoke-RestMethod "$baseUrl/api/v1/system/status" -TimeoutSec 2 -NoProxy
                if ($health.status -eq 'ok') { $ready = $true; break }
            }
            catch [System.Net.Http.HttpRequestException] { }
            catch [System.Threading.Tasks.TaskCanceledException] { }
            Start-Sleep -Milliseconds 500
        }
        if (-not $ready) { throw 'Published application did not become healthy.' }
        $page = Invoke-WebRequest "$baseUrl/" -TimeoutSec 5 -NoProxy
        if ($page.Content -notmatch '<title>Ludus</title>') { throw 'Published frontend is missing.' }
        $asset = [regex]::Match($page.Content, 'src="(/assets/[^\"]+\.js)"').Groups[1].Value
        if (-not $asset) { throw 'Frontend JavaScript entry point is missing.' }
        $assetResponse = Invoke-WebRequest "$baseUrl$asset" -TimeoutSec 5 -NoProxy
        if ($assetResponse.Headers['Content-Type'] -notmatch 'javascript') { throw 'Frontend asset did not return JavaScript.' }
        if (-not (Test-Path -LiteralPath (Join-Path $probeRoot 'config/ludus.db'))) { throw 'SQLite was not initialized.' }
        Write-Host 'Published application verified: health, SQLite initialization, frontend and JavaScript.'
    }
    finally {
        if ($started -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        if (Test-Path variable:stdout) { [IO.File]::WriteAllText((Join-Path $probeRoot 'stdout.log'), $stdout.GetAwaiter().GetResult()) }
        if (Test-Path variable:stderr) { [IO.File]::WriteAllText((Join-Path $probeRoot 'stderr.log'), $stderr.GetAwaiter().GetResult()) }
        $process.Dispose()
    }
}

if (Test-Path -LiteralPath $outputDirectory) { throw "Release directory already exists: $outputDirectory. Preserve it or choose a new version." }
foreach ($command in @('dotnet', $npmCommand, 'tar')) { Get-Command $command -ErrorAction Stop | Out-Null }
foreach ($directory in @($publishDirectory, $sourceDirectory, $packageDirectory)) { [IO.Directory]::CreateDirectory($directory) | Out-Null }

Push-Location $repoRoot
try {
    if (-not $UseExistingDependencies) {
        Invoke-Checked $npmCommand @('--prefix', 'frontend', 'ci', '--no-audit', '--no-fund')
        Invoke-Checked dotnet @('restore', 'src/Ludus.sln', '--locked-mode')
    }
    Invoke-Checked $npmCommand @('--prefix', 'frontend', 'test')
    Invoke-Checked $npmCommand @('--prefix', 'frontend', 'run', 'build')
    Invoke-Checked dotnet @('build', 'src/Ludus.sln', '-c', 'Release', '--no-restore', "-p:Version=$Version")
    Invoke-Checked dotnet @('test', 'src/Ludus.sln', '-c', 'Release', '--no-build', '--no-restore')
    Invoke-Checked dotnet @('publish', 'src/Ludus.Api/Ludus.Api.csproj', '-c', 'Release', '--no-restore', '-o', $publishDirectory, '-p:UseAppHost=false', "-p:Version=$Version")

    # Replace only this new publish directory's static files, never the source tree.
    $webRoot = Join-Path $publishDirectory 'wwwroot'
    if (Test-Path -LiteralPath $webRoot) { Remove-BuildDirectory $webRoot }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'frontend/dist') -Destination $webRoot -Recurse
    foreach ($rid in @('linux-x64', 'linux-arm64')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory "runtimes/$rid/native/libe_sqlite3.so"))) { throw "Missing SQLite library for $rid." }
    }
    foreach ($entry in Get-ChildItem -LiteralPath $publishDirectory -Recurse -Force) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $entry.Name -match '(?i)(^\.env|\.db(-wal|-shm)?$|\.pfx$|\.pem$|^bootstrap-api-key$|^key-.*\.xml$)') {
            throw "Unexpected link or sensitive runtime file in publish output: $($entry.FullName)"
        }
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/releases.md') -Destination (Join-Path $publishDirectory 'RELEASE.md')
    @{ version = $Version; framework = 'net10.0'; deployment = 'framework-dependent'; builtAtUtc = [DateTime]::UtcNow.ToString('O') } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publishDirectory 'release.json') -Encoding utf8NoBOM
    Test-PublishedApplication

    foreach ($directory in @('.github', '.config', 'src', 'frontend', 'tests', 'docker', 'docs', 'scripts')) {
        Copy-SourceTree (Join-Path $repoRoot $directory) (Join-Path $sourceDirectory $directory)
    }
    foreach ($file in @('.dockerignore', '.editorconfig', '.env.example', '.gitattributes', '.gitignore', 'Directory.Build.props', 'Directory.Packages.props', 'Dockerfile', 'NuGet.Config', 'README.md', 'docker-compose.yml', 'docker-compose.hardlinks.yml', 'global.json', 'install-lxc.sh')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $sourceDirectory
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($sourceDirectory, (Join-Path $packageDirectory "ludus-$Version-source.zip"))
    [IO.Compression.ZipFile]::CreateFromDirectory($publishDirectory, (Join-Path $packageDirectory "ludus-$Version-portable.zip"))
    Invoke-Checked tar @('-czf', (Join-Path $packageDirectory "ludus-$Version-portable.tar.gz"), '-C', $publishDirectory, '.')
    $checksums = foreach ($archive in Get-ChildItem -LiteralPath $packageDirectory -File | Sort-Object Name) {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $archive.Name
    }
    [IO.File]::WriteAllText((Join-Path $packageDirectory 'SHA256SUMS'), ($checksums -join "`n") + "`n")
    [IO.Directory]::CreateDirectory((Split-Path $outputDirectory)) | Out-Null
    # Both paths are constructed beneath this workspace, and the destination must be new.
    if (Test-Path -LiteralPath $outputDirectory) { throw 'Release destination appeared during the build.' }
    Move-Item -LiteralPath $packageDirectory -Destination $outputDirectory
    Write-Host "Release ready: $outputDirectory"
    Write-Host "Build and isolated smoke-test files: $buildRoot"
}
finally { Pop-Location }
