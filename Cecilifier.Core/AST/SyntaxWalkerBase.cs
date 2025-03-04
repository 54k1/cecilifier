﻿using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;
using static Cecilifier.Core.Misc.Utils;

namespace Cecilifier.Core.AST
{
    internal class SyntaxWalkerBase : CSharpSyntaxWalker
    {
        private const string ModifiersSeparator = " | ";

        internal SyntaxWalkerBase(IVisitorContext ctx)
        {
            Context = ctx;
        }

        protected IVisitorContext Context { get; }

        protected void AddCecilExpressions(IEnumerable<string> exps)
        {
            foreach (var exp in exps)
            {
                AddCecilExpression(exp);
            }
        }

        protected void AddCecilExpression(string exp)
        {
            WriteCecilExpression(Context, exp);
        }

        protected void AddCecilExpression(string format, params object[] args)
        {
            WriteCecilExpression(Context, format, args);
        }

        protected void AddMethodCall(string ilVar, IMethodSymbol method, bool isAccessOnThisOrObjectCreation = false)
        {
            var opCode = (method.IsStatic || method.IsDefinedInCurrentType(Context) && isAccessOnThisOrObjectCreation || method.ContainingType.IsValueType) && !(method.IsVirtual || method.IsAbstract)
                ? OpCodes.Call
                : OpCodes.Callvirt;
            
            if (method.IsStatic)
            {
                opCode = OpCodes.Call;
            }
            
            if (method.IsGenericMethod && method.IsDefinedInCurrentType(Context))
            {
                // if the method in question is a generic method and it is defined in the same assembly create a generic instance
                var resolvedMethodVar = Context.Naming.MemberReference(method.Name, method.ContainingType.Name);
                var m1 = $"var {resolvedMethodVar} = {method.MethodResolverExpression(Context)};";
                
                var genInstVar = Context.Naming.GenericInstance(method);
                var m = $"var {genInstVar} = new GenericInstanceMethod({resolvedMethodVar});";
                AddCecilExpression(m1);
                AddCecilExpression(m);
                for(int i = 0; i < method.TypeArguments.Length; i++)
                    AddCecilExpression($"{genInstVar}.GenericArguments.Add({resolvedMethodVar}.GenericParameters[{i}]);");
                AddCilInstruction(ilVar, opCode, genInstVar);
            }
            else
            {
                AddCilInstruction(ilVar, opCode, method.MethodResolverExpression(Context));
            }
        }

        protected void AddCilInstruction(string ilVar, OpCode opCode, ITypeSymbol type)
        {
            AddCilInstruction(ilVar, opCode, Context.TypeResolver.Resolve(type));
        }

        protected void InsertCilInstructionAfter<T>(LinkedListNode<string> instruction, string ilVar, OpCode opCode, T arg = default)
        {
            var instVar = CreateCilInstruction(ilVar, opCode, arg);
            Context.MoveLineAfter(Context.CurrentLine, instruction);

            AddCecilExpression($"{ilVar}.Append({instVar});");
            Context.MoveLineAfter(Context.CurrentLine, instruction.Next);
        }

        protected void AddCilInstruction<T>(string ilVar, OpCode opCode, T arg)
        {
            var instVar = CreateCilInstruction(ilVar, opCode, arg);
            AddCecilExpression($"{ilVar}.Append({instVar});");
        }

        protected string AddCilInstruction(string ilVar, OpCode opCode)
        {
            var instVar = CreateCilInstruction(ilVar, opCode);
            AddCecilExpression($"{ilVar}.Append({instVar});");

            return instVar;
        }

        protected string CreateCilInstruction(string ilVar, OpCode opCode, object operand = null)
        {
            var operandStr = operand == null ? string.Empty : $", {operand}";
            var instVar = Context.Naming.Instruction(opCode.Code.ToString());
            AddCecilExpression($"var {instVar} = {ilVar}.Create({opCode.ConstantName()}{operandStr});");

            Context.TriggerInstructionAdded(instVar);

            return Context.DefinitionVariables.LastInstructionVar = instVar;
        }

