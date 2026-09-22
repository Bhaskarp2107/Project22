namespace OrderIntake.Models;

public sealed record LaboratoryOrder(
    string OrderId,
    string PatientId,
    string SpecimenId,
    string SpecimenType,
    string Priority,
    DateOnly CollectionDate,
    IReadOnlyList<string> RequestedTests);
