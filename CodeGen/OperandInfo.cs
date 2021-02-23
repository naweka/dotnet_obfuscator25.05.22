﻿using System;

namespace CodeGen
{
    [Serializable]
    public class OperandInfo
    {
        public OperandTypeInfo? OperandType { get; set; }
        public string OperandName { get; set; }
        public string[] ParametersTypesNames { get; set; }
        public string[] GenericTypesNames { get; set; }
    }
}
