namespace ERP.Core.Contracts.CarShowroom;

/// <summary>هل يرى المستخدم الحالي تكلفة السيارات وربحها (صلاحية car-cost-view)؟ تُحسب مرة واحدة لكل طلب.</summary>
public interface ICostVisibility
{
    Task<bool> CanViewAsync(CancellationToken ct = default);
}
