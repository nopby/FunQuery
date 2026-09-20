using System;
using System.Collections.Generic;
using System.Text;

namespace FunQuery.SemanticTypes;

public sealed record FloatType : SemanticType
{
    public override string Name => "float";
}