# Runs the architecture and integration tests and prints per-test results and failures only.
# Integration tests use Testcontainers (Docker) unless ERP_TEST_SQL_CONNECTION is set;
# -LocalDb sets it to SQL Server LocalDB for machines without Docker.
param([switch]$LocalDb, [string]$Filter = "")
Set-Location (Join-Path $PSScriptRoot "..")
if ($LocalDb) {
    $env:ERP_TEST_SQL_CONNECTION = "Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true"
}

$filterArgs = @()
if ($Filter) { $filterArgs = @("--filter", $Filter) }

foreach ($project in @("tests/Erp.ArchitectureTests", "tests/Erp.IntegrationTests")) {
    $output = dotnet test $project --no-build --nologo --logger "console;verbosity=normal" @filterArgs 2>&1
    $output | Select-String -Pattern "^\s+Failed Erp|Error Message|^\s{5}\S.*(Exception|Assert)|^\s+at Erp\.(IntegrationTests|ArchitectureTests|Modules|BuildingBlocks|Catalog)|Total tests|^\s+Passed:|^\s+Failed:" |
        ForEach-Object { $_.Line } | Select-Object -First 80
}
