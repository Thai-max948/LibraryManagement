[CmdletBinding()]
param(
    [string]$MasterConnection = $env:LIBRARY_TEST_SQL_MASTER_CONNECTION,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($MasterConnection)) {
    $MasterConnection = 'Server=.\SQLEXPRESS;Database=master;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;Connect Timeout=15'
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $projectRoot 'LibraryManagement.Tests\LibraryManagement.Tests.csproj'
$previousConnection = $env:LIBRARY_TEST_SQL_MASTER_CONNECTION

try {
    $env:LIBRARY_TEST_SQL_MASTER_CONNECTION = $MasterConnection
    Write-Host "Running SQL integration tests against an isolated temporary database..." -ForegroundColor Cyan

    $arguments = @(
        'test',
        $testProject,
        '--configuration', 'Release',
        '--filter', 'Category=Integration',
        '--logger', 'console;verbosity=normal'
    )

    if ($NoBuild) {
        $arguments += '--no-build'
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "SQL integration tests failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:LIBRARY_TEST_SQL_MASTER_CONNECTION = $previousConnection
}
