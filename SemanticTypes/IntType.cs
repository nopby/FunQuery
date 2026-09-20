using System;
using System.Collections.Generic;
using System.Text;

namespace FunQuery.SemanticTypes;

public sealed record IntType : SemanticType
{
    public override string Name => "int";
}