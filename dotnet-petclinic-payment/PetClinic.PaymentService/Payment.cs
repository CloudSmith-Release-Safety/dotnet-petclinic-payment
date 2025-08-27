using Amazon.DynamoDBv2.DataModel;
using System.Text.Json.Serialization;

namespace PetClinic.PaymentService;

[DynamoDBTable("PetClinicPayment")]
public record Payment
{
    [DynamoDBHashKey]
    [DynamoDBProperty("id")]
    [JsonPropertyName("paymentId")]
    public string? Id { get; set; }

    [DynamoDBProperty]
    public int? PetId { get; set; }

    [DynamoDBProperty]
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    [DynamoDBProperty]
    [JsonPropertyName("totalAmount")]
    public required double Amount { get; set; }

    [DynamoDBProperty]
    public string? Notes { get; set; }
}