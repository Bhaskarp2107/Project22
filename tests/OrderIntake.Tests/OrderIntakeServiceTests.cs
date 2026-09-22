using System.Globalization;
using OrderIntake;
using OrderIntake.Enums;
using OrderIntake.Models;
using Xunit;

namespace OrderIntake.Tests;

public sealed class OrderIntakeServiceTests
{
    private readonly OrderIntakeService service = new();

    [Fact]
    public void Process_accepts_valid_order_normalizes_values_and_ignores_unknown_fields()
    {
        var json = $$"""
            {
              "orderId": "ORD-1",
              "patientId": "PAT-1",
              "specimenId": "SPEC-1",
              "specimenType": "bLoOd",
              "priority": "uRgEnT",
              "collectionDate": "{{DateTime.Today:yyyy-MM-dd}}",
              "requestedTests": ["CBC", "Lipid Panel"],
              "unknownField": "ignored"
            }
            """;

        var result = service.Process(json);

        Assert.Equal(OrderStatus.Accepted, result.Status);
        var order = Assert.IsType<LaboratoryOrder>(result.Order);
        Assert.Equal("ORD-1", order.OrderId);
        Assert.Equal("PAT-1", order.PatientId);
        Assert.Equal("SPEC-1", order.SpecimenId);
        Assert.Equal("Blood", order.SpecimenType);
        Assert.Equal("Urgent", order.Priority);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), order.CollectionDate);
        Assert.Equal(["CBC", "Lipid Panel"], order.RequestedTests);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Process_returns_all_applicable_errors_at_once()
    {
        const string json = """
            {
              "orderId": "   ",
              "patientId": "PAT-1",
              "specimenId": "SPEC-1",
              "specimenType": "Plasma",
              "priority": "Immediate",
              "collectionDate": "2026-02-30",
              "requestedTests": []
            }
            """;

        var result = service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Null(result.Order);
        Assert.Equal(
            [
                (Field: "orderId", Code: ErrorCodes.REQUIRED),
                (Field: "specimenType", Code: ErrorCodes.INVALID_VALUE),
                (Field: "priority", Code: ErrorCodes.INVALID_VALUE),
                (Field: "collectionDate", Code: ErrorCodes.INVALID_FORMAT),
                (Field: "requestedTests", Code: ErrorCodes.REQUIRED)
            ],
            result.Errors.Select(error => (error.Field, error.Code)));
    }

    [Fact]
    public void Process_accepts_an_id_with_exactly_20_characters()
    {
        var orderId = new string('O', 20);
        var result = service.Process(ValidJson(orderId: orderId));

        Assert.Equal(OrderStatus.Accepted, result.Status);
        Assert.Equal(orderId, Assert.IsType<LaboratoryOrder>(result.Order).OrderId);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Process_rejects_an_id_with_21_characters()
    {
        var result = service.Process(ValidJson(orderId: new string('O', 21)));

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Null(result.Order);
        var error = Assert.Single(result.Errors);
        Assert.Equal("orderId", error.Field);
        Assert.Equal(ErrorCodes.MAX_LENGTH, error.Code);
    }

    [Fact]
    public void Process_rejects_an_invalid_calendar_date()
    {
        var result = service.Process(ValidJson(collectionDate: "2026-02-30"));

        Assert.Equal(OrderStatus.Rejected, result.Status);
        var error = Assert.Single(result.Errors);
        Assert.Equal("collectionDate", error.Field);
        Assert.Equal(ErrorCodes.INVALID_FORMAT, error.Code);
    }

    [Theory]
    [InlineData("2026/09/20")]
    [InlineData("2026-9-2")]
    public void Process_rejects_incorrect_date_formats(string collectionDate)
    {
        var result = service.Process(ValidJson(collectionDate: collectionDate));

        Assert.Equal(OrderStatus.Rejected, result.Status);
        var error = Assert.Single(result.Errors);
        Assert.Equal("collectionDate", error.Field);
        Assert.Equal(ErrorCodes.INVALID_FORMAT, error.Code);
    }

    [Fact]
    public void Process_rejects_a_future_collection_date()
    {
        var futureDate = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var result = service.Process(ValidJson(collectionDate: futureDate));

        Assert.Equal(OrderStatus.Rejected, result.Status);
        var error = Assert.Single(result.Errors);
        Assert.Equal("collectionDate", error.Field);
        Assert.Equal(ErrorCodes.FUTURE_DATE, error.Code);
    }

    [Fact]
    public void Process_rejects_an_empty_requested_tests_list()
    {
        var result = service.Process(ValidJson(requestedTests: "[]"));

        Assert.Equal(OrderStatus.Rejected, result.Status);
        var error = Assert.Single(result.Errors);
        Assert.Equal("requestedTests", error.Field);
        Assert.Equal(ErrorCodes.REQUIRED, error.Code);
    }

    [Fact]
    public void Process_rejects_requested_tests_that_differ_only_by_case()
    {
        var result = service.Process(ValidJson(requestedTests: "[\"CBC\", \"cbc\"]"));

        Assert.Equal(OrderStatus.Rejected, result.Status);
        var error = Assert.Single(result.Errors);
        Assert.Equal("requestedTests", error.Field);
        Assert.Equal(ErrorCodes.DUPLICATE, error.Code);
    }

    [Fact]
    public void Process_rejects_broken_json_without_throwing()
    {
        var exception = Record.Exception(() => service.Process("{\"orderId\": \"ORD-1\""));
        var result = service.Process("{\"orderId\": \"ORD-1\"");

        Assert.Null(exception);
        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Null(result.Order);
        var error = Assert.Single(result.Errors);
        Assert.Equal("$", error.Field);
        Assert.Equal(ErrorCodes.MALFORMED_INPUT, error.Code);
    }

    [Fact]
    public void Process_rejects_null_input_as_malformed()
    {
        var result = service.Process(null!);

        AssertMalformed(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Process_rejects_empty_or_whitespace_input_as_malformed(string json)
    {
        var result = service.Process(json);

        AssertMalformed(result);
    }

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("\"hello\"")]
    public void Process_rejects_non_object_top_level_json_as_malformed(string json)
    {
        var result = service.Process(json);

        AssertMalformed(result);
    }

    [Fact]
    public void Process_rejects_incompatible_recognized_field_type_as_malformed()
    {
        var result = service.Process("{\"orderId\":123}");

        AssertMalformed(result);
    }

    [Fact]
    public void Process_treats_null_recognized_fields_as_missing()
    {
        const string json = """
            {
              "orderId": null,
              "patientId": null,
              "specimenId": null,
              "specimenType": null,
              "priority": null,
              "collectionDate": null,
              "requestedTests": null
            }
            """;

        var result = service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Null(result.Order);
        Assert.DoesNotContain(result.Errors, error => error.Code == ErrorCodes.MALFORMED_INPUT);
        Assert.Equal(
            [
                (Field: "orderId", Code: ErrorCodes.REQUIRED),
                (Field: "patientId", Code: ErrorCodes.REQUIRED),
                (Field: "specimenId", Code: ErrorCodes.REQUIRED),
                (Field: "specimenType", Code: ErrorCodes.REQUIRED),
                (Field: "priority", Code: ErrorCodes.REQUIRED),
                (Field: "collectionDate", Code: ErrorCodes.REQUIRED),
                (Field: "requestedTests", Code: ErrorCodes.REQUIRED)
            ],
            result.Errors.Select(error => (error.Field, error.Code)));
    }

    [Fact]
    public void Process_rejects_whitespace_requested_test_items()
    {
        var result = service.Process(ValidJson(requestedTests: "[\"CBC\", \"   \"]"));

        Assert.Equal(OrderStatus.Rejected, result.Status);
        var error = Assert.Single(result.Errors);
        Assert.Equal("requestedTests", error.Field);
        Assert.Equal(ErrorCodes.INVALID_VALUE, error.Code);
    }

    [Fact]
    public void Process_accepts_a_patient_id_with_exactly_20_characters()
    {
        var patientId = new string('P', 20);
        var result = service.Process(ValidJson(patientId: patientId));

        Assert.Equal(OrderStatus.Accepted, result.Status);
        Assert.Equal(patientId, Assert.IsType<LaboratoryOrder>(result.Order).PatientId);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Process_rejects_a_patient_id_with_21_characters()
    {
        var result = service.Process(ValidJson(patientId: new string('P', 21)));

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Null(result.Order);
        var error = Assert.Single(result.Errors);
        Assert.Equal("patientId", error.Field);
        Assert.Equal(ErrorCodes.MAX_LENGTH, error.Code);
    }

    [Fact]
    public void Process_accepts_a_specimen_id_with_exactly_20_characters()
    {
        var specimenId = new string('S', 20);
        var result = service.Process(ValidJson(specimenId: specimenId));

        Assert.Equal(OrderStatus.Accepted, result.Status);
        Assert.Equal(specimenId, Assert.IsType<LaboratoryOrder>(result.Order).SpecimenId);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Process_rejects_a_specimen_id_with_21_characters()
    {
        var result = service.Process(ValidJson(specimenId: new string('S', 21)));

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Null(result.Order);
        var error = Assert.Single(result.Errors);
        Assert.Equal("specimenId", error.Field);
        Assert.Equal(ErrorCodes.MAX_LENGTH, error.Code);
    }

    [Fact]
    public void Process_accepts_todays_collection_date()
    {
        var today = DateTime.Today;
        var result = service.Process(ValidJson(collectionDate: today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        Assert.Equal(OrderStatus.Accepted, result.Status);
        Assert.Equal(DateOnly.FromDateTime(today), Assert.IsType<LaboratoryOrder>(result.Order).CollectionDate);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("bLoOd", "Blood")]
    [InlineData("uRiNe", "Urine")]
    [InlineData("tIsSuE", "Tissue")]
    [InlineData("sAlIvA", "Saliva")]
    public void Process_accepts_and_normalizes_all_specimen_types(string input, string canonicalValue)
    {
        var result = service.Process(ValidJson(specimenType: input));

        Assert.Equal(OrderStatus.Accepted, result.Status);
        Assert.Equal(canonicalValue, Assert.IsType<LaboratoryOrder>(result.Order).SpecimenType);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("rOuTiNe", "Routine")]
    [InlineData("uRgEnT", "Urgent")]
    public void Process_accepts_and_normalizes_all_priorities(string input, string canonicalValue)
    {
        var result = service.Process(ValidJson(priority: input));

        Assert.Equal(OrderStatus.Accepted, result.Status);
        Assert.Equal(canonicalValue, Assert.IsType<LaboratoryOrder>(result.Order).Priority);
        Assert.Empty(result.Errors);
    }

    private static void AssertMalformed(OrderResult result)
    {
        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Null(result.Order);
        var error = Assert.Single(result.Errors);
        Assert.Equal("$", error.Field);
        Assert.Equal(ErrorCodes.MALFORMED_INPUT, error.Code);
    }

    private static string ValidJson(
        string orderId = "ORD-1",
        string patientId = "PAT-1",
        string specimenId = "SPEC-1",
        string specimenType = "Blood",
        string priority = "Routine",
        string collectionDate = "2026-09-20",
        string requestedTests = "[\"CBC\"]") => $$"""
        {
          "orderId": "{{orderId}}",
          "patientId": "{{patientId}}",
          "specimenId": "{{specimenId}}",
          "specimenType": "{{specimenType}}",
          "priority": "{{priority}}",
          "collectionDate": "{{collectionDate}}",
          "requestedTests": {{requestedTests}}
        }
        """;
}
