[CmdletBinding(PositionalBinding=$false)]
param(
    [bool] $CreatePackages,
    [switch] $StartServers,
    [bool] $RunTests = $true,
    [string] $PullRequestNumber
)

Write-Host "Run Parameters:" -ForegroundColor Cyan
Write-Host "  CreatePackages: $CreatePackages"
Write-Host "  RunTests: $RunTests"
Write-Host "  dotnet --version:" (dotnet --version)

$packageOutputFolder = "$PSScriptRoot\.nupkgs"
$projectsToBuild =
    'StackExchange.Redis',
    'NRediSearch'

$testsToRun =
    'StackExchange.Redis.Tests'

if ($PullRequestNumber) {
    Write-Host "Building for a pull request (#$PullRequestNumber), skipping packaging." -ForegroundColor Yellow
    $CreatePackages = $false
}

Write-Host "Building projects..." -ForegroundColor "Magenta"
foreach ($project in $projectsToBuild + $testsToRun) {
    Write-Host "Building $project (dotnet restore/build)..." -ForegroundColor "Magenta"
    dotnet restore ".\src\$project\$project.csproj" /p:CI=true
    dotnet build ".\src\$project\$project.csproj" -c Release /p:CI=true
    Write-Host ""
}
Write-Host "Done building." -ForegroundColor "Green"

if ($RunTests) {
    if ($StartServers) {
        Write-Host "Starting all servers for testing: $project (all frameworks)" -ForegroundColor "Magenta"
        & .\RedisConfigs\start-all.cmd
        Write-Host "Servers Started." -ForegroundColor "Green"
    }
    foreach ($project in $testsToRun) {
        Write-Host "Running tests: $project (all frameworks)" -ForegroundColor "Magenta"
        #Push-Location ".\tests\$project"
        Push-Location ".\tests\$project"

        dotnet test -c Release
        if ($LastExitCode -ne 0) {
            Write-Host "Error with tests, aborting build." -Foreground "Red"
            Pop-Location
            Exit 1
        }

        Write-Host "Tests passed!" -ForegroundColor "Green"
	    Pop-Location
    }
}

if ($CreatePackages) {
    mkdir -Force $packageOutputFolder | Out-Null
    Write-Host "Clearing existing $packageOutputFolder..." -NoNewline
    Get-ChildItem $packageOutputFolder | Remove-Item
    Write-Host "done." -ForegroundColor "Green"

    Write-Host "Building all packages" -ForegroundColor "Green"

    foreach ($project in $projectsToBuild) {
        Write-Host "Packing $project (dotnet pack)..." -ForegroundColor "Magenta"
        dotnet pack ".\src\$project\$project.csproj" --no-build -c Release /p:PackageOutputPath=$packageOutputFolder /p:NoPackageAnalysis=true /p:CI=true
        Write-Host ""
    }
}

Write-Host "Done."