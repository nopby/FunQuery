


using Console;
using Console.SemanticTypes;

string input = "$source([{id: 1, name: 'hello'}, {id: 2, name: 'world'}]).$filter(id eq 2.1)";

using var tokenBuffer = new TokenBuffer();
Lexer.Tokenize(tokenBuffer, input);
var parsed = Parser.Parse(input, tokenBuffer.Span);

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

var semanticContext = new SemanticContext(input.AsMemory(), identifier, functions);
var result = SemanticAnalyzer.Analyze(parsed, semanticContext);