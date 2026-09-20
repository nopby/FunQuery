


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

var functions = new Dictionary<string, FunctionDefinition>
{
    ["$source"] = new()
    {
        Name = "$source",
        Parameters = [new ParameterDefinition { Name = "source", Type = typeof(ArrayType) }],
        ReturnTypeFromArguments = (_, args) => args[0],
    },

    ["$filter"] = new()
    {
        Name = "$filter",
        Parameters =
        [
            new ParameterDefinition { Name = "predicate", Type = typeof(BooleanType) }
        ],
        RequiresTarget = true,
        UsesElementScope = true,
        AcceptsTarget = t => t is ArrayType,
        ReturnType = target => target ?? SemanticTypeOptions.Unknown,
    },
};

var semanticContext = new SemanticContext(input.AsMemory(), identifier, functions, limits);
var result = SemanticAnalyzer.Analyze(parsed, semanticContext);