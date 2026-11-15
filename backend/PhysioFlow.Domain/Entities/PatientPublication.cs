namespace PhysioFlow.Domain.Entities;

/// <summary>Explicit grant to one account and one incarnation of its link to a patient card.</summary>
public interface IPatientPublication
{
    Guid? PublishedVersionId { get; set; }
    Guid? PublishedToUserId { get; set; }
    Guid? PublishedForLinkVersion { get; set; }
    DateTime? PublishedAt { get; set; }
}
