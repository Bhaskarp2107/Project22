using OrderIntake.Enums;

namespace OrderIntake.Models;

public sealed record OrderResult(
    OrderStatus Status,
    LaboratoryOrder? Order,
    IReadOnlyList<ValidationError> Errors);
