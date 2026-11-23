using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// ترقيم مراكز التكلفة الهرمي من الخادم: الرئيسي خانتان (01)، وابن الرئيسي خانتان بعد رمزه (11 ← 1101)، وما تحته أربع خانات
/// (1101 ← 11010001)، بأصفار بادئة، وبلا تكرار عند التزامن، وبلا إعادة رقم مركز محذوف، مع بقاء الرموز القديمة كما هي.
/// </summary>
[Collection("api")]
public class CostCenterNumberingTests : TestBase
{
    public CostCenterNumberingTests(ErpFactory f) : base(f) { }

    /// <summary>مركز بلا رمز: الخادم يولّده من الأب.</summary>
    private static async Task<(Guid Id, string Code)> AutoAsync(Client api, Guid? parentId = null, string name = "مركز")
    {
        var created = await api.Post("/costcenters", new { nameAr = name, nameEn = name, isActive = true, parentId });
        Assert.Equal(201, created.Status);
        return (created.Data!["id"].G(), created.Data["code"].S());
    }

    private static async Task<Guid> ManualAsync(Client api, string code, Guid? parentId = null)
    {
        var created = await api.Post("/costcenters", new { code, nameAr = "مركز " + code, nameEn = code, isActive = true, parentId });
        Assert.Equal(201, created.Status);
        return created.Data!["id"].G();
    }

    [Fact]
    public async Task Codes_follow_the_parent_with_two_then_four_digit_sequences_and_keep_leading_zeros()
    {
        var api = await NewTenantAsync();

        var (root1, c1) = await AutoAsync(api);
        var (_, c2) = await AutoAsync(api);
        Assert.Equal("01", c1); Assert.Equal("02", c2);

        var (sub1, s1) = await AutoAsync(api, root1);
        var (_, s2) = await AutoAsync(api, root1);
        Assert.Equal("0101", s1); Assert.Equal("0102", s2);

        var (_, g1) = await AutoAsync(api, sub1);
        var (deep, g2) = await AutoAsync(api, sub1);
        Assert.Equal("01010001", g1); Assert.Equal("01010002", g2);
        Assert.Equal("010100020001", (await AutoAsync(api, deep)).Code); // ما بعد المستوى الثاني أربع خانات أيضاً

        // أمثلة المواصفة: الأب 11 ← 1101، 1102؛ والأب 1101 ← 11010001، 11010002
        var eleven = await ManualAsync(api, "11");
        var (s1101, a) = await AutoAsync(api, eleven);
        var (_, b) = await AutoAsync(api, eleven);
        Assert.Equal("1101", a); Assert.Equal("1102", b);
        Assert.Equal("11010001", (await AutoAsync(api, s1101)).Code);
        Assert.Equal("11010002", (await AutoAsync(api, s1101)).Code);
    }

    [Fact]
    public async Task New_centers_are_saved_directly_under_the_selected_parent()
    {
        var api = await NewTenantAsync();
        var (root, rootCode) = await AutoAsync(api);
        var (other, _) = await AutoAsync(api);
        var (child, childCode) = await AutoAsync(api, root);

        var saved = (await api.Get($"/costcenters/{child}")).Data!;
        Assert.Equal(root, saved["parentId"].G());
        Assert.StartsWith(rootCode, childCode);

        // فرع تحت المركز الآخر لا يختلط بتسلسل الأول
        var (_, otherChild) = await AutoAsync(api, other);
        Assert.Equal("0201", otherChild);

        var all = (await api.Get("/costcenters?pageSize=100")).Data!["items"]!.AsArray();
        Assert.Equal(4, all.Count);
        Assert.Equal(2, all.Count(c => c!["parentId"] == null));
    }

    [Fact]
    public async Task Concurrent_creation_under_the_same_parent_never_duplicates_a_code()
    {
        var api = await NewTenantAsync();
        var (root, _) = await AutoAsync(api);

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i =>
            api.Post("/costcenters", new { nameAr = "فرع " + i, nameEn = "B" + i, isActive = true, parentId = root })));

        Assert.All(results, r => Assert.Equal(201, r.Status));
        var codes = results.Select(r => r.Data!["code"].S()).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 12).Select(n => $"01{n:00}").ToHashSet(), codes.ToHashSet());
    }

    [Fact]
    public async Task Deleted_codes_are_not_reused_and_manual_codes_are_skipped()
    {
        var api = await NewTenantAsync();
        var (root, _) = await AutoAsync(api);
        await AutoAsync(api, root);                       // 0101
        var (second, code) = await AutoAsync(api, root);  // 0102
        Assert.Equal("0102", code);
        Assert.Equal(200, (await api.Delete($"/costcenters/{second}")).Status);
        Assert.Equal("0103", (await AutoAsync(api, root)).Code);

        // رمز يدوي سبق العدّاد يُتخطّى
        await ManualAsync(api, "0107", root);
        Assert.Equal("0108", (await AutoAsync(api, root)).Code);
        Assert.Equal(409, (await api.Post("/costcenters", new { code = "0108", nameAr = "مكرر", nameEn = "dup", isActive = true })).Status);
    }

    [Fact]
    public async Task Existing_free_text_codes_stay_unchanged_and_editing_without_a_code_keeps_it()
    {
        var api = await NewTenantAsync();
        var legacy = await ManualAsync(api, "CC-001");
        var (child, childCode) = await AutoAsync(api, legacy);
        Assert.Equal("CC-00101", childCode); // الرمز القديم بادئة كما هو
        Assert.Equal("CC-001", (await api.Get($"/costcenters/{legacy}")).Data!["code"].S());

        // تعديل الاسم بلا رمز يبقي الرمز والأب
        var edited = await api.Put($"/costcenters/{child}", new { nameAr = "اسم جديد", nameEn = "New", isActive = true, parentId = legacy });
        Assert.Equal(200, edited.Status);
        Assert.Equal(childCode, edited.Data!["code"].S());
        Assert.Equal("اسم جديد", edited.Data["nameAr"].S());

        Assert.Equal(400, (await api.Post("/costcenters", new { nameAr = "", nameEn = "x", isActive = true })).Status);
    }

    [Fact]
    public async Task Centers_in_use_or_with_sub_centers_are_protected_from_deletion()
    {
        var api = await NewTenantAsync();
        var (root, _) = await AutoAsync(api);
        var (child, _) = await AutoAsync(api, root);
        Assert.Equal(409, (await api.Delete($"/costcenters/{root}")).Status); // له مراكز فرعية

        var entry = await api.Post("/journalentries", new
        {
            date = DateTime.UtcNow, description = "قيد",
            lines = new object[] { new { accountCode = "521", debit = 10, credit = 0, costCenterId = child }, new { accountCode = "1111", debit = 0, credit = 10 } },
        });
        Assert.Equal(200, entry.Status);
        Assert.Equal(409, (await api.Delete($"/costcenters/{child}")).Status); // عليه قيود: يُوقف بدلاً من الحذف
        Assert.Equal(200, (await api.Get($"/costcenters/{child}")).Status);
    }

    [Fact]
    public async Task Users_without_the_accounts_screen_cannot_view_or_change_cost_centers()
    {
        var owner = await NewTenantAsync();
        var (root, _) = await AutoAsync(owner);
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "rep", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var rep = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");

        Assert.Equal(403, (await rep.Post("/costcenters", new { nameAr = "x", nameEn = "x", isActive = true })).Status);
        Assert.Equal(403, (await rep.Put($"/costcenters/{root}", new { nameAr = "x", nameEn = "x", isActive = true })).Status);
        Assert.Equal(403, (await rep.Delete($"/costcenters/{root}")).Status);
    }
}
