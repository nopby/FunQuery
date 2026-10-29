namespace FunQuery.Server.Playground;

internal static class PlaygroundExamples
{
    public static PlaygroundExample[] Create() =>
    [
        new(
            "Inline: count",
            "$source([1, 2, 3]).$count()",
            null,
            null),

        new(
            "Inline: filter and select",
            "$source([{id: 1, name: 'Ana'}, {id: 2, name: 'Budi'}])" +
            ".$filter(id gt 1).$select(id, name)",
            null,
            null),

        new(
            "Variables: inline filter",
            "$source([{id: 1}, {id: 2}, {id: 3}])" +
            ".$filter(id gt @min).$select(id)",
            null,
            "{\"min\": 1}"),

        new(
            "PostgreSQL: orders over 100",
            "$source(@source).$filter(total gt 100).$select(id, customer, total)",
            "orders",
            null),

        new(
            "PostgreSQL: orders with variable",
            "$source(@source).$filter(total gt @min).$select(id, customer, total)",
            "orders",
            "{\"min\": 100}"),

        new(
            "People: active",
            "$source(@source).$filter(active).$select(id, name, nickname)",
            "people",
            null),

        new(
            "Orders: recent dates",
            "$source(@source).$filter(orderedOn gt $date('2024-02-01'))" +
            ".$select(id, customer, orderedOn)",
            "orders",
            null),
    ];
}
