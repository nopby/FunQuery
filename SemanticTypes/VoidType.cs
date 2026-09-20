using System;
using System.Collections.Generic;
using System.Text;

namespace FunQuery.SemanticTypes;

public sealed record VoidType : SemanticType
{
    public override string Name => "void";
}