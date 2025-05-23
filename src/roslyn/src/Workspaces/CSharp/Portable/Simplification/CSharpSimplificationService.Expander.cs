﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Extensions.ContextQuery;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Utilities;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Simplification;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Simplification;

using static CSharpSyntaxTokens;
using static SyntaxFactory;

internal partial class CSharpSimplificationService
{
    private sealed class Expander : CSharpSyntaxRewriter
    {
        private static readonly SyntaxTrivia s_oneWhitespaceSeparator = SyntaxTrivia(SyntaxKind.WhitespaceTrivia, " ");

        private static readonly SymbolDisplayFormat s_typeNameFormatWithGenerics =
            new(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                memberOptions:
                    SymbolDisplayMemberOptions.IncludeContainingType,
                localOptions: SymbolDisplayLocalOptions.IncludeType,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.ExpandNullable,
                typeQualificationStyle:
                    SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

        private readonly SemanticModel _semanticModel;
        private readonly Func<SyntaxNode, bool> _expandInsideNode;
        private readonly CancellationToken _cancellationToken;
        private readonly SyntaxAnnotation _annotationForReplacedAliasIdentifier;
        private readonly bool _expandParameter;

        public Expander(
            SemanticModel semanticModel,
            Func<SyntaxNode, bool> expandInsideNode,
            bool expandParameter,
            CancellationToken cancellationToken,
            SyntaxAnnotation annotationForReplacedAliasIdentifier = null)
        {
            _semanticModel = semanticModel;
            _expandInsideNode = expandInsideNode;
            _expandParameter = expandParameter;
            _cancellationToken = cancellationToken;
            _annotationForReplacedAliasIdentifier = annotationForReplacedAliasIdentifier;
        }

        public override SyntaxNode Visit(SyntaxNode node)
        {
            if (_expandInsideNode == null || _expandInsideNode(node))
            {
                return base.Visit(node);
            }

            return node;
        }

        private bool IsPassedToDelegateCreationExpression(ArgumentSyntax argument, ITypeSymbol type)
        {
            if (type.IsDelegateType() &&
                argument.IsParentKind(SyntaxKind.ArgumentList) &&
                argument.Parent?.Parent is ObjectCreationExpressionSyntax objectCreationExpression)
            {
                var objectCreationType = _semanticModel.GetTypeInfo(objectCreationExpression).Type;
                if (objectCreationType.Equals(type))
                {
                    return true;
                }
            }

            return false;
        }

        private SpeculationAnalyzer GetSpeculationAnalyzer(ExpressionSyntax expression, ExpressionSyntax newExpression)
            => new(expression, newExpression, _semanticModel, _cancellationToken);

        private bool TryCastTo(ITypeSymbol targetType, ExpressionSyntax expression, ExpressionSyntax newExpression, out ExpressionSyntax newExpressionWithCast)
        {
            var speculativeAnalyzer = GetSpeculationAnalyzer(expression, newExpression);
            var speculativeSemanticModel = speculativeAnalyzer.SpeculativeSemanticModel;
            var speculatedExpression = speculativeAnalyzer.ReplacedExpression;

            var result = speculatedExpression.CastIfPossible(targetType, speculatedExpression.SpanStart, speculativeSemanticModel, _cancellationToken);

            if (result != speculatedExpression)
            {
                newExpressionWithCast = result;
                return true;
            }

            newExpressionWithCast = null;
            return false;
        }

        private bool TryGetLambdaExpressionBodyWithCast(LambdaExpressionSyntax lambdaExpression, LambdaExpressionSyntax newLambdaExpression, out ExpressionSyntax newLambdaExpressionBodyWithCast)
        {
            if (newLambdaExpression.Body is ExpressionSyntax)
            {
                var body = (ExpressionSyntax)lambdaExpression.Body;
                var newBody = (ExpressionSyntax)newLambdaExpression.Body;

                var returnType = (_semanticModel.GetSymbolInfo(lambdaExpression).Symbol as IMethodSymbol)?.ReturnType;
                if (returnType != null)
                {
                    return TryCastTo(returnType, body, newBody, out newLambdaExpressionBodyWithCast);
                }
            }

            newLambdaExpressionBodyWithCast = null;
            return false;
        }

        public override SyntaxNode VisitReturnStatement(ReturnStatementSyntax node)
        {
            var newNode = base.VisitReturnStatement(node);

            if (newNode is ReturnStatementSyntax newReturnStatement)
            {
                if (newReturnStatement.Expression != null)
                {
                    var parentLambda = node.FirstAncestorOrSelf<LambdaExpressionSyntax>();
                    if (parentLambda != null)
                    {
                        var returnType = (_semanticModel.GetSymbolInfo(parentLambda).Symbol as IMethodSymbol)?.ReturnType;
                        if (returnType != null)
                        {
                            if (TryCastTo(returnType, node.Expression, newReturnStatement.Expression, out var newExpressionWithCast))
                            {
                                newNode = newReturnStatement.WithExpression(newExpressionWithCast);
                            }
                        }
                    }
                }
            }

            return newNode;
        }

        public override SyntaxNode VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            var newNode = base.VisitParenthesizedLambdaExpression(node);

            if (newNode is ParenthesizedLambdaExpressionSyntax parenthesizedLambda)
            {
                // First, try to add a cast to the lambda.
                if (TryGetLambdaExpressionBodyWithCast(node, parenthesizedLambda, out var newLambdaExpressionBodyWithCast))
                {
                    parenthesizedLambda = parenthesizedLambda.WithBody(newLambdaExpressionBodyWithCast);
                }

                // Next, try to add a types to the lambda parameters
                if (_expandParameter && parenthesizedLambda.ParameterList != null)
                {
                    var parameterList = parenthesizedLambda.ParameterList;
                    var parameters = parameterList.Parameters.ToArray();

                    if (parameters.Length > 0 && parameters.Any(p => p.Type == null))
                    {
                        var parameterSymbols = node.ParameterList.Parameters
                            .Select(p => _semanticModel.GetDeclaredSymbol(p, _cancellationToken))
                            .ToArray();

                        if (parameterSymbols.All(p => p.Type?.ContainsAnonymousType() == false))
                        {
                            var newParameters = parameterList.Parameters;

                            for (var i = 0; i < parameterSymbols.Length; i++)
                            {
                                var typeSyntax = parameterSymbols[i].Type.GenerateTypeSyntax().WithTrailingTrivia(s_oneWhitespaceSeparator);
                                var newParameter = parameters[i].WithType(typeSyntax).WithAdditionalAnnotations(Simplifier.Annotation);

                                var currentParameter = newParameters[i];
                                newParameters = newParameters.Replace(currentParameter, newParameter);
                            }

                            var newParameterList = parameterList.WithParameters(newParameters);
                            var newParenthesizedLambda = parenthesizedLambda.WithParameterList(newParameterList);

                            return SimplificationHelpers.CopyAnnotations(from: parenthesizedLambda, to: newParenthesizedLambda);
                        }
                    }
                }

                return parenthesizedLambda;
            }

            return newNode;
        }

