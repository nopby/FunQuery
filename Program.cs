using FunQuery;

var engine = new QueryEngine();

while (true)
{
    string? inputQuery = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(inputQuery))
        break;
    try
    {
        Console.WriteLine(engine.Execute(inputQuery).ToJson());
    }
    catch (QueryException error)
    {
        Console.WriteLine(error.ToDisplayString());
    }
    Console.WriteLine();

}
