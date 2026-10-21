using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// إرسال مستندات المعارض للهيئة أمام البديل المحلي: فاتورة عقد البيع وإشعاره الدائن يُنشآن داخل معاملة العقد فيُرسلان بعد اكتمالها،
/// والمرتجع يتبع فاتورته الأصلية في الإرسال لا إعداد المنشأة.
/// </summary>
[Collection("api")]
public class CarShowroomZatcaTests : TestBase
{
    public CarShowroomZatcaTests(ErpFactory f) : base(f) { }

    private async Task<Client> OnboardedAsync(bool autoSubmit)
    {
        var api = await NewTenantAsync();
        Assert.Equal(200, (await api.Put("/company/ZatcaConfig", new
        {
            isEnabled = false, environment = "sandbox", autoSubmitOnInvoiceSave = autoSubmit,
            street = "طريق الملك فهد", buildingNumber = "1234", district = "العليا", postalCode = "12345",
        })).Status);
        Assert.Equal(200, (await api.Post("/company/ZatcaOnboard", new { otp = FakeZatcaApiClient.Otp })).Status);
        return api;
    }

    private static async Task<string> SeedCarAsync(Client api)
    {
        var car = await api.Post("/vehicles", new
        {
            chassisNumber = Client.NewVin('Z'), brandNameAr = "تويوتا", modelNameAr = "يارس", year = 2024, condition = "new", purchasePrice = 40000, sellingPrice = 50000,
            vatMode = "standard_15", fuelType = "petrol", transmission = "automatic", colorExterior = "أبيض", colorInterior = "أسود", location = "المعرض",
        });
        Assert.Equal(201, car.Status);
        return car.Data!["id"].S();
    }

    private static Task<Res> QuickSaleAsync(Client api, string vehicleId) => api.Post("/carsalescontracts/QuickSale", new
    {
        contract = new
        {
            cycleType = "individual", buyerName = "فرد", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000",
            vehicleId, sellingPrice = 50000, vatMode = "standard_15", paymentMethod = "cash", condition = "new",
        },
    });

    private static string Icv(JsonNode invoice)
        => FakeZatcaApiClient.Between(invoice["zatcaUblXml"].S(), "<cbc:ID>ICV</cbc:ID><cbc:UUID>", "</cbc:UUID>");

    [Fact]
    public async Task Contract_invoice_and_its_credit_note_are_sent_once_the_contract_transaction_completes()
    {
        var api = await OnboardedAsync(autoSubmit: true);
        var sale = await QuickSaleAsync(api, await SeedCarAsync(api));
        Assert.Equal(201, sale.Status);
        var contractId = sale.Data!["id"].S();

        // فرد بلا رقم ضريبي: مبسطة تُبلَّغ
        var invoice = (await api.Get(await api.InvoicePath(sale.Data["invoiceId"].S()))).Data!;
        Assert.Equal("reported", invoice["zatcaStatus"].S());
        Assert.Equal("1", Icv(invoice));

        Assert.Equal(200, (await api.Post($"/carsalescontracts/{contractId}/return", new { reason = "عيب مصنعي" })).Status);
        var documents = (await api.Get($"/carsalescontracts/{contractId}/ZatcaDocuments")).Data!.AsArray();
        Assert.Equal(new[] { "sales", "sales_return" }, documents.Select(d => d!["kind"].S()));
        Assert.All(documents, d => Assert.Equal("reported", d!["zatcaStatus"].S()));
        Assert.Equal("2", Icv(documents[1]!)); // الإشعار الدائن يكمل سلسلة الفاتورة
    }

    [Fact]
    public async Task Without_auto_submit_the_contract_invoice_waits_for_a_manual_send_and_its_return_follows_it()
    {
        var api = await OnboardedAsync(autoSubmit: false);
        var sale = await QuickSaleAsync(api, await SeedCarAsync(api));
        var contractId = sale.Data!["id"].S();
        var invoiceId = sale.Data["invoiceId"].S();
        Assert.Equal("not_submitted", (await api.Get(await api.InvoicePath(invoiceId))).Data!["zatcaStatus"].S());

        // الإرسال اليدوي (زر «إرسال للهيئة»)
        var sent = await api.Post(await api.InvoicePath(invoiceId, "/SubmitZatca"));
        Assert.True(sent.Data!["success"]!.GetValue<bool>());

        // الأصل وصل الهيئة: إشعاره الدائن يُرسل رغم إيقاف الإرسال التلقائي
        Assert.Equal(200, (await api.Post($"/carsalescontracts/{contractId}/return", new { reason = "عيب مصنعي" })).Status);
        var creditNote = (await api.Get($"/carsalescontracts/{contractId}/ZatcaDocuments")).Data!.AsArray()[1]!;
        Assert.Equal("reported", creditNote["zatcaStatus"].S());
    }

    [Fact]
    public async Task Vehicle_invoice_return_follows_the_send_choice_of_its_original_invoice()
    {
        var api = await OnboardedAsync(autoSubmit: false);
        object Sale(string vehicleId, bool send) => new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted", sendToZatca = send,
            revenueAccountCode = "412", cogsAccountCode = "512", inventoryAccountCode = "1142",
            vehicleLines = new[] { new { vehicleId, unitPrice = 50000, discount = 0, vatMode = "standard_15" } },
        };

        var sent = await api.PostInvoice(Sale(await SeedCarAsync(api), send: true));
        Assert.Equal("reported", sent.Data!["zatcaStatus"].S());
        var sentReturn = await api.PostReturn(new { originalInvoiceId = sent.Data["id"].S(), returnReason = "عيب مصنعي" });
        Assert.Equal("reported", sentReturn.Data!["zatcaStatus"].S());

        var kept = await api.PostInvoice(Sale(await SeedCarAsync(api), send: false));
        Assert.Equal("not_submitted", kept.Data!["zatcaStatus"].S());
        var keptReturn = await api.PostReturn(new { originalInvoiceId = kept.Data["id"].S(), returnReason = "عيب مصنعي" });
        Assert.Equal("not_submitted", keptReturn.Data!["zatcaStatus"].S());
    }
}
