# Builds a project/solution and prints only the distinct errors and warnings (paths shortened).
param([string]$Target = "Erp.Modular.sln")
Set-Location (Join-Path $PSScriptRoot "..")
$output = dotnet build $Target -v q -nologo 2>&1
$lines = $output | Select-String -Pattern ": (error|warning) " | ForEach-Object { ($_.Line -replace '\s*\[[^\]]*\]$', '') -replace '^.*\\(src|tests)\\', '' } | Sort-Object -Unique
if ($lines) { $lines | Select-Object -First 60 } else { "BUILD OK" }
if ($LASTEXITCODE -ne 0 -and -not $lines) { $output | Select-Object -Last 20 }
