param(
    [double]$MinimumLineRate = 70,
    [double]$MinimumBranchRate = 55,
    [double]$MinimumCriticalLineRate = 80
)

$ErrorActionPreference = 'Stop'
$solutionRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $solutionRoot 'DeepDroidChanger.slnx'
$resultsDirectory = Join-Path $solutionRoot 'TestResults'
$coveragePath = Join-Path $resultsDirectory 'coverage.cobertura.xml'
$settingsPath = Join-Path $PSScriptRoot 'coverage.settings.xml'

if (Test-Path -LiteralPath $resultsDirectory) {
    Remove-Item -LiteralPath $resultsDirectory -Recurse -Force
}

$packageAudit = & dotnet list $solution package --vulnerable --include-transitive 2>&1
$packageAudit | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
if (($packageAudit -join "`n") -match 'has the following vulnerable packages') {
    Write-Error 'Package vulnerability audit found one or more vulnerable packages.'
    exit 1
}

& dotnet test $solution --no-build --no-restore `
    --coverage `
    --coverage-output-format cobertura `
    --coverage-output 'coverage.cobertura.xml' `
    --coverage-settings $settingsPath `
    --results-directory $resultsDirectory `
    --timeout 120s
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

[xml]$coverage = Get-Content -LiteralPath $coveragePath
$lineRate = [double]$coverage.coverage.'line-rate' * 100
$branchRate = [double]$coverage.coverage.'branch-rate' * 100
$failures = [System.Collections.Generic.List[string]]::new()

if ($lineRate -lt $MinimumLineRate) {
    $failures.Add("Line coverage $($lineRate.ToString('F2'))% is below $MinimumLineRate%.")
}

if ($branchRate -lt $MinimumBranchRate) {
    $failures.Add("Branch coverage $($branchRate.ToString('F2'))% is below $MinimumBranchRate%.")
}

$criticalClasses = @(
    'DeepDroidChanger.Authentication.Internal.AccountAuthenticationService',
    'DeepDroidChanger.Services.DeviceIntegrityService',
    'DeepDroidChanger.Services.DeviceRandomProfileService',
    'DeepDroidChanger.Services.PackageInstallService',
    'DeepDroidChanger.Services.ProxyService',
    'DeepDroidChanger.Services.RandomDeviceService',
    'DeepDroidChanger.Services.XapkPackageService'
)
$classes = @($coverage.coverage.packages.package.classes.class)
foreach ($className in $criticalClasses) {
    $class = $classes | Where-Object { $_.name -eq $className } | Select-Object -First 1
    if ($null -eq $class) {
        $failures.Add("Critical coverage class was not found: $className.")
        continue
    }

    $classLineRate = [double]$class.'line-rate' * 100
    if ($classLineRate -lt $MinimumCriticalLineRate) {
        $failures.Add("Critical class $className has $($classLineRate.ToString('F2'))% line coverage; required $MinimumCriticalLineRate%.")
    }
}

Write-Host "Coverage: line $($lineRate.ToString('F2'))%, branch $($branchRate.ToString('F2'))%."
if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}