        public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            var newNode = base.VisitSimpleLambdaExpression(node);

            if (newNode is SimpleLambdaExpressionSyntax simpleLambda)
            {
                // First, try to add a cast to the lambda.
                if (TryGetLambdaExpressionBodyWithCast(node, simpleLambda, out var newLambdaExpressionBodyWithCast))
                {
                    simpleLambda = simpleLambda.WithBody(newLambdaExpressionBodyWithCast);
                }

                // Next, try to add a type to the lambda parameter
                if (_expandParameter)
                {
                    var parameterSymbol = _semanticModel.GetDeclaredSymbol(node.Parameter);
                    if (parameterSymbol?.Type?.ContainsAnonymousType() == false)
                    {
                        var typeSyntax = parameterSymbol.Type.GenerateTypeSyntax().WithTrailingTrivia(s_oneWhitespaceSeparator);
                        var newSimpleLambdaParameter = simpleLambda.Parameter.WithType(typeSyntax).WithoutTrailingTrivia();

                        var parenthesizedLambda = ParenthesizedLambdaExpression(
                            simpleLambda.AsyncKeyword,
                            ParameterList([newSimpleLambdaParameter])
                                .WithTrailingTrivia(simpleLambda.Parameter.GetTrailingTrivia())
                                .WithLeadingTrivia(simpleLambda.Parameter.GetLeadingTrivia()),
                            simpleLambda.ArrowToken,
                            simpleLambda.Body).WithAdditionalAnnotations(Simplifier.Annotation);

                        return SimplificationHelpers.CopyAnnotations(from: simpleLambda, to: parenthesizedLambda);
                    }
                }

                return simpleLambda;
            }

            return newNode;
        }

        public override SyntaxNode VisitArgument(ArgumentSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var newArgument = (ArgumentSyntax)base.VisitArgument(node);

            if (node.NameColon == null
                && node.Parent is TupleExpressionSyntax tuple
                && !IsTupleInDeconstruction(tuple)) // The language currently does not allow explicit element names in deconstruction
            {
                var inferredName = node.Expression.TryGetInferredMemberName();
                if (CanMakeNameExplicitInTuple(tuple, inferredName))
                {
                    var identifier = Identifier(inferredName);
                    identifier = TryEscapeIdentifierToken(identifier, node);

                    newArgument = newArgument
                        .WithoutLeadingTrivia()
                        .WithNameColon(NameColon(IdentifierName(identifier)))
                        .WithAdditionalAnnotations(Simplifier.Annotation)
                        .WithLeadingTrivia(node.GetLeadingTrivia());
                }
            }

            var argumentType = _semanticModel.GetTypeInfo(node.Expression).ConvertedType;
            if (argumentType != null &&
                !IsPassedToDelegateCreationExpression(node, argumentType) &&
                node.Expression.Kind() != SyntaxKind.DeclarationExpression &&
                node.RefOrOutKeyword.Kind() == SyntaxKind.None)
            {
                if (TryCastTo(argumentType, node.Expression, newArgument.Expression, out var newArgumentExpressionWithCast))
                {
                    return newArgument.WithExpression(newArgumentExpressionWithCast);
                }
            }

            return newArgument;
        }

