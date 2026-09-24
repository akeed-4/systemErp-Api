using ERP.Tests.Infrastructure;

namespace ERP.Tests;

[Collection("api")]
public class AccountingTests : TestBase
{
    public AccountingTests(ErpFactory f) : base(f) { }

    private static object Sale(Guid product, decimal qty, string method, Guid? party = null, decimal price = 100)
        => new { kind = "sales", invoiceType = "simplified", paymentMethod = method, partyId = party, items = new[] { new { itemId = product, quantity = qty, unitPrice = price, vatRate = 15 } } };

    // ---------- شجرة الحسابات والقيود ----------
    [Fact]
    public async Task Customer_supplier_and_bank_get_linked_ledger_accounts_under_the_right_parents()
    {
        var api = await NewTenantAsync();
        var (_, cAcc) = await SeedCustomerAsync(api);
        var (_, sAcc) = await SeedSupplierAsync(api);
        var bank = await api.Post("/banks", new { nameAr = "بنك", accountNumber = "1", iban = "SA0380000000608010167519", status = "active" });
        Assert.StartsWith("112", cAcc); Assert.StartsWith("211", sAcc); Assert.StartsWith("111", bank.Data!["accountCode"].S());
        Assert.Equal(409, (await api.Delete($"/accounts/{(await api.Get($"/accounts/by-code/{cAcc}")).Data!["id"].S()}")).Status); // مرتبط بعميل
    }

