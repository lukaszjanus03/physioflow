namespace PhysioFlow.Application.DTOs;

public record AppointmentStatsDto(int Scheduled, int Completed, int Cancelled, int NoShow)
{
    public int Total => Scheduled + Completed + Cancelled + NoShow;
}

public record DashboardStatsDto(DateOnly From, DateOnly To, string TimeZone,
    int ActivePatientCount, AppointmentStatsDto Appointments);

public record DailyPainPointDto(DateOnly Date, double? AveragePainLevel, int RatingCount);

public record PatientPainTrendDto(DateOnly From, DateOnly To, string TimeZone,
    IReadOnlyList<DailyPainPointDto> Points);
