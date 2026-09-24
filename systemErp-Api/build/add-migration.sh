#!/usr/bin/env bash
# Adds an EF Core migration to one context of the modular solution, then applies the house rules:
# migration classes are internal and the model snapshot sits next to the migrations.
#   build/add-migration.sh <module-key> <MigrationName>
#   module keys: platform | catalog | org | identity | authz | settings | accounting | banking | customers | suppliers | payments | inventory | einvoicing
set -euo pipefail
cd "$(dirname "$0")/.."

key=${1:?module key}; name=${2:?migration name}
case "$key" in
  platform) proj=src/BuildingBlocks/Erp.BuildingBlocks.Infrastructure; ctx=PlatformDbContext; out=Outbox/Migrations; ns=Erp.BuildingBlocks.Infrastructure.Outbox.Migrations ;;
  catalog)  proj=src/Catalog/Erp.Catalog; ctx=CatalogDbContext; out=Persistence/Migrations; ns=Erp.Catalog.Persistence.Migrations ;;
  org)      proj=src/Modules/Platform/Organization/Erp.Modules.Organization; ctx=OrganizationDbContext; out=Persistence/Migrations; ns=Erp.Modules.Organization.Persistence.Migrations ;;
  identity) proj=src/Modules/Platform/Identity/Erp.Modules.Identity; ctx=IdentityDbContext; out=Persistence/Migrations; ns=Erp.Modules.Identity.Persistence.Migrations ;;
  authz)    proj=src/Modules/Platform/Permissions/Erp.Modules.Permissions; ctx=PermissionsDbContext; out=Persistence/Migrations; ns=Erp.Modules.Permissions.Persistence.Migrations ;;
  settings) proj=src/Modules/Platform/Settings/Erp.Modules.Settings; ctx=SettingsDbContext; out=Persistence/Migrations; ns=Erp.Modules.Settings.Persistence.Migrations ;;
  accounting) proj=src/Modules/Finance/Accounting/Erp.Modules.Accounting; ctx=AccountingDbContext; out=Persistence/Migrations; ns=Erp.Modules.Accounting.Persistence.Migrations ;;
  banking)  proj=src/Modules/Finance/Banking/Erp.Modules.Banking; ctx=BankingDbContext; out=Persistence/Migrations; ns=Erp.Modules.Banking.Persistence.Migrations ;;
  customers) proj=src/Modules/Parties/Customers/Erp.Modules.Customers; ctx=CustomersDbContext; out=Persistence/Migrations; ns=Erp.Modules.Customers.Persistence.Migrations ;;
  suppliers) proj=src/Modules/Parties/Suppliers/Erp.Modules.Suppliers; ctx=SuppliersDbContext; out=Persistence/Migrations; ns=Erp.Modules.Suppliers.Persistence.Migrations ;;
  payments) proj=src/Modules/Finance/Payments/Erp.Modules.Payments; ctx=PaymentsDbContext; out=Persistence/Migrations; ns=Erp.Modules.Payments.Persistence.Migrations ;;
  einvoicing) proj=src/Modules/Platform/EInvoicing/Erp.Modules.EInvoicing; ctx=EInvoicingDbContext; out=Persistence/Migrations; ns=Erp.Modules.EInvoicing.Persistence.Migrations ;;
  inventory) proj=src/Modules/Trading/Inventory/Erp.Modules.Inventory; ctx=InventoryDbContext; out=Persistence/Migrations; ns=Erp.Modules.Inventory.Persistence.Migrations ;;
  *) echo "unknown module key $key" >&2; exit 2 ;;
esac

dotnet ef migrations add "$name" --project "$proj" --startup-project "$proj" --context "$ctx" --output-dir "$out" --namespace "$ns"

# EF mirrors the namespace as folders for the snapshot; keep it next to the migrations instead.
if [ -d "$proj/Erp" ]; then
  find "$proj/Erp" -name '*ModelSnapshot.cs' -exec mv {} "$proj/$out/" \;
  rm -rf "$proj/Erp"
fi

sed -i 's/^    public partial class /    internal partial class /' "$proj/$out/"*_"$name".cs
echo "Added $name to $ctx ($proj/$out)"