    [Fact]
    public async Task Manual_journal_must_balance_and_can_only_post_to_leaf_accounts()
    {
        var api = await NewTenantAsync();
        await api.Post("/accounts", new { code = "311", nameAr = "رأس المال المدفوع", nameEn = "Paid Capital", type = "equity", parentCode = "31", isDebitNature = false });

        var unbalanced = await api.Post("/journalentries", new { description = "x", lines = new[] { new { accountCode = "1111", debit = 10, credit = 0 }, new { accountCode = "311", debit = 0, credit = 5 } } });
        Assert.Equal(400, unbalanced.Status);
        var parent = await api.Post("/journalentries", new { description = "x", lines = new[] { new { accountCode = "11", debit = 10, credit = 0 }, new { accountCode = "311", debit = 0, credit = 10 } } });
        Assert.Equal(400, parent.Status);
        var ok = await api.Post("/journalentries", new { description = "رأس مال", lines = new[] { new { accountCode = "1111", debit = 1000, credit = 0 }, new { accountCode = "311", debit = 0, credit = 1000 } } });
        Assert.Equal(200, ok.Status);
        Assert.True(ok.Data!["isBalanced"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Journal_entries_can_be_edited_reversed_and_reversed_ones_are_locked()
    {
        var api = await NewTenantAsync();
        await api.Post("/accounts", new { code = "311", nameAr = "رأس المال", nameEn = "Cap", type = "equity", parentCode = "31", isDebitNature = false });
        object Lines(decimal amount) => new { description = "قيد", lines = new[] { new { accountCode = "1111", debit = amount, credit = 0m }, new { accountCode = "311", debit = 0m, credit = amount } } };
        var je = await api.Post("/journalentries", Lines(1000));
        var id = je.Data!["id"].S();

        var edited = await api.Put($"/journalentries/{id}", Lines(700));
        Assert.Equal(200, edited.Status);
        Assert.Equal(je.Data["entryNumber"].S(), edited.Data!["entryNumber"].S()); // نفس الرقم
        Assert.Equal(700, (await api.Get("/accounts/by-code/1111")).Data!["balance"].D());

        Assert.Equal(200, (await api.Post($"/journalentries/{id}/reverse")).Status);
        Assert.Equal(0, (await api.Get("/accounts/by-code/1111")).Data!["balance"].D());
        Assert.Equal(409, (await api.Post($"/journalentries/{id}/reverse")).Status);   // لا يُعكس مرتين
        Assert.Equal(409, (await api.Delete($"/journalentries/{id}")).Status);          // المعكوس مقفل
    }

    [Fact]
    public async Task Automatic_journals_can_be_deleted_or_reversed_but_not_twice()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var inv = await api.Post("/invoices", Sale(product, 1, "cash"));
        var jeId = inv.Data!["journalEntryId"].S();
        Assert.Equal(200, (await api.Post($"/journalentries/{jeId}/reverse")).Status);
        Assert.Equal(409, (await api.Post($"/journalentries/{jeId}/reverse")).Status);
        var inv2 = await api.Post("/invoices", Sale(product, 1, "cash"));
        Assert.Equal(200, (await api.Delete($"/journalentries/{inv2.Data!["journalEntryId"].S()}")).Status);
        Assert.Null((await api.Get($"/invoices/{inv2.Data["id"].S()}")).Data!["journalEntryId"]);
        var (d, c) = Totals(await api.Get("/reports/trial-balance")); Assert.Equal(d, c);
    }

    // ---------- الفواتير والمخزون والترحيل ----------
    [Fact]
    public async Task Cash_sale_computes_totals_on_the_server_posts_stock_cost_and_qr()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var inv = await api.Post("/invoices", new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", grandTotal = 1, vatTotal = 999,
            items = new[] { new { itemId = product, quantity = 2, unitPrice = 100, vatRate = 15 } } }); // أرقام العميل تُتجاهل
        Assert.Equal(201, inv.Status);
        Assert.Equal(230, inv.Data!["grandTotal"].D()); Assert.Equal(30, inv.Data["vatTotal"].D());
        Assert.Equal(80, inv.Data["totalCost"].D()); Assert.Equal(120, inv.Data["grossProfit"].D());
        Assert.Equal("posted", inv.Data["status"].S());
        Assert.False(string.IsNullOrEmpty(inv.Data["zatcaQrCode"].S()));
        Assert.Equal(8, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
    }

    [Fact]
    public async Task Sale_is_blocked_when_stock_is_insufficient_and_nothing_is_saved()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 3);
        var r = await api.Post("/invoices", Sale(product, 5, "cash"));
        Assert.Equal(409, r.Status);
        Assert.Equal(0, (await api.Get("/invoices")).Data!["totalCount"].D());          // لا فاتورة يتيمة
        Assert.Equal(3, (await api.Get($"/products/{product}")).Data!["currentStock"].D()); // ولا خصم مخزون
    }

    [Fact]
    public async Task Credit_sale_respects_credit_limit_and_updates_customer_balance()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var (customer, _) = await SeedCustomerAsync(api, creditLimit: 500);
        Assert.Equal(409, (await api.Post("/invoices", Sale(product, 5, "credit", customer))).Status);
        Assert.Equal(400, (await api.Post("/invoices", Sale(product, 1, "credit", null))).Status); // آجل بدون عميل
        Assert.Equal(201, (await api.Post("/invoices", Sale(product, 3, "credit", customer))).Status);
        Assert.Equal(345, (await api.Get($"/customers/{customer}")).Data!["currentBalance"].D());
    }

    [Fact]
    public async Task Receipt_voucher_reduces_customer_balance_and_returns_amount_in_arabic_words()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var (customer, account) = await SeedCustomerAsync(api);
        await api.Post("/invoices", Sale(product, 3, "credit", customer));
        var v = await api.Post("/vouchers", new { type = "receipt", amount = 100, partyName = "عميل", partyAccountCode = account, treasuryAccountCode = "1111", paymentMethod = "cash" });
        Assert.Equal(200, v.Status);
        Assert.StartsWith("RV-", v.Data!["voucherNumber"].S());
        Assert.Contains("مائة", v.Data["amountInWordsAr"].S());
        Assert.Equal(245, (await api.Get($"/customers/{customer}")).Data!["currentBalance"].D());
        // حذف السند يعكس قيده
        Assert.Equal(200, (await api.Delete($"/vouchers/{v.Data["id"].S()}")).Status);
        Assert.Equal(345, (await api.Get($"/customers/{customer}")).Data!["currentBalance"].D());
    }

    [Fact]
    public async Task Sales_return_restocks_and_cannot_exceed_the_sold_quantity()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var inv = await api.Post("/invoices", Sale(product, 2, "cash"));
        var id = inv.Data!["id"].S();

        Assert.Equal(400, (await api.Post("/invoices/returns", new { originalInvoiceId = id, returnReason = "x", lines = new[] { new { itemId = product, quantity = 5 } } })).Status);
        var ret = await api.Post("/invoices/returns", new { originalInvoiceId = id, returnReason = "عيب", lines = new[] { new { itemId = product, quantity = 1 } } });
        Assert.Equal(200, ret.Status);
        Assert.Equal(115, ret.Data!["grandTotal"].D()); Assert.True(ret.Data["isReturn"]!.GetValue<bool>());
        Assert.Equal(9, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        // المرتجعات السابقة تُحتسب: الباقي 1 فقط
        Assert.Equal(400, (await api.Post("/invoices/returns", new { originalInvoiceId = id, returnReason = "x", lines = new[] { new { itemId = product, quantity = 2 } } })).Status);
    }

    [Fact]
    public async Task Purchase_updates_moving_average_cost_and_supplier_balance()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 6, cost: 40);
        var (supplier, _) = await SeedSupplierAsync(api);
        var pur = await api.Post("/invoices", new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier,
            items = new[] { new { itemId = product, quantity = 4, unitPrice = 50, vatRate = 15 } } });
        Assert.Equal(201, pur.Status); Assert.Equal(230, pur.Data!["grandTotal"].D());
        var p = (await api.Get($"/products/{product}")).Data!;
        Assert.Equal(10, p["currentStock"].D()); Assert.Equal(44, p["averageCost"].D()); // (6*40+4*50)/10
        Assert.Equal(230, (await api.Get($"/suppliers/{supplier}")).Data!["currentBalance"].D());
    }

    [Fact]
    public async Task Costing_policy_switch_to_last_purchase_changes_cost_of_sales()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        await api.Put("/costing/policy", new { method = "last_purchase", recalculateOnNewPurchase = true, includeFreightAndCustoms = false, negativeInventoryPolicy = "prohibit", standardCostVarianceAccountCode = "513" });
        await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 5, unitCost = 70, referenceNumber = "X" });
        Assert.Equal(70, (await api.Get($"/products/{product}")).Data!["averageCost"].D());
        var inv = await api.Post("/invoices", Sale(product, 1, "cash"));
        Assert.Equal(70, inv.Data!["totalCost"].D());
    }


    [Fact]
    public async Task Fifo_consumes_the_oldest_layers_first_and_recalculation_matches_history()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        await api.Put("/costing/policy", new { method = "fifo", recalculateOnNewPurchase = true, includeFreightAndCustoms = false, negativeInventoryPolicy = "prohibit", standardCostVarianceAccountCode = "513" });
        await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 10, unitCost = 60, referenceNumber = "L2" });

        var inv = await api.Post("/invoices", Sale(product, 12, "cash"));
        Assert.Equal(201, inv.Status);
        Assert.Equal(520, inv.Data!["totalCost"].D()); // 10×40 + 2×60
        var after = (await api.Get($"/products/{product}")).Data!;
        Assert.Equal(8, after["currentStock"].D()); Assert.Equal(60, after["averageCost"].D()); // باقي الدفعة الثانية فقط

        var re = await api.Post("/costing/recalculate-all");
        Assert.Equal(1, re.Data!["productsUpdated"].D());
        Assert.Equal(8, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
    }

    [Fact]
    public async Task Draft_invoice_has_no_financial_effect_until_posted_and_posted_delete_reverses_it()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var draft = await api.Post("/invoices", new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "draft",
            items = new[] { new { itemId = product, quantity = 2, unitPrice = 100, vatRate = 15 } } });
        Assert.Equal("draft", draft.Data!["status"].S());
        Assert.Equal(10, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        var id = draft.Data["id"].S();
        var posted = await api.Post($"/invoices/{id}/post");
        Assert.Equal("posted", posted.Data!["status"].S());
        Assert.Equal(8, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        Assert.Equal(409, (await api.Post($"/invoices/{id}/post")).Status); // لا يُرحَّل مرتين
        Assert.Equal(200, (await api.Delete($"/invoices/{id}")).Status); // الحذف يعكس القيد والمخزون
        Assert.Equal(10, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        Assert.Equal(404, (await api.Get($"/invoices/{id}")).Status);
    }

    [Fact]
    public async Task Standard_tax_invoice_requires_the_buyer_vat_number()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var r = await api.Post("/invoices", new { kind = "sales", invoiceType = "tax_invoice", paymentMethod = "cash", items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } } });
        Assert.Equal(400, r.Status);
    }

    // ---------- المستندات التجارية ----------
    [Fact]
    public async Task Quotation_converts_once_to_a_draft_invoice_that_can_then_be_posted()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var q = await api.Post("/quotations", new { type = "sales_quotation", partyName = "عميل", date = DateTime.UtcNow, validUntil = DateTime.UtcNow.AddDays(10), status = "draft",
            items = new[] { new { itemId = product, itemName = "منتج", sku = "P1", unit = "PCS", quantity = 1, unitPrice = 100, vatRate = 15, discount = 0 } } });
        Assert.Equal(115, q.Data!["grandTotal"].D());
        var conv = await api.Post($"/quotations/{q.Data["id"].S()}/convert-to-invoice");
        Assert.Equal(200, conv.Status); Assert.Equal("draft", conv.Data!["status"].S());
        Assert.Equal(409, (await api.Post($"/quotations/{q.Data["id"].S()}/convert-to-invoice")).Status);
        Assert.Equal("posted", (await api.Post($"/invoices/{conv.Data["id"].S()}/post")).Data!["status"].S());
    }

    [Fact]
    public async Task Requisition_needs_approval_before_conversion_to_a_purchase_invoice()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 0);
        var (supplier, _) = await SeedSupplierAsync(api);
        var req = await api.Post("/materialrequisitions", new { requestDate = DateTime.UtcNow, requiredDate = DateTime.UtcNow.AddDays(5), department = "المشتريات", requestedBy = "أحمد", priority = "high", supplierId = supplier, supplierName = "مورد", status = "draft",
            items = new[] { new { itemId = product, itemName = "منتج", sku = "P1", unit = "PCS", requestedQuantity = 5, estimatedCost = 30 } } });
        var id = req.Data!["id"].S();
        Assert.Equal(409, (await api.Post($"/materialrequisitions/{id}/convert-to-invoice")).Status);
        Assert.Equal(200, (await api.Post($"/materialrequisitions/{id}/approve")).Status);
        var conv = await api.Post($"/materialrequisitions/{id}/convert-to-invoice");
        Assert.Equal(200, conv.Status); Assert.Equal("draft", conv.Data!["status"].S());
        Assert.Equal(150, conv.Data["subtotal"].D());
    }

    [Fact]
    public async Task Commercial_contract_follows_stages_and_billing_posts_invoice_and_updates_totals()
    {
        var api = await NewTenantAsync();
        var (customer, _) = await SeedCustomerAsync(api);
        var ct = await api.Post("/commercialcontracts", new { title = "عقد صيانة", contractType = "maintenance", partyName = "عميل", partyId = customer, contractValue = 1000, vatRate = 15,
            milestones = new[] { new { title = "دفعة 1", percentage = 50 }, new { title = "دفعة 2", percentage = 50 } }, clauses = Array.Empty<object>() });
        Assert.Equal(1150, ct.Data!["totalValueWithVat"].D());
        var id = ct.Data["id"].S(); var m1 = ct.Data["milestones"]![0]!["id"].S();
        Assert.Equal(500, ct.Data["milestones"]![0]!["amount"].D());

        Assert.Equal(409, (await api.Post($"/commercialcontracts/{id}/milestones/{m1}/bill")).Status); // قبل التنفيذ
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{id}/advance-stage", new { stage = "final_closed" })).Status); // تخطي مراحل
        foreach (var s in new[] { "legal_review", "approved_signed", "active_execution" })
            Assert.Equal(200, (await api.Post($"/commercialcontracts/{id}/advance-stage", new { stage = s })).Status);

        var bill = await api.Post($"/commercialcontracts/{id}/milestones/{m1}/bill");
        Assert.Equal(200, bill.Status); Assert.Equal(575, bill.Data!["grandTotal"].D());
        var after = (await api.Get($"/commercialcontracts/{id}")).Data!;
        Assert.Equal(575, after["totalInvoiced"].D()); Assert.Equal(575, after["remainingBalance"].D());
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{id}/milestones/{m1}/bill")).Status);
        Assert.Equal(575, (await api.Get($"/customers/{customer}")).Data!["currentBalance"].D());
        // إغلاق العقد ممنوع وفيه مستخلص غير مفوتر
        await api.Post($"/commercialcontracts/{id}/advance-stage", new { stage = "milestone_billing" });
        await api.Post($"/commercialcontracts/{id}/advance-stage", new { stage = "initial_inspection" });
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{id}/advance-stage", new { stage = "final_closed" })).Status);
    }

    [Fact]
    public async Task Delivery_return_cannot_exceed_delivered_quantity_and_updates_status()
    {
        var api = await NewTenantAsync();
        var note = await api.Post("/deliverynotes", new { type = "sales_delivery", partyName = "عميل", date = DateTime.UtcNow,
            items = new[] { new { itemId = Guid.NewGuid(), itemName = "بند", contractQty = 10, deliveredQty = 10 } } });
        Assert.Equal(200, note.Status);
        var id = note.Data!["id"].S(); var item = note.Data["items"]![0]!["itemId"].S();
        Assert.Equal(400, (await api.Post("/deliverynotes/returns", new { type = "sales_delivery_return", deliveryNoteId = id, items = new[] { new { itemId = item, quantity = 11 } } })).Status);
        Assert.Equal(200, (await api.Post("/deliverynotes/returns", new { type = "sales_delivery_return", deliveryNoteId = id, items = new[] { new { itemId = item, quantity = 4 } } })).Status);
        Assert.Equal("partially_returned", (await api.Get($"/deliverynotes/{id}")).Data!["status"].S());
    }

    // ---------- التقارير ----------
    [Fact]
    public async Task Reports_stay_balanced_after_a_mix_of_documents()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var (customer, account) = await SeedCustomerAsync(api);
        var (supplier, _) = await SeedSupplierAsync(api);
        await api.Post("/invoices", Sale(product, 2, "cash"));
        await api.Post("/invoices", Sale(product, 3, "credit", customer));
        await api.Post("/vouchers", new { type = "receipt", amount = 100, partyName = "عميل", partyAccountCode = account, treasuryAccountCode = "1111", paymentMethod = "cash" });
        await api.Post("/invoices", new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, items = new[] { new { itemId = product, quantity = 4, unitPrice = 50, vatRate = 15 } } });

        var (d, c) = Totals(await api.Get("/reports/trial-balance"));
        Assert.True(d > 0); Assert.Equal(d, c);
        Assert.True((await api.Get("/reports/financial-summary")).Data!["isBalanceSheetBalanced"]!.GetValue<bool>());

        var stats = (await api.Get("/reports/financial-stats")).Data!;
        Assert.Equal(500, stats["totalSales"].D()); Assert.Equal(200, stats["totalPurchases"].D()); Assert.Equal(100, stats["totalReceipts"].D());
        var vat = (await api.Get("/reports/vat-return?from=2000-01-01&to=2100-01-01")).Data!;
        Assert.Equal(75, vat["outputVat"].D()); Assert.Equal(30, vat["inputVat"].D()); Assert.Equal(45, vat["netVatPayable"].D());
        var st = (await api.Get($"/reports/account-statement/{account}")).Data!;
        Assert.Equal(245, st["closingBalance"].D()); Assert.Equal(2, st["entries"]!.AsArray().Count);
        var ledger = (await api.Get($"/reports/item-ledger?itemId={product}")).Data!.AsArray();
        Assert.Equal(10 - 2 - 3 + 4, ledger[^1]!["runningStockBalance"].D());
    }
}
