using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// نطاق الحضور الميداني على ملف مشروع المقاولات: موقع المشروع ونصف قطر النطاق وأضعف دقة مقبولة وتفعيله. التفعيل يشترط
/// الموقع، والنطاق يُعدَّل في أي مرحلة غير مغلقة ولا ملغاة (يُحتاج أثناء التنفيذ). الحضور السابق يحفظ النطاق وقت تسجيله.
/// </summary>
public class ProjectGeofenceService : IProjectGeofenceService
{
    private readonly ErpDbContext _db;
    public ProjectGeofenceService(ErpDbContext db) => _db = db;

    public async Task<ProjectGeofenceDto> GetAsync(Guid contractId, CancellationToken ct = default)
    {
        await ConstructionProjectService.LoadBoqContractAsync(_db, contractId, ct);
        var profile = await _db.Set<ConstructionProjectProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.ContractId == contractId, ct);
        return ToDto(profile ?? new ConstructionProjectProfile { ContractId = contractId });
    }

    public async Task<ProjectGeofenceDto> SaveAsync(Guid contractId, ProjectGeofenceDto request, CancellationToken ct = default)
    {
        var contract = await ConstructionProjectService.LoadBoqContractAsync(_db, contractId, ct);
        ConstructionProjectService.EnsureEditable(contract);
        Validate(request);

        var profile = await _db.Set<ConstructionProjectProfile>().FirstOrDefaultAsync(p => p.ContractId == contractId, ct);
        if (profile == null)
        {
            profile = new ConstructionProjectProfile { ContractId = contractId };
            _db.Add(profile);
        }
        profile.SiteLatitude = request.Latitude is decimal lat ? Math.Round(lat, 6) : null;
        profile.SiteLongitude = request.Longitude is decimal lon ? Math.Round(lon, 6) : null;
        profile.GeofenceRadiusMeters = request.RadiusMeters;
        profile.MaxGpsAccuracyMeters = request.MaxAccuracyMeters;
        profile.GpsAttendanceEnabled = request.GpsAttendanceEnabled;
        if (request.OutOfRangePolicy != null) profile.OutOfRangePolicy = request.OutOfRangePolicy;
        await _db.SaveChangesAsync(ct);
        return ToDto(profile);
    }

    private static void Validate(ProjectGeofenceDto r)
    {
        var errors = new List<string>();
        if (r.Latitude.HasValue != r.Longitude.HasValue
            || (r.Latitude is decimal lat && r.Longitude is decimal lon && !GeoDistance.IsValidCoordinate(lat, lon)))
            errors.Add(Messages.ProjectGeofenceLocationInvalid);
        else if (r.GpsAttendanceEnabled && r.Latitude == null)
            errors.Add(Messages.ProjectGeofenceLocationRequired);
        if (r.RadiusMeters is < ProjectGeofence.MinRadiusMeters or > ProjectGeofence.MaxRadiusMeters)
            errors.Add(string.Format(Messages.ProjectGeofenceRadiusInvalid, ProjectGeofence.MinRadiusMeters, ProjectGeofence.MaxRadiusMeters));
        if (r.MaxAccuracyMeters is < ProjectGeofence.MinAccuracyMeters or > ProjectGeofence.MaxAccuracyMeters)
            errors.Add(string.Format(Messages.ProjectGeofenceAccuracyInvalid, ProjectGeofence.MinAccuracyMeters, ProjectGeofence.MaxAccuracyMeters));
        if (r.OutOfRangePolicy != null && !OutOfRangePolicies.IsValid(r.OutOfRangePolicy))
            errors.Add(Messages.ProjectGeofencePolicyInvalid);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
    }

    private static ProjectGeofenceDto ToDto(ConstructionProjectProfile p) => new()
    {
        Latitude = p.SiteLatitude, Longitude = p.SiteLongitude, RadiusMeters = p.GeofenceRadiusMeters,
        MaxAccuracyMeters = p.MaxGpsAccuracyMeters, GpsAttendanceEnabled = p.GpsAttendanceEnabled, OutOfRangePolicy = p.OutOfRangePolicy,
    };
}