        protected string AddLocalVariableWithResolvedType(string localVarName, DefinitionVariable methodVar, string resolvedVarType)
        {
            var cecilVarDeclName = Context.Naming.SyntheticVariable(localVarName, ElementKind.LocalVariable);
            
            AddCecilExpression("var {0} = new VariableDefinition({1});", cecilVarDeclName, resolvedVarType);
            AddCecilExpression("{0}.Body.Variables.Add({1});", methodVar.VariableName, cecilVarDeclName);

            Context.DefinitionVariables.RegisterNonMethod(string.Empty, localVarName, MemberKind.LocalVariable, cecilVarDeclName);

            return cecilVarDeclName;
        }

        protected IMethodSymbol DeclaredSymbolFor<T>(T node) where T : BaseMethodDeclarationSyntax
        {
            return Context.GetDeclaredSymbol(node);
        }

        protected ITypeSymbol DeclaredSymbolFor(TypeDeclarationSyntax node)
        {
            return Context.GetDeclaredSymbol(node);
        }

        protected void WithCurrentMethod(string declaringTypeName, string localVariable, string methodName, string[] paramTypes, Action<string> action)
        {
            using (Context.DefinitionVariables.WithCurrentMethod(declaringTypeName, methodName, paramTypes, localVariable))
            {
                action(methodName);
            }
        }

        protected string ImportExpressionForType(Type type)
        {
            return ImportExpressionForType(type.FullName);
        }

        private static string ImportExpressionForType(string typeName)
        {
            return ImportFromMainModule($"typeof({typeName})");
        }

        protected string TypeModifiersToCecil(TypeDeclarationSyntax node)
        {
            var hasStaticCtor = node.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Any(d => d.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)));
            var typeAttributes = CecilDefinitionsFactory.DefaultTypeAttributeFor(node.Kind(), hasStaticCtor);
            if (IsNestedTypeDeclaration(node))
            {
                return typeAttributes.AppendModifier(ModifiersToCecil(node.Modifiers, m => "TypeAttributes.Nested" + m.ValueText.PascalCase()));
            }

