using System;
using System.Collections.Generic;
using System.Text;

namespace FunQuery.SemanticTypes;

public sealed record ArrayType(SemanticType Type) : SemanticType
{
    public override string Name => "array";
}