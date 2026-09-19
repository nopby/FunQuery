using System;
using System.Collections.Generic;
using System.Text;

namespace Console.SemanticTypes;

public sealed record VoidType : SemanticType
{
    public override string Name => "void";
}