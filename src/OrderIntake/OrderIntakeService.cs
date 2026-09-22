using System.Globalization;
using System.Text.Json;
using OrderIntake.Enums;
using OrderIntake.Models;

namespace OrderIntake;

public sealed class OrderIntakeService
{
    public OrderResult Process(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return MalformedResult("Input must contain one JSON object.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return ProcessDocument(document);
        }
        catch (JsonException)
        {
            return MalformedResult("Input is not valid JSON.");
        }
    }

    private static OrderResult ProcessDocument(JsonDocument document)
    {
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return MalformedResult("The JSON top-level value must be an object.");
        }

        if (!TryReadFields(document.RootElement, out var fields))
        {
            return MalformedResult("A recognized field has an incompatible JSON type.");
        }

        var errors = new List<ValidationError>();
        var orderId = ValidateRequiredText(fields.OrderId, "orderId", errors);
        var patientId = ValidateRequiredText(fields.PatientId, "patientId", errors);
        var specimenId = ValidateRequiredText(fields.SpecimenId, "specimenId", errors);
        var specimenType = ValidateChoice(
            fields.SpecimenType,
            "specimenType",
            ["Blood", "Urine", "Tissue", "Saliva"],
            errors);
        var priority = ValidateChoice(
            fields.Priority,
            "priority",
            ["Routine", "Urgent"],
            errors);
        var collectionDate = ValidateCollectionDate(fields.CollectionDate, errors);
        var requestedTests = ValidateRequestedTests(fields.RequestedTests, errors);

        if (errors.Count > 0)
        {
            return new(OrderStatus.Rejected, null, errors);
        }

        var order = new LaboratoryOrder(
            orderId!,
            patientId!,
            specimenId!,
            specimenType!,
            priority!,
            collectionDate!.Value,
            requestedTests!);

        return new(OrderStatus.Accepted, order, []);
    }

    private static bool TryReadFields(JsonElement root, out JsonFields fields)
    {
        fields = new JsonFields();

        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "orderId":
                    if (!TryReadNullableString(property.Value, out var orderId)) return false;
                    fields.OrderId = orderId;
                    break;
                case "patientId":
                    if (!TryReadNullableString(property.Value, out var patientId)) return false;
                    fields.PatientId = patientId;
                    break;
                case "specimenId":
                    if (!TryReadNullableString(property.Value, out var specimenId)) return false;
                    fields.SpecimenId = specimenId;
                    break;
                case "specimenType":
                    if (!TryReadNullableString(property.Value, out var specimenType)) return false;
                    fields.SpecimenType = specimenType;
                    break;
                case "priority":
                    if (!TryReadNullableString(property.Value, out var priority)) return false;
                    fields.Priority = priority;
                    break;
                case "collectionDate":
                    if (!TryReadNullableString(property.Value, out var collectionDate)) return false;
                    fields.CollectionDate = collectionDate;
                    break;
                case "requestedTests":
                    if (!TryReadRequestedTests(property.Value, out var requestedTests)) return false;
                    fields.RequestedTests = requestedTests;
                    break;
            }
        }

        return true;
    }

    private static bool TryReadNullableString(JsonElement value, out string? result)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            result = null;
            return true;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            result = null;
            return false;
        }

        result = value.GetString();
        return true;
    }

    private static bool TryReadRequestedTests(JsonElement value, out IReadOnlyList<string>? result)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            result = null;
            return true;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            result = null;
            return false;
        }

        var tests = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                result = null;
                return false;
            }

            tests.Add(item.GetString()!);
        }

        result = tests;
        return true;
    }

    private static string? ValidateRequiredText(
        string? value,
        string field,
        ICollection<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(field, ErrorCodes.REQUIRED, $"{field} is required."));
        }

        if (value is not null && value.Length > 20)
        {
            errors.Add(new(field, ErrorCodes.MAX_LENGTH, $"{field} must be 20 characters or fewer."));
        }

        return value;
    }

    private static string? ValidateChoice(
        string? value,
        string field,
        IReadOnlyList<string> allowedValues,
        ICollection<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(field, ErrorCodes.REQUIRED, $"{field} is required."));
            return value;
        }

        var canonicalValue = allowedValues.FirstOrDefault(
            allowedValue => string.Equals(allowedValue, value, StringComparison.OrdinalIgnoreCase));

        if (canonicalValue is null)
        {
            errors.Add(new(field, ErrorCodes.INVALID_VALUE, $"{field} has an invalid value."));
        }

        return canonicalValue;
    }

    private static DateOnly? ValidateCollectionDate(
        string? value,
        ICollection<ValidationError> errors)
    {
        const string field = "collectionDate";

        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(field, ErrorCodes.REQUIRED, "collectionDate is required."));
            return null;
        }

        if (!DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            errors.Add(new(field, ErrorCodes.INVALID_FORMAT, "collectionDate must use yyyy-MM-dd format."));
            return null;
        }

        if (date > DateOnly.FromDateTime(DateTime.Today))
        {
            errors.Add(new(field, ErrorCodes.FUTURE_DATE, "collectionDate cannot be in the future."));
        }

        return date;
    }

    private static IReadOnlyList<string>? ValidateRequestedTests(
        IReadOnlyList<string>? values,
        ICollection<ValidationError> errors)
    {
        const string field = "requestedTests";

        if (values is null || values.Count == 0)
        {
            errors.Add(new(field, ErrorCodes.REQUIRED, "At least one requested test is required."));
            return values;
        }

        var hasInvalidValue = false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasDuplicate = false;

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                hasInvalidValue = true;
            }

            if (!seen.Add(value))
            {
                hasDuplicate = true;
            }
        }

        if (hasInvalidValue)
        {
            errors.Add(new(field, ErrorCodes.INVALID_VALUE, "Requested tests cannot be empty."));
        }

        if (hasDuplicate)
        {
            errors.Add(new(field, ErrorCodes.DUPLICATE, "Requested tests must be unique."));
        }

        return values;
    }

    private static OrderResult MalformedResult(string message) =>
        new(
            OrderStatus.Rejected,
            null,
            [new("$", ErrorCodes.MALFORMED_INPUT, message)]);

    private sealed class JsonFields
    {
        public string? OrderId { get; set; }
        public string? PatientId { get; set; }
        public string? SpecimenId { get; set; }
        public string? SpecimenType { get; set; }
        public string? Priority { get; set; }
        public string? CollectionDate { get; set; }
        public IReadOnlyList<string>? RequestedTests { get; set; }
    }
}
