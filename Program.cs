


using Console;

ReadOnlySpan<char> input = "$source([{id: 1f, name: 'hello'}, {id: 2, name: 'world'}]).$filter(id eq 2)";

using var tokenBuffer = new TokenBuffer();
Lexer.Tokenize(tokenBuffer, input);
var parsed = Parser.Parse(input, tokenBuffer.Span);
var result = SemanticAnalyzer.Analyze(parsed);