﻿using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CodeGen.Models;
using Parser;
using TypeInfo = CodeGen.Models.TypeInfo;

namespace CodeGen.Generators
{
    public class SerializableMethodBodyGenerator
    {
        private readonly MethodBase _method;
        private readonly Module _module;

        private object SafeResolveToken(int token) {
            var declaringTypeGenerics = _method.DeclaringType.GetGenericArguments();
            var methodGenerics = _method is ConstructorInfo ? null : _method.GetGenericArguments();
            object result = null;
            try {
                result = _module.ResolveType(token, declaringTypeGenerics, methodGenerics);
            }
            catch (ArgumentException) { }
            if (result != null) {
                return result;
            }

            try {
                result = _module.ResolveMethod(token, declaringTypeGenerics, methodGenerics);
            }
            catch (ArgumentException) { }
            if (result != null) {
                return result;
            }

            try {
                _module.ResolveField(token, declaringTypeGenerics, methodGenerics);
            }
            catch (ArgumentException) { }

            return result;
        }

        private object ResolveToken(int token, OperandType operandType) {
            var declaringTypeGenerics = _method.DeclaringType.GetGenericArguments();
            var methodGenerics = _method is ConstructorInfo ? null : _method.GetGenericArguments();
            return operandType switch {
                OperandType.InlineMethod => _module.ResolveMethod(token, declaringTypeGenerics, methodGenerics),
                OperandType.InlineField => _module.ResolveField(token, declaringTypeGenerics, methodGenerics),
                OperandType.InlineSig => _module.ResolveSignature(token),
                OperandType.InlineString => _module.ResolveString(token),
                OperandType.InlineType => _module.ResolveType(token, declaringTypeGenerics, methodGenerics),
                OperandType.InlineTok => SafeResolveToken(token),
                var x when
                    x == OperandType.ShortInlineI ||
                    x == OperandType.ShortInlineBrTarget ||
                    x == OperandType.ShortInlineVar
                    => token,
                _ => null
            };
        }

        private static OperandTypeInfo? ConvertOperandType(OperandType type, object operand) {
            if (type == OperandType.InlineTok) {
                return operand switch {
                    Type t => OperandTypeInfo.Type,
                    FieldInfo f => OperandTypeInfo.Field,
                    MethodBase m => OperandTypeInfo.Method,
                    _ => null
                };
            }
            return type switch {
                OperandType.InlineField => OperandTypeInfo.Field,
                OperandType.InlineMethod => OperandTypeInfo.Method,
                OperandType.InlineSig => OperandTypeInfo.Signature,
                OperandType.InlineString => OperandTypeInfo.String,
                OperandType.InlineType => OperandTypeInfo.Type,
                _ => null
            };
        }
        
        

        private static string ResolveObjectName(object obj) {
            return obj switch {
                Type type => type.FullName ?? $"{type.Namespace}.{type.Name}",
                string str => str,
                FieldInfo field =>  $"{ResolveObjectName(field.DeclaringType)}#{field.Name}",
                MethodInfo method => $"{ResolveObjectName(method.DeclaringType)}#{method.Name}",
                ConstructorInfo constructor => $"{ResolveObjectName(constructor.DeclaringType)}#{constructor.Name}",
                _ => null
            };
        }

        private static TypeInfo GetTypeInfo(Type type) {
            return new TypeInfo {
                Name = ResolveObjectName(type).Trim('*', '&'),
                GenericArguments = type.GetGenericArguments().Select(GetTypeInfo).ToArray(),
                IsByRef = type.IsByRef,
                IsPointer = type.IsPointer
            };
        }

        public SerializableMethodBodyGenerator(MethodBase method) {
            _method = method;
            _module = _method.Module;
        }

        public SerializableMethodBody Generate() {
            var parser = new IlParser(_method);
            var a = parser.Parse();
            var instructions = from instruction in parser.Parse()
                let operand = instruction.OperandToken != null
                    ? ResolveToken(instruction.OperandToken.Value, instruction.OpCode.OperandType)
                    : null
                let parameters = operand is MethodBase method
                    ? method.GetParameters().Select(p => GetTypeInfo(p.ParameterType)).ToArray()
                    : null
                let genericArguments = operand switch {
                    MethodBase method when !method.Name.Contains(".ctor") && !method.Name.Contains(".cctor") => method.GetGenericArguments(),
                    Type type => type.GetGenericArguments(),
                    _ => null
                }
                let declaringTypeGenericArguments = operand switch {
                    MethodBase method => method.DeclaringType.GetGenericArguments(),
                    FieldInfo field => field.DeclaringType.GetGenericArguments(),
                    _ => null
                }
                select new InstructionInfo {
                    Offset = instruction.Offset,
                    Size = instruction.OpCode.Size,
                    OperandInfo = new OperandInfo {
                        OperandType = ConvertOperandType(instruction.OpCode.OperandType, operand),
                        OperandName = ResolveObjectName(operand),
                        Parameters = parameters,
                        GenericTypes = genericArguments?.Select(GetTypeInfo).ToArray(),
                        DeclaringTypeGenericTypes = declaringTypeGenericArguments?.Select(GetTypeInfo).ToArray()
                    }
                };
            var methodBody = _method.GetMethodBody();
            return new SerializableMethodBody {
                Instructions = instructions.ToList(),
                IlCode = methodBody.GetILAsByteArray(),
                MaxStackSize = methodBody.MaxStackSize,
                LocalVariables = methodBody.LocalVariables.Select(l => 
                    new SerializableLocalVariableInfo {
                        IsPinned = l.IsPinned, 
                        Info = GetTypeInfo(l.LocalType)
                    }).ToList()
            };
        }
    }
}
