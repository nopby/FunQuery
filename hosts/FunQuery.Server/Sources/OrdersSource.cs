using FunQuery.AspNetCore;
using FunQuery.SemanticTypes;

namespace FunQuery.Server.Sources;

/// <summary>Contoh source in-memory: daftar pesanan.</summary>
public sealed class OrdersSource : IQuerySource
{
    private static readonly ObjectType OrderSchema = new(
        new Dictionary<string, SemanticType>(StringComparer.Ordinal)
        {
            ["id"] = SemanticTypeOptions.Int,
            ["customer"] = SemanticTypeOptions.String,
            ["total"] = SemanticTypeOptions.Decimal,
            ["status"] = SemanticTypeOptions.String,
            ["orderedOn"] = SemanticTypeOptions.Date,
        });

    public string Name => "orders";

    public ObjectType Schema => OrderSchema;

    public SourceCapabilities Capabilities => SourceCapabilities.All;

    public ValueTask<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LoadAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
        [
            Row(1, "Ana", 120.50m, "open", new DateOnly(2024, 1, 15)),
            Row(2, "Budi", 89.90m, "paid", new DateOnly(2024, 2, 3)),
            Row(3, "Citra", 250.00m, "open", new DateOnly(2024, 3, 21)),
            Row(4, "Dewi", 15.75m, "cancelled", new DateOnly(2024, 4, 2)),
            Row(5, "Eko", 320.10m, "paid", new DateOnly(2024, 5, 18)),
        ];

        return ValueTask.FromResult(rows);
    }

    private static IReadOnlyDictionary<string, object?> Row(
        int id,
        string customer,
        decimal total,
        string status,
        DateOnly orderedOn) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["customer"] = customer,
            ["total"] = total,
            ["status"] = status,
            ["orderedOn"] = orderedOn,
        };
}
