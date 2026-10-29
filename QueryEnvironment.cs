using FunQuery.SemanticTypes;

namespace FunQuery;

/// <summary>
/// Kumpulan parameter untuk satu eksekusi query. Immutable: setiap penambahan menghasilkan
/// environment baru, sehingga satu instance aman dipakai bersama.
/// </summary>
public sealed class QueryEnvironment
{
    private readonly Dictionary<string, QueryParameter> _parameters;

    /// <summary>Environment tanpa parameter, dipakai bila host tidak memberikan apa pun.</summary>
    public static QueryEnvironment Empty { get; } =
        new(new Dictionary<string, QueryParameter>(StringComparer.Ordinal));

    public QueryEnvironment(IReadOnlyDictionary<string, QueryParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        _parameters = new Dictionary<string, QueryParameter>(parameters, StringComparer.Ordinal);
    }

    /// <summary>Nama parameter tanpa '@' → tipe dan nilainya.</summary>
    public IReadOnlyDictionary<string, QueryParameter> Parameters => _parameters;

    /// <summary>Menyalin environment ini lalu menambah/mengganti satu parameter.</summary>
    public QueryEnvironment With(string name, QueryParameter parameter)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var copy = new Dictionary<string, QueryParameter>(_parameters, StringComparer.Ordinal)
        {
            [name] = parameter,
        };

        return new QueryEnvironment(copy);
    }

    /// <summary>Environment berisi tepat satu parameter.</summary>
    public static QueryEnvironment From(string name, QueryParameter parameter) =>
        Empty.With(name, parameter);
}
