using DevExtreme.AspNet.Data;

namespace ERP.Core.DTOs.Shared;

/// <summary>
/// خيارات التحميل القياسية لـ DevExtreme (filter, sort, skip, take, group, totalSummary, groupSummary, select,
/// requireTotalCount, ...) كما ترسلها DataSource/CustomStore في الواجهة. تُربَط من الـ query string في طبقة الـ API.
/// </summary>
public class DataSourceLoadOptions : DataSourceLoadOptionsBase
{
}