            var convertedModifiers = ModifiersToCecil("TypeAttributes", node.Modifiers, "NotPublic", ExcludeHasNoCILRepresentationInTypes);
            return typeAttributes.AppendModifier(convertedModifiers);
        }

        private static bool IsNestedTypeDeclaration(SyntaxNode node)
        {
            return node.Parent.Kind() != SyntaxKind.NamespaceDeclaration && node.Parent.Kind() != SyntaxKind.CompilationUnit;
        }

        protected static string ModifiersToCecil(string targetEnum, IEnumerable<SyntaxToken> modifiers, string @default)
        {
            return ModifiersToCecil(targetEnum, modifiers, @default, ExcludeHasNoCILRepresentation);
        }

        private static string ModifiersToCecil(string targetEnum, IEnumerable<SyntaxToken> modifiers, string @default, Func<SyntaxToken, bool> meaninglessModifiersFilter)
        {
            var validModifiers = modifiers.Where(meaninglessModifiersFilter).ToList();

            var hasAccessibilityModifier = validModifiers.Any(m =>
                m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.PrivateKeyword) ||
                m.IsKind(SyntaxKind.ProtectedKeyword) || m.IsKind(SyntaxKind.InternalKeyword));

            var modifiersStr = ModifiersToCecil(validModifiers, m => m.MapModifier(targetEnum));
            if (!validModifiers.Any() || !hasAccessibilityModifier)
            {
                modifiersStr = modifiersStr.AppendModifier(targetEnum + "." + @default);
            }

            return modifiersStr;
        }

        private static string ModifiersToCecil(IEnumerable<SyntaxToken> modifiers, Func<SyntaxToken, string> map)
        {
            var cecilModifierStr = modifiers.Aggregate("", (acc, token) =>
                acc + ModifiersSeparator + map(token));

            if (cecilModifierStr.Length > 0)
            {
                cecilModifierStr = cecilModifierStr.Substring(ModifiersSeparator.Length);
            }

            return cecilModifierStr;
        }

        private static bool ExcludeHasNoCILRepresentationInTypes(SyntaxToken token)
        {
            return ExcludeHasNoCILRepresentation(token) && token.Kind() != SyntaxKind.PrivateKeyword;
        }

        protected static void WriteCecilExpression(IVisitorContext context, string format, params object[] args)
        {
            context.WriteCecilExpression($"{string.Format(format, args)}\r\n");
        }

        protected static void WriteCecilExpression(IVisitorContext context, string value)
        {
            context.WriteCecilExpression($"{value}\r\n");
        }

        protected static bool ExcludeHasNoCILRepresentation(SyntaxToken token)
        {
            return !token.IsKind(SyntaxKind.PartialKeyword) && !token.IsKind(SyntaxKind.VolatileKeyword) && !token.IsKind(SyntaxKind.UnsafeKeyword);
        }

        protected string ResolveExpressionType(ExpressionSyntax expression)
        {
            if (expression == null)
            {
                throw new ArgumentNullException(nameof(expression));
            }

            var info = Context.GetTypeInfo(expression);
            return Context.TypeResolver.Resolve(info.Type);
        }
        
        protected string ResolveType(TypeSyntax type)
        {
            var typeToCheck = type is RefTypeSyntax refType ? refType.Type : type;
            var typeInfo = Context.GetTypeInfo(typeToCheck);

            var resolvedType = Context.TypeResolver.Resolve(typeInfo.Type);
            return type is RefTypeSyntax ? resolvedType.MakeByReferenceType() : resolvedType;
        }

        protected INamedTypeSymbol GetSpecialType(SpecialType specialType)
        {
            return Context.GetSpecialType(specialType);
        }

        protected void ProcessParameter(string ilVar, SimpleNameSyntax node, IParameterSymbol paramSymbol)
        {
            var parent = (CSharpSyntaxNode) node.Parent;
            if (HandleLoadAddress(ilVar, paramSymbol.Type, parent, OpCodes.Ldarga, paramSymbol.Name, MemberKind.Parameter))
            {
                return;
            }

            if (node.Parent.Kind() == SyntaxKind.SimpleMemberAccessExpression && paramSymbol.ContainingType.IsValueType)
            {
                AddCilInstruction(ilVar, OpCodes.Ldarga, Context.DefinitionVariables.GetVariable(paramSymbol.Name, MemberKind.Parameter).VariableName);
            }
            else if (paramSymbol.Ordinal > 3)
            {
                AddCilInstruction(ilVar, OpCodes.Ldarg, paramSymbol.Ordinal.ToCecilIndex());
                HandlePotentialDelegateInvocationOn(node, paramSymbol.Type, ilVar);
                HandlePotentialRefLoad(ilVar, node, paramSymbol.Type);
            }
            else
            {
                var method = paramSymbol.ContainingSymbol as IMethodSymbol;
                OpCode[] optimizedLdArgs = {OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Ldarg_2, OpCodes.Ldarg_3};
                var loadOpCode = optimizedLdArgs[paramSymbol.Ordinal + (method.IsStatic ? 0 : 1)];
                AddCilInstruction(ilVar, loadOpCode);
                HandlePotentialDelegateInvocationOn(node, paramSymbol.Type, ilVar);
                HandlePotentialRefLoad(ilVar, node, paramSymbol.Type);
            }
        }

        protected bool HandleLoadAddress(string ilVar, ITypeSymbol symbol, CSharpSyntaxNode parentNode, OpCode opCode, string symbolName, MemberKind memberKind, string parentName = null)
        {
            return HandleSystemIndexUsage() || HandleRefAssignment() || HandleParameter();
            
            bool HandleSystemIndexUsage()
            {
                // in this case we need to call System.Index.GetOffset(int32) on a value type (System.Index)
                // which requires the address of the value type.
                var isSystemIndexUsedAsIndex = IsSystemIndexUsedAsIndex(symbol, parentNode);
                if (isSystemIndexUsedAsIndex || parentNode.IsKind(SyntaxKind.AddressOfExpression) || (symbol.IsValueType && parentNode.Accept(new UsageVisitor()) == UsageKind.CallTarget))
                {
                    AddCilInstruction(ilVar, opCode, Context.DefinitionVariables.GetVariable(symbolName, memberKind, parentName).VariableName);
                    if (!Context.HasFlag("fixed") && parentNode.IsKind(SyntaxKind.AddressOfExpression))
                        AddCilInstruction(ilVar, OpCodes.Conv_U);

                    return true;
                }

                return false;
            }
            
            bool HandleRefAssignment()
            {
                if (!(parentNode is RefExpressionSyntax refExpression))
                    return false;
                
                var assignedValueSymbol = Context.SemanticModel.GetSymbolInfo(refExpression.Expression);
                if (assignedValueSymbol.Symbol.IsByRef())
                    return false;
                
                AddCilInstruction(ilVar, opCode, Context.DefinitionVariables.GetVariable(symbolName, memberKind, parentName).VariableName);
                return true;
            }

            bool HandleParameter()
            {
                if (!(parentNode is ArgumentSyntax argument) || !argument.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword))
                    return false;

                if (Context.SemanticModel.GetSymbolInfo(argument.Expression).Symbol is IParameterSymbol parameterSymbol && parameterSymbol.RefKind != RefKind.Ref && parameterSymbol.RefKind != RefKind.RefReadOnly)
                {
                    AddCilInstruction(ilVar, opCode, Context.DefinitionVariables.GetVariable(symbolName, memberKind, parentName).VariableName);
                    return true;
                }
                return false;
            }
        }
        
        private void HandlePotentialRefLoad(string ilVar, SimpleNameSyntax argumentSimpleNameSyntax, ITypeSymbol typeSymbol)
        {
            var needsLoadIndirect = false;
            
            var argumentSymbol = Context.SemanticModel.GetSymbolInfo(argumentSimpleNameSyntax).Symbol;
            var argumentIsByRef = argumentSymbol.IsByRef();

            var argument = argumentSimpleNameSyntax.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault();

            if (argument != null)
            {
                var parameterSymbol = ParameterSymbolFromArgumentSyntax(argument);
                var parameterIsByRef = parameterSymbol.IsByRef();

                needsLoadIndirect = argumentIsByRef && !parameterIsByRef;
            }
            
            if (needsLoadIndirect)
                AddCilInstruction(ilVar, LoadIndirectOpCodeFor(typeSymbol.SpecialType));
        }
        
        protected IParameterSymbol ParameterSymbolFromArgumentSyntax(ArgumentSyntax argument)
        {
            var invocation = argument.Ancestors().OfType<InvocationExpressionSyntax>().SingleOrDefault();
            if (invocation != null)
            {
                var argumentIndex = argument.Ancestors().OfType<ArgumentListSyntax>().First().Arguments.IndexOf(argument);
                return ((IMethodSymbol) Context.SemanticModel.GetSymbolInfo(invocation.Expression).Symbol).Parameters.ElementAt(argumentIndex);
            }

            var elementAccess = argument.Ancestors().OfType<ElementAccessExpressionSyntax>().SingleOrDefault();
            if (elementAccess != null)
            {
                var indexerSymbol= Context.SemanticModel.GetIndexerGroup(elementAccess.Expression).FirstOrDefault();
                if (indexerSymbol != null)
                {
                    var argumentIndex = argument.Ancestors().OfType<BracketedArgumentListSyntax>().Single().Arguments.IndexOf(argument);
                    return indexerSymbol.Parameters.ElementAt(argumentIndex);
                }
            }

            return null;
        }
        
        protected OpCode LoadIndirectOpCodeFor(SpecialType type)
        {
            return type switch
            {
                SpecialType.System_Single => OpCodes.Ldind_R4,
                SpecialType.System_Double => OpCodes.Ldind_R8,
                SpecialType.System_SByte => OpCodes.Ldind_I1,
                SpecialType.System_Byte => OpCodes.Ldind_U1,
                SpecialType.System_Int16 => OpCodes.Ldind_I2,
                SpecialType.System_UInt16 => OpCodes.Ldind_U2,
                SpecialType.System_Int32 => OpCodes.Ldind_I4,
                SpecialType.System_UInt32 => OpCodes.Ldind_U4,
                SpecialType.System_Int64 => OpCodes.Ldind_I8,
                SpecialType.System_UInt64 => OpCodes.Ldind_I8,
                SpecialType.System_Char => OpCodes.Ldind_U2,
                SpecialType.System_Boolean => OpCodes.Ldind_U1,
                SpecialType.System_Object => OpCodes.Ldind_Ref,
                
                _ => throw new ArgumentException($"Literal type {type} not supported.", nameof(type))
            };
        }

        private static bool IsSystemIndexUsedAsIndex(ITypeSymbol symbol, CSharpSyntaxNode parentNode)
        {
            return parentNode.Parent.IsKind(SyntaxKind.BracketedArgumentList) && symbol.FullyQualifiedName() == "System.Index";
        }

        protected void HandlePotentialDelegateInvocationOn(SimpleNameSyntax node, ITypeSymbol typeSymbol, string ilVar)
        {
            var invocation = node.Parent as InvocationExpressionSyntax;
            if (invocation == null || invocation.Expression != node)
            {
                return;
            }

            if (typeSymbol is IFunctionPointerTypeSymbol functionPointer)
            {
                AddCilInstruction(ilVar, OpCodes.Calli, CecilDefinitionsFactory.CallSite(Context.TypeResolver, functionPointer));
                return;
            }

            var localDelegateDeclaration = Context.TypeResolver.ResolveTypeLocalVariable(typeSymbol);
            if (localDelegateDeclaration != null)
            {
                AddCilInstruction(ilVar, OpCodes.Callvirt, $"{localDelegateDeclaration}.Methods.Single(m => m.Name == \"Invoke\")");
            }
            else
            {
                var invokeMethod = (IMethodSymbol) typeSymbol.GetMembers("Invoke").SingleOrDefault();
                var resolvedMethod = invokeMethod.MethodResolverExpression(Context);
                AddCilInstruction(ilVar, OpCodes.Callvirt, resolvedMethod);
            }
        }

        protected void HandleAttributesInMemberDeclaration(in SyntaxList<AttributeListSyntax> nodeAttributeLists, Func<AttributeTargetSpecifierSyntax, SyntaxKind, bool> predicate, SyntaxKind toMatch, string whereToAdd)
        {
            var tattributeLists = nodeAttributeLists.Where(c => predicate(c.Target, toMatch));
            HandleAttributesInMemberDeclaration(tattributeLists, whereToAdd);
        }

        protected static bool TargetDoesNotMatch(AttributeTargetSpecifierSyntax target, SyntaxKind operand) => target == null || !target.Identifier.IsKind(operand);
        protected static bool TargetMatches(AttributeTargetSpecifierSyntax target, SyntaxKind operand) => target != null && target.Identifier.IsKind(operand);
        

        protected void HandleAttributesInMemberDeclaration(IEnumerable<AttributeListSyntax> attributeLists, string varName)
        {
            if (!attributeLists.Any())
            {
                return;
            }

            foreach (var attribute in attributeLists.SelectMany(al => al.Attributes))
            {
                var attrsExp = CecilDefinitionsFactory.Attribute(varName, Context, attribute, (attrType, attrArgs) =>
                {
                    var typeVar = Context.TypeResolver.ResolveTypeLocalVariable(attrType);
                    if (typeVar == null)
                    {
                        //attribute is not declared in the same assembly....
                        var ctorArgumentTypes = $"new Type[{attrArgs.Length}] {{ {string.Join(",", attrArgs.Select(arg => $"typeof({Context.GetTypeInfo(arg.Expression).Type.Name})"))} }}";

                        return ImportFromMainModule($"typeof({attrType.FullyQualifiedName()}).GetConstructor({ctorArgumentTypes})");
                    }

                    // Attribute is defined in the same assembly. We need to find the variable that holds its "ctor declaration"
                    var attrCtor = attrType.GetMembers().OfType<IMethodSymbol>().SingleOrDefault(m => m.MethodKind == MethodKind.Constructor && m.Parameters.Length == attrArgs.Length);
                    var attrCtorVar = Context.DefinitionVariables.GetMethodVariable(attrCtor.AsMethodDefinitionVariable());
                    if (!attrCtorVar.IsValid)
                        throw new Exception($"Could not find variable for {attrCtor.ContainingType.Name} ctor.");

                    return attrCtorVar.VariableName;
                });

                AddCecilExpressions(attrsExp);
            }
        }

        protected void LogUnsupportedSyntax(SyntaxNode node)
        {
            var lineSpan = node.GetLocation().GetLineSpan();
            AddCecilExpression($"/* Syntax '{node.Kind()}' is not supported in {lineSpan.Path} ({lineSpan.Span.Start.Line + 1},{lineSpan.Span.Start.Character + 1}):\n------\n{node}\n----*/");
        }
    }

    internal class UsageVisitor : CSharpSyntaxVisitor<UsageKind>
    {
        public override UsageKind VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            return UsageKind.CallTarget;
        }
    }

    internal enum UsageKind
    {
        None = 0,
        CallTarget = 1
    }
}
