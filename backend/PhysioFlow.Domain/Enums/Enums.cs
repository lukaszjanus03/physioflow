namespace PhysioFlow.Domain.Enums;

public enum UserRole
{
    Therapist = 1,
    Patient = 2,
    Admin = 3
}

public enum AppointmentStatus
{
    Scheduled = 1,
    Completed = 2,
    Cancelled = 3,
    NoShow = 4
}

public enum AppointmentDuration
{
    Thirty = 30,
    FortyFive = 45,
    Sixty = 60
}

public enum PainPointSeverity
{
    AcutePain = 1,    // czerwony
    ChronicPain = 2,  // pomarańczowy
    Tension = 3       // żółty
}

public enum BodySide
{
    Front = 1,
    Back = 2
}

public enum MuscleGroup
{
    Neck = 1,
    Shoulders = 2,
    Back = 3,
    Chest = 4,
    Arms = 5,
    Core = 6,
    Glutes = 7,
    Legs = 8,
    FullBody = 9
}

public enum AttachmentType
{
    Photo = 1,
    Pdf = 2,
    XRay = 3,
    Video = 4,
    Other = 5
}
