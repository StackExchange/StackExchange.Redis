[CmdletBinding(PositionalBinding=$false)]
param(
    [string] $Version,
    [string] $BuildNumber,
    [bool] $CreatePackages,
    [bool] $RunTests = $true,
    [string] $PullRequestNumber
)
    
function CalculateVersion() {
    if ($Version) {
        return $Version
    }

    $semVersion = '';
    $path = $pwd;
    while (!$semVersion) {
        if (Test-Path (Join-Path $path "semver.txt")) {
            $semVersion = Get-Content (Join-Path $path "semver.txt")
            break
        }
        if ($PSScriptRoot -eq $path) {
            break
        }
        $path = Split-Path $path -Parent
    }

    if (!$semVersion) {
        Write-Error "semver.txt was not found in $pwd or any parent directory"
        Exit 1
    }

    return "$semVersion-$BuildNumber"
}

if ($BuildNumber -and $BuildNumber.Length -lt 5) {
    $BuildNumber = $BuildNumber.PadLeft(5, "0")
}

Write-Host "Run Parameters:" -ForegroundColor Cyan
Write-Host "Version: $Version"
Write-Host "BuildNumber: $BuildNumber"
Write-Host "CreatePackages: $CreatePackages"
Write-Host "RunTests: $RunTests"
Write-Host "Base Version: $(CalculateVersion)"

$packageOutputFolder = "$PSScriptRoot\.nupkgs"
$projectsToBuild =
    'StackExchange.Redis',
    'StackExchange.Redis.StrongName',
    'NRediSearch'

$testsToRun =
    'StackExchange.Redis.Tests'
if (!$Version -and !$BuildNumber) {
    Write-Host "ERROR: You must supply either a -Version or -BuildNumber argument. `
  Use -Version `"4.0.0`" for explicit version specification, or `
  Use -BuildNumber `"12345`" for generation using <semver.txt>-<buildnumber>" -ForegroundColor Yellow
    Exit 1
}

if ($PullRequestNumber) {
    Write-Host "Building for a pull request (#$PullRequestNumber), skipping packaging." -ForegroundColor Yellow
    $CreatePackages = $false
}

if ($RunTests) {
    dotnet restore /ConsoleLoggerParameters:Verbosity=Quiet
    foreach ($project in $testsToRun) {
        Write-Host "Running tests: $project (all frameworks)" -ForegroundColor "Magenta"
        #Push-Location ".\tests\$project"
        Push-Location ".\$project"

        dotnet xunit -configuration Release
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
}

foreach ($project in $projectsToBuild) {
    Write-Host "Working on $project`:" -ForegroundColor "Magenta"
	
    #Push-Location ".\src\$project"
    Push-Location ".\$project"

    $semVer = CalculateVersion
    $targets = "Restore"

    Write-Host "  Restoring " -NoNewline -ForegroundColor "Magenta"
    if ($CreatePackages) {
        $targets += ";Pack"
		Write-Host "and packing " -NoNewline -ForegroundColor "Magenta"
    }
	Write-Host "$project... (Version:" -NoNewline -ForegroundColor "Magenta"
    Write-Host $semVer -NoNewline -ForegroundColor "Cyan"
    Write-Host ")" -ForegroundColor "Magenta"
    

	dotnet msbuild "/t:$targets" "/p:Configuration=Release" "/p:Version=$semVer" "/p:PackageOutputPath=$packageOutputFolder" "/p:CI=true"

	Pop-Location

    Write-Host "Done."
    Write-Host ""
}