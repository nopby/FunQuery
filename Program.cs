


using FunQuery;
using FunQuery.SemanticTypes;

string input = "$source([{id: 1, name: 'hello'}, {id: 2, name: 'world'}]).$filter(id eq 2.1)";

// Batas keamanan. Semua nilai opsional, default: 2048 karakter, 1024 token, kedalaman 64.
var limits = new QueryLimits();

using var tokenBuffer = new TokenBuffer(limits: limits);
Lexer.Tokenize(tokenBuffer, input);
var parsed = Parser.Parse(input, tokenBuffer.Span, limits);

var identifier = new Dictionary<string, SemanticType>
{
    { "id", SemanticTypeOptions.Int  },
    { "name", SemanticTypeOptions.String  },
};

// Function inti didaftarkan lewat mekanisme ekstensi yang sama dengan ekstensi lain (mis. SQL nanti).
var functions = new FunctionRegistry()
    .AddExtension(new CoreFunctions());

var semanticContext = new SemanticContext(input.AsMemory(), identifier, functions, limits);
var result = SemanticAnalyzer.Analyze(parsed, semanticContext);