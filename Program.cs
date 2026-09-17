


using Console;

ReadOnlySpan<char> input = "$source([{id: 1, name: 'hello'}, {id: 2, name: 'world'}]).$filter(id eq 1)";

using var tokenBuffer = new TokenBuffer();
Lexer.Tokenize(tokenBuffer, input);
var parsed = Parser.Parse(input, tokenBuffer.Span);