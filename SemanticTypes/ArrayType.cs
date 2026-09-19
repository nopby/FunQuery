using System;
using System.Collections.Generic;
using System.Text;

namespace Console.SemanticTypes;

public sealed record ArrayType(SemanticType Type) : SemanticType
{
    public override string Name => "array";
}