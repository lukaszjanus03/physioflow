namespace PhysioFlow.Domain.Enums;

public enum ReviewStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

public enum ReviewChangeAction
{
    Created = 1,
    Edited = 2,
    Approved = 3,
    Rejected = 4,
    Withdrawn = 5,
    Deleted = 6
}