        private static bool CanMakeNameExplicitInTuple(TupleExpressionSyntax tuple, string name)
        {
            if (name == null || SyntaxFacts.IsReservedTupleElementName(name))
            {
                return false;
            }

            var found = false;
            foreach (var argument in tuple.Arguments)
            {
                string elementName;
                if (argument.NameColon != null)
                {
                    elementName = argument.NameColon.Name.Identifier.ValueText;
                }
                else
                {
                    elementName = argument.Expression?.TryGetInferredMemberName();
                }

                if (elementName?.Equals(name, StringComparison.Ordinal) == true)
                {
                    if (found)
                    {
                        // No duplicate names allowed
                        return false;
                    }

                    found = true;
                }
            }

            return true;
        }

        public override SyntaxNode VisitAnonymousObjectMemberDeclarator(AnonymousObjectMemberDeclaratorSyntax node)
        {
            var newDeclarator = (AnonymousObjectMemberDeclaratorSyntax)base.VisitAnonymousObjectMemberDeclarator(node);
            if (node.NameEquals == null)
            {
                var inferredName = node.Expression.TryGetInferredMemberName();
                if (inferredName != null)
                {
                    // Creating identifier without elastic trivia to avoid unexpected line break
                    var identifier = Identifier(SyntaxTriviaList.Empty, inferredName, SyntaxTriviaList.Empty);
                    identifier = TryEscapeIdentifierToken(identifier, node);

                    newDeclarator = newDeclarator
                        .WithoutLeadingTrivia()
                        .WithNameEquals(NameEquals(IdentifierName(identifier))
                            .WithLeadingTrivia(node.GetLeadingTrivia()))
                        .WithAdditionalAnnotations(Simplifier.Annotation);
                }
            }

            return newDeclarator;
        }

        public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            // Special case: We parenthesize to avoid a situation where inlining an
            // expression can cause code to parse differently. The canonical case is...
            //
            //     var x = 0;
            //     var y = (1 + 2);
            //     var z = new[] { x < x, x > y };
            //
            // Inlining 'y' in the code above will produce code that parses differently
            // (i.e. as a generic method invocation).
            //
            //      var z = new[] { x < x, x > (1 + 2) };

            var result = (BinaryExpressionSyntax)base.VisitBinaryExpression(node);

            if ((node.Kind() == SyntaxKind.GreaterThanExpression || node.Kind() == SyntaxKind.RightShiftExpression) && !node.IsParentKind(SyntaxKind.ParenthesizedExpression))
            {
                return result.Parenthesize();
            }

