﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.DotNet.UpgradeAssistant.Extensions.Default.Analyzers.Common;

using CS = Microsoft.CodeAnalysis.CSharp;
using CSSyntax = Microsoft.CodeAnalysis.CSharp.Syntax;

using VB = Microsoft.CodeAnalysis.VisualBasic;
using VBSyntax = Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Microsoft.DotNet.UpgradeAssistant.Extensions.Default
{
    public static class SyntaxNodeExtensions
    {
        public static bool IsVisualBasic(this SyntaxNode node) => node?.Language == LanguageNames.VisualBasic;

        public static bool IsCSharp(this SyntaxNode node) => node?.Language == LanguageNames.CSharp;

        public static SyntaxNode AddArgumentToInvocation(this SyntaxNode invocationNode, SyntaxNode argument)
        {
            if (invocationNode.IsVisualBasic())
            {
                var node = (VBSyntax.InvocationExpressionSyntax)invocationNode;
                return node.WithArgumentList(node.ArgumentList.AddArguments((VBSyntax.ArgumentSyntax)argument));
            }
            else if (invocationNode.IsCSharp())
            {
                var node = (CSSyntax.InvocationExpressionSyntax)invocationNode;
                return node.WithArgumentList(node.ArgumentList.AddArguments((CSSyntax.ArgumentSyntax)argument));
            }

            throw new NotImplementedException(Resources.UnknownLanguage);
        }

        /// <summary>
        /// Handles language aware selection of QualifiedNameSyntax or IdentifierNameSyntaxNode from current context.
        /// </summary>
        /// <param name="importOrBaseListSyntax">Shuold be an ImportStatementSyntax for VB or a BaseListSyntax for CS.</param>
        /// <returns>Null, QualifiedNameSyntaxNode, or IdentifierNameSyntaxNode.</returns>
        public static SyntaxNode? GetSyntaxIdentifierForBaseType(this SyntaxNode importOrBaseListSyntax)
        {
            if (importOrBaseListSyntax is null)
            {
                return null;
            }

            if (importOrBaseListSyntax.IsQualifiedName() || importOrBaseListSyntax.IsIdentifierName())
            {
                return importOrBaseListSyntax;
            }
            else if (!importOrBaseListSyntax.IsBaseTypeSyntax())
            {
                return null;
            }

            var baseTypeNode = importOrBaseListSyntax.DescendantNodes(descendIntoChildren: node => true)
                .FirstOrDefault(node => node.IsQualifiedName() || node.IsIdentifierName());

            return baseTypeNode;
        }

        /// <summary>
        /// A language agnostic specification that checks if a node is a QualifiedName.
        /// </summary>
        /// <param name="node">any SyntaxNode.</param>
        /// <returns>True if the node IsKind(SyntaxKind.QualifiedName).</returns>
        public static bool IsQualifiedName(this SyntaxNode node)
        {
            if (node is null)
            {
                return false;
            }

            return node.IsKind(CS.SyntaxKind.QualifiedName)
            || node.IsKind(VB.SyntaxKind.QualifiedName);
        }

        /// <summary>
        /// A language agnostic specification that checks if a node is a IdentifierName.
        /// </summary>
        /// <param name="node">any SyntaxNode.</param>
        /// <returns>True if the node IsKind(SyntaxKind.IdentifierName).</returns>
        public static bool IsIdentifierName(this SyntaxNode node)
        {
            if (node is null)
            {
                return false;
            }

            return node.IsKind(CS.SyntaxKind.IdentifierName)
            || node.IsKind(VB.SyntaxKind.IdentifierName);
        }

        /// <summary>
        /// A language agnostic specification that checks if a node is one of
        /// SyntaxKind.BaseList, SyntaxKind.SimpleBaseType, or SyntaxKind.InheritsStatement.
        /// </summary>
        /// <param name="node">any SyntaxNode.</param>
        /// <returns>True if the node IsKind of SyntaxKind.BaseList,
        /// SyntaxKind.SimpleBaseType, or SyntaxKind.InheritsStatement.</returns>
        public static bool IsBaseTypeSyntax(this SyntaxNode node)
        {
            if (node is null)
            {
                return false;
            }

            return node.IsKind(CS.SyntaxKind.BaseList)
            || node.IsKind(CS.SyntaxKind.SimpleBaseType)
            || node.IsKind(VB.SyntaxKind.InheritsStatement);
        }

        /// <summary>
        /// Gets the simple name of an identifier syntax. Works for both C# and VB.
        /// </summary>
        /// <param name="node">The IdentifierNameSyntax node to get the simple name for.</param>
        /// <returns>The identifier's simple name.</returns>
        public static string GetSimpleName(this SyntaxNode node) =>
            node switch
            {
                CSSyntax.IdentifierNameSyntax csIdentifier => csIdentifier.Identifier.ValueText,
                VBSyntax.IdentifierNameSyntax vbIdentifier => vbIdentifier.Identifier.ValueText,
                _ => throw new ArgumentException("Syntax node must be an IdentifierNameSyntax to get its simple name", nameof(node))
            };

        /// <summary>
        /// Finds the fully qualified name syntax or member access expression syntax (if any)
        /// that a node is part of and returns that larger, fully qualified syntax node.
        /// </summary>
        /// <param name="node">
        /// The identifier syntax node to find a fully qualified ancestor for. This should be of type
        /// Microsoft.CodeAnalysis.CSharp.Syntax.NameSyntax or Microsoft.CodeAnalysis.VisualBasic.Syntax.NameSyntax.
        /// </param>
        /// <returns>
        /// Returns the qualified name syntax or member access expression syntax containing the provided
        /// name syntax. If the node is not part of a larger qualified name, the input node will be returned.
        /// </returns>
        public static SyntaxNode GetQualifiedName(this SyntaxNode node)
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            // If the node is part of a qualified name, we want to get the full qualified name
            while (node.Parent is CSSyntax.NameSyntax || node.Parent is VBSyntax.NameSyntax)
            {
                node = node.Parent;
            }

            // If the node is part of a member access expression (a static member access, for example), then the
            // qualified name will be a member access expression rather than a name syntax.
            if ((node.Parent is CSSyntax.MemberAccessExpressionSyntax csMAE && csMAE.Name.IsEquivalentTo(node))
                || (node.Parent is VBSyntax.MemberAccessExpressionSyntax vbMAE && vbMAE.Name.IsEquivalentTo(node)))
            {
                node = node.Parent;
            }

            return node;
        }

        /// <summary>
        /// Gets a method declared in a given syntax node.
        /// </summary>
        /// <typeparam name="T">The type of method declaration syntax node to search for.</typeparam>
        /// <param name="node">The syntax node to find the method declaration in.</param>
        /// <param name="methodName">The name of the method to return.</param>
        /// <param name="requiredParameterTypes">An optional list of parameter types that the method must accept.</param>
        /// <returns>The first method declaration in the syntax node with the given name and parameter types, or null if no such method declaration exists.</returns>
        public static T? GetMethodDeclaration<T>(this SyntaxNode node, string methodName, params string[] requiredParameterTypes)
            where T : SyntaxNode
        {
            var methodDeclNodes = node?.DescendantNodes().OfType<T>();

            return methodDeclNodes.FirstOrDefault(m =>
                    (m is CSSyntax.MethodDeclarationSyntax csM &&
                    csM.Identifier.ToString().Equals(methodName, StringComparison.Ordinal) &&
                    requiredParameterTypes.All(req => csM.ParameterList.Parameters.Any(p => string.Equals(p.Type?.ToString(), req, StringComparison.Ordinal))))
                || (m is VBSyntax.MethodStatementSyntax vbM &&
                    vbM.Identifier.ToString().Equals(methodName, StringComparison.Ordinal) &&
                    requiredParameterTypes.All(req => vbM.ParameterList.Parameters.Any(p => string.Equals(p.AsClause?.Type?.ToString(), req, StringComparison.Ordinal)))));
        }

        /// <summary>
        /// Applies whitespace trivia and new line trivia from another syntax node to this one.
        /// </summary>
        /// <typeparam name="T">The type of syntax node to be updated.</typeparam>
        /// <param name="statement">The syntax node to update.</param>
        /// <param name="otherStatement">The syntax node to copy trivia from.</param>
        /// <returns>The original syntax node updated with the other syntax's whitespace and new line trivia.</returns>
        public static T WithWhitespaceTriviaFrom<T>(this T statement, SyntaxNode otherStatement)
            where T : SyntaxNode
        {
            return statement
                .WithLeadingTrivia(otherStatement?.GetLeadingTrivia().Where(IsWhitespaceTrivia) ?? SyntaxTriviaList.Empty)
                .WithTrailingTrivia(otherStatement?.GetTrailingTrivia().Where(IsWhitespaceTrivia) ?? SyntaxTriviaList.Empty);

            static bool IsWhitespaceTrivia(SyntaxTrivia trivia) =>
                CSharpExtensions.IsKind(trivia, CS.SyntaxKind.EndOfLineTrivia)
                || CSharpExtensions.IsKind(trivia, CS.SyntaxKind.WhitespaceTrivia)
                || VisualBasicExtensions.IsKind(trivia, VB.SyntaxKind.EndOfLineTrivia)
                || VisualBasicExtensions.IsKind(trivia, VB.SyntaxKind.WhitespaceTrivia);
        }

        /// <summary>
        /// Determines if a syntax node includes a using or import statement for a given namespace.
        /// Will not return true if the node's children include the specified using/import but the node itself does not.
        /// </summary>
        /// <param name="node">The node to analyze.</param>
        /// <param name="namespaceName">The namespace name to check for.</param>
        /// <returns>True if the node has a direct import or using statement for the given namespace. False otherwise.</returns>
        public static bool HasUsingStatement(this SyntaxNode node, string namespaceName)
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            // Descend only into VB import statements
            var nodes = node.DescendantNodesAndSelf(n => VisualBasicExtensions.IsKind(n, VB.SyntaxKind.ImportsStatement), false);
            var children = node.ChildNodes();

            var usings = children.OfType<CSSyntax.UsingDirectiveSyntax>().Select(u => u.Name.ToString())
                .Concat(children.OfType<VBSyntax.SimpleImportsClauseSyntax>().Select(i => i.Name.ToString()))
                .Concat(children.OfType<VBSyntax.ImportsStatementSyntax>().SelectMany(i => i.ImportsClauses.OfType<VBSyntax.SimpleImportsClauseSyntax>().Select(i => i.Name.ToString())));

            return usings.Any(n => n.Equals(namespaceName, StringComparison.Ordinal));
        }

        /// <summary>
        /// Determines if a syntax node is in a scope that includes a using or import statement for a given namespace.
        /// </summary>
        /// <param name="node">The node to analyze for access to the namespace in its scope.</param>
        /// <param name="namespaceName">The namespace name to check for.</param>
        /// <returns>True if the node is in a syntax tree with the given namespace in scope. False otherwise.</returns>
        public static bool HasAccessToNamespace(this SyntaxNode node, string namespaceName)
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            return node.AncestorsAndSelf().Any(n => n.HasUsingStatement(namespaceName));
        }

        /// <summary>
        /// Adds a using directive for a given namespace to the document root only if the directive is not already present.
        /// </summary>
        /// <param name="documentRoot">The document to add the directive to.</param>
        /// <param name="namespaceName">The namespace to reference with the using directive.</param>
        /// <returns>An updated document root with the specific using directive.</returns>
        public static CSSyntax.CompilationUnitSyntax AddUsingIfMissing(this CSSyntax.CompilationUnitSyntax documentRoot, string namespaceName)
        {
            if (documentRoot is null)
            {
                throw new ArgumentNullException(nameof(documentRoot));
            }

            // TODO - remove this helper and use ImportAdder instead.
            var anyUsings = documentRoot.Usings.Any(u => u.Name.ToString().Equals(namespaceName, StringComparison.Ordinal));
            var usingDirective = CS.SyntaxFactory.UsingDirective(CS.SyntaxFactory.ParseName(namespaceName).WithLeadingTrivia(CS.SyntaxFactory.Whitespace(" ")));
            var result = anyUsings ? documentRoot : documentRoot.AddUsings(usingDirective);

            return result;
        }

        /// <summary>
        /// Determines whether a node is a NameSyntax (either C# or VB).
        /// </summary>
        /// <param name="node">The node to inspect.</param>
        /// <returns>True if the node derives from Microsoft.CodeAnalysis.CSharp.Syntax.NameSyntax or Microsoft.CodeAnalysis.VisualBasic.Syntax.NameSyntax, false otherwise.</returns>
        public static bool IsNameSyntax(this SyntaxNode node) => node is CSSyntax.NameSyntax || node is VBSyntax.NameSyntax;

        /// <summary>
        /// Determines whether a node is a MemberAccessExpressionSyntax (either C# or VB).
        /// </summary>
        /// <param name="node">The node to inspect.</param>
        /// <returns>True if the node derives from Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax or Microsoft.CodeAnalysis.VisualBasic.Syntax.MemberAccessExpressionSyntax, false otherwise.</returns>
        public static bool IsMemberAccessExpressionSyntax(this SyntaxNode node) => node is CSSyntax.MemberAccessExpressionSyntax || node is VBSyntax.MemberAccessExpressionSyntax;

        public static SyntaxNode? GetInvocationExpression(this SyntaxNode callerNode)
        {
            if (callerNode.IsVisualBasic())
            {
                return callerNode.FirstAncestorOrSelf<VBSyntax.InvocationExpressionSyntax>();
            }
            else if (callerNode.IsCSharp())
            {
                return callerNode.FirstAncestorOrSelf<CSSyntax.InvocationExpressionSyntax>();
            }

            throw new NotImplementedException(Resources.UnknownLanguage);
        }
    }
}
