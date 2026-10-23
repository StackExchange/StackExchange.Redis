[CmdletBinding(PositionalBinding=$false)]
param(
    [bool] $CreatePackages,
    [switch] $StartServers,
    [bool] $RunTests = $true,
    [string] $PullRequestNumber,
    [switch] $NetCoreOnlyTests
)

Write-Host "Run Parameters:" -ForegroundColor Cyan
Write-Host "  CreatePackages: $CreatePackages"
Write-Host "  RunTests: $RunTests"
Write-Host "  dotnet --version:" (dotnet --version)

$packageOutputFolder = "$PSScriptRoot\.nupkgs"

if ($PullRequestNumber) {
    Write-Host "Building for a pull request (#$PullRequestNumber), skipping packaging." -ForegroundColor Yellow
    $CreatePackages = $false
}

Write-Host "Building all projects (Build.csproj traversal)..." -ForegroundColor "Magenta"
dotnet build ".\Build.csproj" -c Release /p:CI=true
Write-Host "Done building." -ForegroundColor "Green"

if ($RunTests) {
    if ($StartServers) {
        Write-Host "Starting all servers for testing: $project (all frameworks)" -ForegroundColor "Magenta"
        & .\RedisConfigs\start-all.cmd
        Write-Host "Servers Started." -ForegroundColor "Green"
    }
    Write-Host "Running tests: StackExchange.Redis.slnx (all test projects)" -ForegroundColor "Magenta"
    if ($NetCoreOnlyTests) {
        # net10.0, not net8.0: the analyzer and fault-injector test projects only target net10.0, and the
        # Microsoft.Testing.Platform runner fails a project that lacks the requested framework rather than skipping it
        dotnet test --solution ".\StackExchange.Redis.slnx" -c Release -f net10.0 --no-build --report-xunit-trx
    } else {
        dotnet test --solution ".\StackExchange.Redis.slnx" -c Release --no-build --report-xunit-trx
    }
    if ($LastExitCode -ne 0) {
        Write-Host "Error with tests, aborting build." -Foreground "Red"
        Exit 1
    }
    Write-Host "Tests passed!" -ForegroundColor "Green"
}

if ($CreatePackages) {
    New-Item -ItemType Directory -Path $packageOutputFolder -Force | Out-Null
    Write-Host "Clearing existing $packageOutputFolder..." -NoNewline
    Get-ChildItem $packageOutputFolder | Remove-Item
    Write-Host "done." -ForegroundColor "Green"

    Write-Host "Building all packages" -ForegroundColor "Green"
    dotnet pack ".\Build.csproj" --no-build -c Release /p:PackageOutputPath=$packageOutputFolder /p:CI=true
}

Write-Host "Done."