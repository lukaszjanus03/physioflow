using PhysioFlow.Domain.Common;

namespace PhysioFlow.Domain.Entities;

public class AnatomicalRegionInfo : BaseEntity
{
    public string RegionId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Function { get; set; } = string.Empty;

    public string[] CommonIssues { get; set; } = Array.Empty<string>();

    public string[] ExaminationTips { get; set; } = Array.Empty<string>();
}