# Project22

I built this plain C#/.NET laboratory order intake validation service to accept one laboratory order as a JSON string and return an `Accepted` or `Rejected` result.

## Project Structure

- `Project22.slnx`: solution containing the production and test projects.
- `src/OrderIntake`: my plain .NET 10 class library.
	- `OrderIntakeService`: public JSON processing and validation service.
	- `LaboratoryOrder`: strongly typed accepted order model.
	- `OrderResult`: processing status, order, and validation errors.
	- `ValidationError`: field-level error details.
	- `OrderStatus` and `ErrorCodes`: assessment status and error enums.
- `tests/OrderIntake.Tests`: my xUnit tests for the service.

## Requirements Implemented

- `orderId`, `patientId`, and `specimenId` are required, nonblank, and limited to 20 characters.
- `specimenType` accepts `Blood`, `Urine`, `Tissue`, or `Saliva` case-insensitively and stores the canonical value.
- `priority` accepts `Routine` or `Urgent` case-insensitively and stores the canonical value.
- `collectionDate` requires the exact `yyyy-MM-dd` format, represents a valid calendar date, allows today, and rejects future dates.
- `requestedTests` is required, must contain at least one item, rejects empty or whitespace items, and detects duplicates case-insensitively.
- Unknown JSON fields are ignored.
- Malformed JSON and incompatible recognized field types return one `MALFORMED_INPUT` error.
- Accepted results contain a typed `LaboratoryOrder` with a `DateOnly` collection date.
- Rejected results contain validation errors and no order.

## Design

The public entry point is:

```csharp
OrderResult Process(string json)
```

I separated the implementation into:

1. JSON reading and parsing
2. Field validation
3. Validation-error collection
4. Strongly typed order construction
5. Result construction

I used plain C# objects and `System.Text.Json`. I did not use a dependency injection framework or a validation framework.

## Build and Test

Run these commands from the repository root:

```text
dotnet build
dotnet test
```

The current automated suite contains 30 tests, including the required assessment groups and additional edge cases. This count describes the current test suite and may change as I add or remove tests.

## Assumptions and Limitations

- I do not automatically trim input values. Whitespace-only required values are rejected, and surrounding whitespace is not removed from other values.
- I preserve the casing sent by the caller for requested test names.
- Error ordering is not treated as significant.
- Accepted collection dates use `DateOnly`.
- Unknown JSON fields are ignored.
- The service is in-memory only.

## AI Use

I used GitHub Copilot during development. Copilot initially generated implementation code with compile issues involving an invalid using-variable pattern and passing properties as `out` parameters. I reviewed the compiler errors, corrected the implementation, and verified it with `dotnet build` and `dotnet test`.

I reviewed and validated the generated implementation and tests rather than accepting AI output blindly.

## Out of Scope

- Database
- Authentication
- UI
- HTTP API
- Cloud services
- External APIs
- Queues
- Load testing
- Production logging
- Validation frameworks
