param(
    [string]$MasterConnection = 'Server=.\SQLEXPRESS;Database=master;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;Connect Timeout=10'
)
$ErrorActionPreference = 'Stop'
$taskRepoRoot = Split-Path -Parent $PSScriptRoot
$taskPreviousConnection = $env:LIBRARY_TEST_SQL_MASTER_CONNECTION
try {
    $env:LIBRARY_TEST_SQL_MASTER_CONNECTION = $MasterConnection
    dotnet build (Join-Path $taskRepoRoot 'LibraryManagement.slnx') -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet test (Join-Path $taskRepoRoot 'LibraryManagement.Tests\LibraryManagement.Tests.csproj') -c Release --no-build --filter 'Category=Integration'
    if ($LASTEXITCODE -ne 0) { throw 'SQL integration failed. Review connection/authentication or test failures.' }
}
finally {
    $env:LIBRARY_TEST_SQL_MASTER_CONNECTION = $taskPreviousConnection
}
