using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// وثيقة Swagger تُولَّد فعلاً (بيئة التطوير): توليدها يمرّ على كل نقاط الـ API، فيكشف تعارض مولّد Swagger مع إصدار .NET
/// أو نقطة لا يستطيع وصفها، وهو ما لا تكشفه بقية الاختبارات لأنها لا تطلب الوثيقة.
/// </summary>
[Collection("api")]
public class SwaggerDocumentTests : TestBase
{
    public SwaggerDocumentTests(ErpFactory f) : base(f) { }

    [Fact]
    public async Task Swagger_document_is_generated_for_every_endpoint()
    {
        var res = await NewHttp().GetAsync("/swagger/v1/swagger.json");
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {body[..Math.Min(body.Length, 2000)]}");

        var paths = JsonNode.Parse(body)!["paths"]!.AsObject();
        Assert.Contains("/api/v1/reports/AccountStatement/{code}/Entries", paths.Select(p => p.Key));
        Assert.Contains("/api/v1/reports/DashboardSummary", paths.Select(p => p.Key));
        Assert.Contains("/api/v1/costing/policy", paths.Select(p => p.Key));
    }
}
