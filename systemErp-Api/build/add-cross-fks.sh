#!/usr/bin/env bash
# Appends cross-module foreign keys (§10.1) to the end of Up() of a migration.
#   build/add-cross-fks.sh <migration.cs> <schema> "Table,Column,principalSchema,PrincipalTable" ...
set -euo pipefail
file=${1:?migration file}; schema=${2:?schema}; shift 2

line=$(grep -n '^        }$' "$file" | head -1 | cut -d: -f1)
tmp=$(mktemp)
{
  printf '\n            // Cross-module references (§10.1); the principal modules are migrated first (§17.2).\n'
  for fk in "$@"; do
    IFS=, read -r table column principalSchema principalTable <<< "$fk"
    printf '            migrationBuilder.AddCrossModuleForeignKey("%s", "%s", "%s", "%s", "%s");\n' "$schema" "$table" "$column" "$principalSchema" "$principalTable"
  done
} > "$tmp"

awk -v n="$line" -v ins="$tmp" 'NR==n{while((getline l < ins)>0) print l} {print}' "$file" > "$file.new" && mv "$file.new" "$file"
grep -q '^using Erp.BuildingBlocks.Infrastructure.Persistence;' "$file" || \
  sed -i '0,/^using Microsoft.EntityFrameworkCore.Migrations;/s//using Erp.BuildingBlocks.Infrastructure.Persistence;\nusing Microsoft.EntityFrameworkCore.Migrations;/' "$file"
rm -f "$tmp"
echo "Added $# cross-module FK(s) to $file"
