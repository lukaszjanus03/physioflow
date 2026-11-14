using FluentValidation;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.MedicalRecords;

public class MedicalRecordContentValidator : AbstractValidator<MedicalRecordContent>
{
    public MedicalRecordContentValidator(bool requireComplete = false)
    {
        RuleFor(x => x.Interview).MaximumLength(MedicalRecord.ClinicalTextMaxLength).OverridePropertyName("interview");
        RuleFor(x => x.FunctionalAssessment).MaximumLength(MedicalRecord.ClinicalTextMaxLength).OverridePropertyName("functionalAssessment");
        RuleFor(x => x.PerformedActions).MaximumLength(MedicalRecord.ClinicalTextMaxLength).OverridePropertyName("performedActions");
        RuleFor(x => x.Recommendations).MaximumLength(MedicalRecord.ClinicalTextMaxLength).OverridePropertyName("recommendations");
        RuleFor(x => x.PatientSummary).MaximumLength(MedicalRecord.PatientSummaryMaxLength).OverridePropertyName("patientSummary");
        if (requireComplete)
        {
            RuleFor(x => x.Interview).NotEmpty().OverridePropertyName("interview");
            RuleFor(x => x.FunctionalAssessment).NotEmpty().OverridePropertyName("functionalAssessment");
            RuleFor(x => x.PerformedActions).NotEmpty().OverridePropertyName("performedActions");
            RuleFor(x => x.Recommendations).NotEmpty().OverridePropertyName("recommendations");
        }
    }
}

public class CreateMedicalRecordValidator : AbstractValidator<CreateMedicalRecordCommand>
{
    public CreateMedicalRecordValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty().OverridePropertyName("patientId");
        RuleFor(x => x.Body.AppointmentId).NotEmpty().OverridePropertyName("appointmentId");
        RuleFor(x => x.Body).SetValidator(new MedicalRecordContentValidator()).OverridePropertyName(string.Empty);
    }
}

public class SaveMedicalRecordValidator : AbstractValidator<SaveMedicalRecordCommand>
{
    public SaveMedicalRecordValidator()
    {
        RuleFor(x => x.Body.Version).NotEmpty().OverridePropertyName("version");
        RuleFor(x => x.Body).SetValidator(new MedicalRecordContentValidator()).OverridePropertyName(string.Empty);
    }
}

public class ApproveMedicalRecordValidator : AbstractValidator<ApproveMedicalRecordCommand>
{
    public ApproveMedicalRecordValidator() => RuleFor(x => x.Version).NotEmpty().OverridePropertyName("version");
}

public class CorrectMedicalRecordValidator : AbstractValidator<CorrectMedicalRecordCommand>
{
    public CorrectMedicalRecordValidator()
    {
        RuleFor(x => x.Body.Version).NotEmpty().OverridePropertyName("version");
        RuleFor(x => x.Body).SetValidator(new MedicalRecordContentValidator(requireComplete: true)).OverridePropertyName(string.Empty);
        RuleFor(x => x.Body.CorrectionReason).NotEmpty()
            .MaximumLength(MedicalRecord.CorrectionReasonMaxLength).OverridePropertyName("correctionReason");
    }
}

public class ListMedicalRecordsValidator : AbstractValidator<ListMedicalRecordsQuery>
{
    public ListMedicalRecordsValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).OverridePropertyName("pageSize");
        RuleFor(x => x.PageNumber).GreaterThan(0)
            .Must((q, page) => ((long)page - 1) * q.PageSize <= int.MaxValue).OverridePropertyName("pageNumber");
    }
}