            return result;
        }

        public override SyntaxNode VisitInterpolation(InterpolationSyntax node)
        {
            var result = (InterpolationSyntax)base.VisitInterpolation(node);

            if (result.Expression != null && !result.Expression.IsKind(SyntaxKind.ParenthesizedExpression))
            {
                result = result.WithExpression(result.Expression.Parenthesize());
            }

            return result;
        }

        public override SyntaxNode VisitXmlNameAttribute(XmlNameAttributeSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var newNode = (XmlNameAttributeSyntax)base.VisitXmlNameAttribute(node);

            return node.CopyAnnotationsTo(newNode);
        }

        public override SyntaxNode VisitNameMemberCref(NameMemberCrefSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var rewrittenname = (TypeSyntax)this.Visit(node.Name);
            var parameters = (CrefParameterListSyntax)this.Visit(node.Parameters);

            if (rewrittenname is QualifiedNameSyntax qualifiedName)
            {
                return node.CopyAnnotationsTo(QualifiedCref(
                    qualifiedName.Left
                        .WithAdditionalAnnotations(Simplifier.Annotation),
                    NameMemberCref(((QualifiedNameSyntax)rewrittenname).Right, parameters)
                    .WithLeadingTrivia(SyntaxTriviaList.Empty))
                        .WithLeadingTrivia(node.GetLeadingTrivia())
                        .WithTrailingTrivia(node.GetTrailingTrivia()))
                        .WithAdditionalAnnotations(Simplifier.Annotation);
            }
            else if (rewrittenname.Kind() == SyntaxKind.AliasQualifiedName)
            {
                return node.CopyAnnotationsTo(TypeCref(
                    rewrittenname).WithLeadingTrivia(node.GetLeadingTrivia())
                    .WithTrailingTrivia(node.GetTrailingTrivia()))
                    .WithAdditionalAnnotations(Simplifier.Annotation);
            }

            return node.Update(rewrittenname, parameters);
        }

        public override SyntaxNode VisitGenericName(GenericNameSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var newNode = (SimpleNameSyntax)base.VisitGenericName(node);

            return VisitSimpleName(newNode, node);
        }

        public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var newNode = (SimpleNameSyntax)base.VisitIdentifierName(node);

            return VisitSimpleName(newNode, node);
        }

        private ExpressionSyntax VisitSimpleName(SimpleNameSyntax rewrittenSimpleName, SimpleNameSyntax originalSimpleName)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            // if this is "var", then do not process further
            if (originalSimpleName.IsVar)
            {
                return rewrittenSimpleName;
            }

            var identifier = rewrittenSimpleName.Identifier;
            ExpressionSyntax newNode = rewrittenSimpleName;

            var isInsideCref = originalSimpleName.AncestorsAndSelf(ascendOutOfTrivia: true).Any(n => n is CrefSyntax);

            ////
            //// 1. if this identifier is an alias, we'll expand it here and replace the node completely.
            ////
            if (!SyntaxFacts.IsAliasQualifier(originalSimpleName))
            {
                var aliasInfo = _semanticModel.GetAliasInfo(originalSimpleName, _cancellationToken);
                if (aliasInfo != null)
                {
                    var aliasTarget = aliasInfo.Target;

                    if (aliasTarget is INamespaceSymbol { IsGlobalNamespace: true })
                        return rewrittenSimpleName;

                    // if the enclosing expression is a typeof expression that already contains open type we cannot
                    // we need to insert an open type as well.
                    var typeOfExpression = originalSimpleName.GetAncestor<TypeOfExpressionSyntax>();
                    if (typeOfExpression != null && IsTypeOfUnboundGenericType(_semanticModel, typeOfExpression))
                    {
                        aliasTarget = ((INamedTypeSymbol)aliasTarget).ConstructUnboundGenericType();
                    }

                    if (aliasTarget is INamedTypeSymbol typeSymbol && typeSymbol.IsTupleType)
                    {
                        return rewrittenSimpleName;
                    }

                    // the expanded form replaces the current identifier name.
                    var replacement = FullyQualifyIdentifierName(
                        aliasTarget,
                        newNode,
                        originalSimpleName,
                        replaceNode: true,
                        isInsideCref: isInsideCref,
                        omitLeftHandSide: false)
                            .WithAdditionalAnnotations(Simplifier.Annotation);

                    // We replace the simple name completely, so we can't continue and rename the token
                    // with a RenameLocationAnnotation.
                    // There's also no way of removing annotations, so we just add a DoNotRenameAnnotation.
                    switch (replacement.Kind())
                    {
                        case SyntaxKind.AliasQualifiedName:
                            var aliasQualifiedReplacement = (AliasQualifiedNameSyntax)replacement;
                            replacement = replacement.ReplaceNode(
                                    aliasQualifiedReplacement.Name,
                                    aliasQualifiedReplacement.Name.WithIdentifier(
                                        GetNewIdentifier(aliasQualifiedReplacement.Name.Identifier)));

                            var firstReplacementToken = replacement.GetFirstToken(true, false, true, true);
                            var firstOriginalToken = originalSimpleName.GetFirstToken(true, false, true, true);
                            if (TryAddLeadingElasticTriviaIfNecessary(firstReplacementToken, firstOriginalToken, out var tokenWithLeadingWhitespace))
                            {
                                replacement = replacement.ReplaceToken(firstOriginalToken, tokenWithLeadingWhitespace);
                            }

                            break;

                        case SyntaxKind.QualifiedName:
                            var qualifiedReplacement = (QualifiedNameSyntax)replacement;
                            replacement = replacement.ReplaceNode(
                                    qualifiedReplacement.Right,
                                    qualifiedReplacement.Right.WithIdentifier(
                                        GetNewIdentifier(qualifiedReplacement.Right.Identifier)));
                            break;

                        case SyntaxKind.IdentifierName:
                            var identifierReplacement = (IdentifierNameSyntax)replacement;
                            replacement = replacement.ReplaceToken(identifier, GetNewIdentifier(identifierReplacement.Identifier));
                            break;

                        default:
                            throw ExceptionUtilities.UnexpectedValue(replacement.Kind());
                    }

                    replacement = newNode.CopyAnnotationsTo(replacement);
                    replacement = AppendElasticTriviaIfNecessary(replacement, originalSimpleName);

                    return replacement;

                    SyntaxToken GetNewIdentifier(SyntaxToken _identifier)
                    {
                        var newIdentifier = identifier.CopyAnnotationsTo(_identifier);

                        if (_annotationForReplacedAliasIdentifier != null)
                        {
                            newIdentifier = newIdentifier.WithAdditionalAnnotations(_annotationForReplacedAliasIdentifier);
                        }

                        var aliasAnnotationInfo = AliasAnnotation.Create(aliasInfo.Name);
                        return newIdentifier.WithAdditionalAnnotations(aliasAnnotationInfo);
                    }
                }
            }

            var symbol = _semanticModel.GetSymbolInfo(originalSimpleName.Identifier).Symbol;
            if (symbol == null)
            {
                return newNode;
            }

            var typeArgumentSymbols = TypeArgumentSymbolsPresentInName(originalSimpleName);
            var omitLeftSideOfExpression = false;

            // Check to see if the type Arguments in the resultant Symbol is recursively defined.
            if (IsTypeArgumentDefinedRecursive(symbol, typeArgumentSymbols, enterContainingSymbol: true))
            {
                if (symbol.ContainingSymbol.Equals(symbol.OriginalDefinition.ContainingSymbol) &&
                    symbol is IMethodSymbol { IsStatic: true })
                {
                    if (IsTypeArgumentDefinedRecursive(symbol, typeArgumentSymbols, enterContainingSymbol: false))
                    {
                        return newNode;
                    }
                    else
                    {
                        omitLeftSideOfExpression = true;
                    }
                }
                else
                {
                    return newNode;
                }
            }

            if (IsInvocationWithDynamicArguments(originalSimpleName, _semanticModel))
            {
                return newNode;
            }

            ////
            //// 2. If it's an attribute, make sure the identifier matches the attribute's class name.
            ////
            if (originalSimpleName.GetAncestor<AttributeSyntax>() != null)
            {
                if (symbol.IsConstructor() && symbol.ContainingType?.IsAttribute() == true)
                {
                    symbol = symbol.ContainingType;
                    var name = symbol.Name;

                    Debug.Assert(name.StartsWith(originalSimpleName.Identifier.ValueText, StringComparison.Ordinal));

                    // if the user already used the Attribute suffix in the attribute, we'll maintain it.
                    if (identifier.ValueText == name && name.EndsWith("Attribute", StringComparison.Ordinal))
                    {
                        identifier = identifier.WithAdditionalAnnotations(SimplificationHelpers.DoNotSimplifyAnnotation);
                    }

                    identifier = identifier.CopyAnnotationsTo(VerbatimIdentifier(identifier.LeadingTrivia, name, name, identifier.TrailingTrivia));
                }
            }

            ////
            //// 3. Always try to escape keyword identifiers
            ////
            identifier = TryEscapeIdentifierToken(identifier, originalSimpleName).WithAdditionalAnnotations(Simplifier.Annotation);
            if (identifier != rewrittenSimpleName.Identifier)
            {
                switch (newNode.Kind())
                {
                    case SyntaxKind.IdentifierName:
                    case SyntaxKind.GenericName:
                        newNode = ((SimpleNameSyntax)newNode).WithIdentifier(identifier);
                        break;

                    default:
                        throw new NotImplementedException();
                }
            }

            var parent = originalSimpleName.Parent;

            // do not complexify further for location where only simple names are allowed
            if (parent is MemberBindingExpressionSyntax ||
                originalSimpleName.GetAncestor<NameEqualsSyntax>() != null ||
                (parent is MemberAccessExpressionSyntax && parent.Kind() != SyntaxKind.SimpleMemberAccessExpression) ||
                ((parent.Kind() == SyntaxKind.SimpleMemberAccessExpression || parent.Kind() == SyntaxKind.NameMemberCref) && originalSimpleName.IsRightSideOfDot()) ||
                (parent.Kind() == SyntaxKind.QualifiedName && originalSimpleName.IsRightSideOfQualifiedName()) ||
                (parent.Kind() == SyntaxKind.AliasQualifiedName) ||
                (parent.Kind() == SyntaxKind.NameColon))
            {
                return TryAddTypeArgumentToIdentifierName(newNode, symbol);
            }

            ////
            //// 4. If this is a standalone identifier or the left side of a qualified name or member access try to fully qualify it
            ////

            // we need to treat the constructor as type name, so just get the containing type.
            if (symbol.IsConstructor() && (parent.Kind() == SyntaxKind.ObjectCreationExpression || parent.Kind() == SyntaxKind.NameMemberCref))
            {
                symbol = symbol.ContainingType;
            }

            // if it's a namespace or type name, fully qualify it.
            if (symbol.Kind is SymbolKind.Namespace or SymbolKind.NamedType)
            {
                var replacement = FullyQualifyIdentifierName(
                    (INamespaceOrTypeSymbol)symbol,
                    newNode,
                    originalSimpleName,
                    replaceNode: false,
                    isInsideCref: isInsideCref,
                    omitLeftHandSide: omitLeftSideOfExpression)
                        .WithAdditionalAnnotations(Simplifier.Annotation);

                replacement = AppendElasticTriviaIfNecessary(replacement, originalSimpleName);

                return replacement;
            }

            // if it's a member access, we're fully qualifying the left side and make it a member access.
            if (symbol.Kind is SymbolKind.Method or SymbolKind.Field or SymbolKind.Property)
            {
                if (symbol.IsStatic ||
                    originalSimpleName.IsParentKind(SyntaxKind.NameMemberCref) ||
                    _semanticModel.SyntaxTree.IsNameOfContext(originalSimpleName.SpanStart, _semanticModel, _cancellationToken))
                {
                    newNode = FullyQualifyIdentifierName(
                        symbol,
                        newNode,
                        originalSimpleName,
                        replaceNode: false,
                        isInsideCref: isInsideCref,
                        omitLeftHandSide: omitLeftSideOfExpression);
                }
                else
                {
                    if (!IsPropertyNameOfObjectInitializer(originalSimpleName) &&
                        !symbol.IsLocalFunction())
                    {
                        ExpressionSyntax left;

                        // Assumption here is, if the enclosing and containing types are different then there is inheritance relationship
                        if (!Equals(_semanticModel.GetEnclosingNamedType(originalSimpleName.SpanStart, _cancellationToken), symbol.ContainingType))
                        {
                            left = BaseExpression();
                        }
                        else
                        {
                            left = ThisExpression();
                        }

                        var identifiersLeadingTrivia = newNode.GetLeadingTrivia();
                        newNode = TryAddTypeArgumentToIdentifierName(newNode, symbol);

                        newNode = newNode.CopyAnnotationsTo(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                left,
                                (SimpleNameSyntax)newNode.WithLeadingTrivia(null))
                                                         .WithLeadingTrivia(identifiersLeadingTrivia));
                    }
                }
            }

            var result = newNode.WithAdditionalAnnotations(Simplifier.Annotation);
            result = AppendElasticTriviaIfNecessary(result, originalSimpleName);

            return result;
        }

        private ExpressionSyntax TryReplaceAngleBracesWithCurlyBraces(ExpressionSyntax expression, bool isInsideCref)
        {
            if (isInsideCref)
            {
                var leftTokens = expression.DescendantTokens();
                var candidateTokens = new List<SyntaxToken>();

                foreach (var candidateToken in leftTokens)
                {
                    if (candidateToken.Kind() is SyntaxKind.LessThanToken or SyntaxKind.GreaterThanToken)
                    {
                        candidateTokens.Add(candidateToken);
                        continue;
                    }
                }

                expression = expression.ReplaceTokens(candidateTokens, computeReplacementToken: ReplaceTokenForCref);
            }

            return expression;
        }

        private static ExpressionSyntax TryAddTypeArgumentToIdentifierName(ExpressionSyntax newNode, ISymbol symbol)
        {
            if (newNode.Kind() == SyntaxKind.IdentifierName &&
                symbol is IMethodSymbol { TypeArguments.Length: > 0 } method)
            {
                var typeArguments = method.TypeArguments;
                if (!typeArguments.Any(static t => t.ContainsAnonymousType()))
                {
                    var genericName = GenericName(
                                    ((IdentifierNameSyntax)newNode).Identifier,
                                    TypeArgumentList(
                                        [.. typeArguments.Select(p => ParseTypeName(p.ToDisplayString(s_typeNameFormatWithGenerics)))]))
                                    .WithLeadingTrivia(newNode.GetLeadingTrivia())
                                    .WithTrailingTrivia(newNode.GetTrailingTrivia())
                                    .WithAdditionalAnnotations(Simplifier.Annotation);

                    genericName = newNode.CopyAnnotationsTo(genericName);
                    return genericName;
                }
            }

            return newNode;
        }

        private IList<ISymbol> TypeArgumentSymbolsPresentInName(SimpleNameSyntax simpleName)
        {
            var typeArgumentSymbols = new List<ISymbol>();
            var typeArgumentListSyntax = simpleName.DescendantNodesAndSelf().Where(n => n is TypeArgumentListSyntax);
            foreach (var typeArgumentList in typeArgumentListSyntax)
            {
                var castedTypeArgument = (TypeArgumentListSyntax)typeArgumentList;
                foreach (var typeArgument in castedTypeArgument.Arguments)
                {
                    var symbol = _semanticModel.GetSymbolInfo(typeArgument).Symbol;
                    if (symbol != null && !typeArgumentSymbols.Contains(symbol))
                    {
                        typeArgumentSymbols.Add(symbol);
                    }
                }
            }

            return typeArgumentSymbols;
        }

        private static bool IsInvocationWithDynamicArguments(SimpleNameSyntax originalSimpleName, SemanticModel semanticModel)
        {
            var invocationExpression = originalSimpleName.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();

            // Check to see if this is the invocation Expression we wanted to work with
            if (invocationExpression != null && invocationExpression.Expression.GetLastToken() == originalSimpleName.GetLastToken())
            {
                if (invocationExpression.ArgumentList != null)
                {
                    foreach (var argument in invocationExpression.ArgumentList.Arguments)
                    {
                        if (argument != null)
                        {
                            var typeinfo = semanticModel.GetTypeInfo(argument.Expression);
                            if (typeinfo.Type != null && typeinfo.Type.TypeKind == TypeKind.Dynamic)
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsTypeArgumentDefinedRecursive(ISymbol symbol, IList<ISymbol> typeArgumentSymbols, bool enterContainingSymbol)
        {
            if (Equals(symbol, symbol.OriginalDefinition))
            {
                return false;
            }

            var typeArgumentsInSymbol = new List<ISymbol>();
            TypeArgumentsInAllContainingSymbol(symbol, typeArgumentsInSymbol, enterContainingSymbol, isRecursive: true);

            var typeArgumentsInOriginalDefinition = new List<ISymbol>();
            TypeArgumentsInAllContainingSymbol(symbol.OriginalDefinition, typeArgumentsInOriginalDefinition, enterContainingSymbol, isRecursive: false);

            if (typeArgumentsInSymbol.Intersect(typeArgumentsInOriginalDefinition).Any(n => !typeArgumentSymbols.Contains(n)))
            {
                return true;
            }

            return false;
        }

        private static void TypeArgumentsInAllContainingSymbol(ISymbol symbol, IList<ISymbol> typeArgumentSymbols, bool enterContainingSymbol, bool isRecursive)
        {
            if (symbol == null || symbol is INamespaceSymbol)
            {
                // This is the terminating condition
                return;
            }

            if (symbol is INamedTypeSymbol namedTypedSymbol)
            {
                if (namedTypedSymbol.TypeArguments.Length != 0)
                {
                    foreach (var typeArgument in namedTypedSymbol.TypeArguments)
                    {
                        if (!typeArgumentSymbols.Contains(typeArgument))
                        {
                            typeArgumentSymbols.Add(typeArgument);
                            if (isRecursive)
                            {
                                TypeArgumentsInAllContainingSymbol(typeArgument, typeArgumentSymbols, enterContainingSymbol, isRecursive);
                            }
                        }
                    }
                }
            }

            if (enterContainingSymbol)
            {
                TypeArgumentsInAllContainingSymbol(symbol.ContainingSymbol, typeArgumentSymbols, enterContainingSymbol, isRecursive);
            }
        }

        private static bool IsPropertyNameOfObjectInitializer(SimpleNameSyntax identifierName)
        {
            SyntaxNode currentNode = identifierName;
            SyntaxNode parent = identifierName;

            while (parent != null)
            {
                if (parent.Kind() is SyntaxKind.ObjectInitializerExpression or SyntaxKind.WithInitializerExpression)
                {
                    return currentNode.Kind() == SyntaxKind.SimpleAssignmentExpression &&
                        object.Equals(((AssignmentExpressionSyntax)currentNode).Left, identifierName);
                }
                else if (parent is ExpressionSyntax)
                {
                    currentNode = parent;
                    parent = parent.Parent;

                    continue;
                }
                else
                {
                    return false;
                }
            }

            return false;
        }

        private ExpressionSyntax FullyQualifyIdentifierName(
            ISymbol symbol,
            ExpressionSyntax rewrittenNode,
            ExpressionSyntax originalNode,
            bool replaceNode,
            bool isInsideCref,
            bool omitLeftHandSide)
        {
            Debug.Assert(!replaceNode || rewrittenNode.Kind() == SyntaxKind.IdentifierName);

            //// TODO: use and expand Generate*Syntax(isymbol) to not depend on symbol display any more.
            //// See GenerateExpressionSyntax();

            var result = rewrittenNode;

            // only if this symbol has a containing type or namespace there is work for us to do.
            if (replaceNode || symbol.ContainingType != null || symbol.ContainingNamespace != null)
            {
                // we either need to create an AliasQualifiedName if the symbol is directly contained in the global namespace,
                // otherwise it a QualifiedName.
                if (!replaceNode && symbol.ContainingType == null && symbol.ContainingNamespace.IsGlobalNamespace)
                {
                    return rewrittenNode.CopyAnnotationsTo(
                        AliasQualifiedName(
                            IdentifierName(GlobalKeyword),
                            (SimpleNameSyntax)rewrittenNode.WithLeadingTrivia(null))
                                .WithLeadingTrivia(rewrittenNode.GetLeadingTrivia()));
                }

                rewrittenNode = TryAddTypeArgumentToIdentifierName(rewrittenNode, symbol);

                // Replaces the '<' token with the '{' token since we are inside crefs
                rewrittenNode = TryReplaceAngleBracesWithCurlyBraces(rewrittenNode, isInsideCref);
                result = rewrittenNode;

                if (!omitLeftHandSide)
                {
                    var displaySymbol = replaceNode
                        ? symbol
                        : (symbol.ContainingType ?? (ISymbol)symbol.ContainingNamespace);

                    var displayString = displaySymbol.ToDisplayString(s_typeNameFormatWithGenerics);

                    ExpressionSyntax left = ParseTypeName(displayString);

                    // Replaces the '<' token with the '{' token since we are inside crefs
                    left = TryReplaceAngleBracesWithCurlyBraces(left, isInsideCref);

                    if (replaceNode)
                    {
                        return left
                            .WithLeadingTrivia(rewrittenNode.GetLeadingTrivia())
                            .WithTrailingTrivia(rewrittenNode.GetTrailingTrivia());
                    }

                    // now create syntax for the combination of left and right syntax, or a simple replacement in case of an identifier
                    var parent = originalNode.Parent;
                    var leadingTrivia = rewrittenNode.GetLeadingTrivia();
                    rewrittenNode = rewrittenNode.WithLeadingTrivia(null);

                    switch (parent.Kind())
                    {
                        case SyntaxKind.QualifiedName:
                            result = rewrittenNode.CopyAnnotationsTo(
                                QualifiedName(
                                    (NameSyntax)left,
                                    (SimpleNameSyntax)rewrittenNode));

                            break;

                        case SyntaxKind.SimpleMemberAccessExpression:
                            result = rewrittenNode.CopyAnnotationsTo(
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    left,
                                    (SimpleNameSyntax)rewrittenNode));

                            break;

                        default:
                            Debug.Assert(rewrittenNode is SimpleNameSyntax);

                            if (SyntaxFacts.IsInNamespaceOrTypeContext(originalNode))
                            {
                                var right = (SimpleNameSyntax)rewrittenNode;
                                result = rewrittenNode.CopyAnnotationsTo(QualifiedName((NameSyntax)left, right.WithAdditionalAnnotations(Simplifier.SpecialTypeAnnotation)));
                            }
                            else if (originalNode.Parent is CrefSyntax)
                            {
                                var right = (SimpleNameSyntax)rewrittenNode;
                                result = rewrittenNode.CopyAnnotationsTo(QualifiedName((NameSyntax)left, right));
                            }
                            else
                            {
                                result = rewrittenNode.CopyAnnotationsTo(
                                    MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        left,
                                        (SimpleNameSyntax)rewrittenNode));
                            }

                            break;
                    }

                    result = result.WithLeadingTrivia(leadingTrivia);
                }
            }

            return result;
        }

        private SyntaxToken ReplaceTokenForCref(SyntaxToken oldToken, SyntaxToken dummySameToken)
        {
            if (oldToken.Kind() == SyntaxKind.LessThanToken)
            {
                return Token(oldToken.LeadingTrivia, SyntaxKind.LessThanToken, "{", "{", oldToken.TrailingTrivia);
            }

            if (oldToken.Kind() == SyntaxKind.GreaterThanToken)
            {
                return Token(oldToken.LeadingTrivia, SyntaxKind.GreaterThanToken, "}", "}", oldToken.TrailingTrivia);
            }

            Debug.Assert(false, "This method is used only replacing the '<' and '>' to '{' and '}' respectively");
            return default;
        }

        private bool IsTypeOfUnboundGenericType(SemanticModel semanticModel, TypeOfExpressionSyntax typeOfExpression)
        {
            if (typeOfExpression != null)
            {
                var type = semanticModel.GetTypeInfo(typeOfExpression.Type, _cancellationToken).Type as INamedTypeSymbol;

                // It's possible the immediate type might not be an unbound type, such as typeof(A<>.B). So walk through
                // parent types too
                while (type != null)
                {
                    if (type.IsUnboundGenericType)
                    {
                        return true;
                    }

                    type = type.ContainingType;
                }
            }

            return false;
        }

        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax originalNode)
        {
            if (_semanticModel.GetSymbolInfo(originalNode).Symbol.IsLocalFunction())
            {
                return originalNode;
            }

            var rewrittenNode = (InvocationExpressionSyntax)base.VisitInvocationExpression(originalNode);
            if (originalNode.Expression is MemberAccessExpressionSyntax(SyntaxKind.SimpleMemberAccessExpression) memberAccess)
            {
                var targetSymbol = SimplificationHelpers.GetOriginalSymbolInfo(_semanticModel, memberAccess.Name);

                if (targetSymbol != null && targetSymbol.IsReducedExtension() && memberAccess.Expression != null)
                {
                    rewrittenNode = RewriteExtensionMethodInvocation(originalNode, rewrittenNode, ((MemberAccessExpressionSyntax)rewrittenNode.Expression).Expression, (IMethodSymbol)targetSymbol);
                }
            }

            return rewrittenNode;
        }

        private InvocationExpressionSyntax RewriteExtensionMethodInvocation(
            InvocationExpressionSyntax originalNode,
            InvocationExpressionSyntax rewrittenNode,
            ExpressionSyntax thisExpression,
            IMethodSymbol reducedExtensionMethod)
        {
            var originalMemberAccess = (MemberAccessExpressionSyntax)originalNode.Expression;

            // Bail out on extension method invocations in conditional access expression.
            // Note that this is a temporary workaround for https://github.com/dotnet/roslyn/issues/2593.
            // Issue https://github.com/dotnet/roslyn/issues/3260 tracks fixing this workaround.
            if (originalMemberAccess.GetRootConditionalAccessExpression() == null)
            {
                var speculationPosition = originalNode.SpanStart;
                var expression = RewriteExtensionMethodInvocation(speculationPosition, rewrittenNode, thisExpression, reducedExtensionMethod);

                // Let's rebind this and verify the original method is being called properly
                var binding = _semanticModel.GetSpeculativeSymbolInfo(originalNode.SpanStart, expression, SpeculativeBindingOption.BindAsExpression);
                if (binding.Symbol != null)
                    return expression;
            }

            return rewrittenNode;
        }

        private InvocationExpressionSyntax RewriteExtensionMethodInvocation(
            int speculationPosition,
            InvocationExpressionSyntax originalNode,
            ExpressionSyntax thisExpression,
            IMethodSymbol reducedExtensionMethod)
        {
            // It may be the case that this extension method cannot be called in static form.  For example, if the
            // qualified name for the type containing the extension would be ambiguous.  In that case, just return
            // the original call as is.
            var containingTypeString = reducedExtensionMethod.ContainingType.ToDisplayString(s_typeNameFormatWithGenerics);

            // We use .ParseExpression here, and not .GenerateTypeSyntax as we want this to be a property
            // MemberAccessExpression, and not a QualifiedNameSyntax.
            var containingTypeSyntax = ParseExpression(containingTypeString);
            var newContainingType = _semanticModel.GetSpeculativeSymbolInfo(speculationPosition, containingTypeSyntax, SpeculativeBindingOption.BindAsExpression).Symbol;
            if (newContainingType == null || !newContainingType.Equals(reducedExtensionMethod.ContainingType))
                return originalNode;

            var originalMemberAccess = (MemberAccessExpressionSyntax)originalNode.Expression;
            var newMemberAccess = originalMemberAccess.WithExpression(containingTypeSyntax)
                                                      .WithLeadingTrivia(thisExpression.GetFirstToken().LeadingTrivia);

            // Copies the annotation for the member access expression
            newMemberAccess = originalNode.Expression.CopyAnnotationsTo(newMemberAccess).WithAdditionalAnnotations(Simplifier.Annotation);

            var thisArgument = Argument(thisExpression).WithLeadingTrivia(SyntaxTriviaList.Empty);

            // Copies the annotation for the left hand side of the member access expression to the first argument in the complexified form
            thisArgument = originalMemberAccess.Expression.CopyAnnotationsTo(thisArgument);

            var arguments = originalNode.ArgumentList.Arguments.Insert(0, thisArgument);
            var replacementNode = InvocationExpression(
                newMemberAccess,
                originalNode.ArgumentList.WithArguments(arguments));

            // This Annotation copy is for the InvocationExpression
            return originalNode.CopyAnnotationsTo(replacementNode)
                               .WithAdditionalAnnotations(Simplifier.Annotation, Formatter.Annotation);
        }
    }
}
