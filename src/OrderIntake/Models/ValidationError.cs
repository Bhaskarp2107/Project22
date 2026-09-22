using OrderIntake.Enums;

namespace OrderIntake.Models;

public sealed record ValidationError(
	string Field,
	ErrorCodes Code,
	string Message);
