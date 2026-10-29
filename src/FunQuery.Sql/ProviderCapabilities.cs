namespace FunQuery.Sql;

/// <summary>
/// Operasi yang bisa diterjemahkan sebuah provider. Provider memvalidasi plan terhadap flag ini
/// sebelum menyentuh database; yang tidak didukung ditolak, bukan dieksekusi dengan hasil berbeda.
/// </summary>
[Flags]
public enum ProviderCapabilities
{
    None = 0,

    Filtering = 1 << 0,
    Sorting = 1 << 1,
    Projection = 1 << 2,
    Pagination = 1 << 3,
    StringOperations = 1 << 4,
    DateOperations = 1 << 5,

    /// <summary>Terminal: $count, $any, $first.</summary>
    Aggregation = 1 << 6,

    Distinct = 1 << 7,
    DistinctOn = 1 << 8,
    WindowFunctions = 1 << 9,
    JsonProjection = 1 << 10,
    InlineSources = 1 << 11,

    All = Filtering | Sorting | Projection | Pagination |
          StringOperations | DateOperations | Aggregation |
          Distinct | DistinctOn | WindowFunctions |
          JsonProjection | InlineSources,
}
