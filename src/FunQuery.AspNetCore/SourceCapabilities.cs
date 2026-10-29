namespace FunQuery.AspNetCore;

/// <summary>
/// Operasi yang dapat diterjemahkan sebuah source. Dipakai provider pada Milestone 6; adapter
/// in-memory saat ini mendukung semuanya.
/// </summary>
[Flags]
public enum SourceCapabilities
{
    None = 0,
    Filtering = 1 << 0,
    Sorting = 1 << 1,
    Projection = 1 << 2,
    Pagination = 1 << 3,
    StringOperations = 1 << 4,
    DateOperations = 1 << 5,
    Aggregation = 1 << 6,

    All = Filtering | Sorting | Projection | Pagination |
          StringOperations | DateOperations | Aggregation,
}
