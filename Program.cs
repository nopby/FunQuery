using FunQuery;

const string query =
    "$source([{id: 1, name: 'hello'}, {id: 2, name: 'world'}]).$filter(name eq 'hello')";

var engine = new QueryEngine();

try
{
    Console.WriteLine(engine.Execute(query).ToJson());
}
catch (QueryException error)
{
    Console.WriteLine(error.ToDisplayString());
}
