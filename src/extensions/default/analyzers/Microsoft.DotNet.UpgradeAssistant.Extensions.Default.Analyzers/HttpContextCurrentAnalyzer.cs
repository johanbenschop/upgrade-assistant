﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

using CS = Microsoft.CodeAnalysis.CSharp;
using CSSyntax = Microsoft.CodeAnalysis.CSharp.Syntax;

using VB = Microsoft.CodeAnalysis.VisualBasic;
using VBSyntax = Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Microsoft.DotNet.UpgradeAssistant.Extensions.Default.Analyzers
{
    [ApplicableComponents(ProjectComponents.AspNetCore)]
    [DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
    public sealed class HttpContextCurrentAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "UA0005";
        private const string Category = "Upgrade";

        private const string TargetTypeSimpleName = "HttpContext";
        private const string TargetTypeSymbolName = "System.Web.HttpContext";
        private const string TargetMember = "Current";

        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.HttpContextCurrentTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.HttpContextCurrentMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.HttpContextCurrentDescription), Resources.ResourceManager, typeof(Resources));

        private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

            context.RegisterCompilationStartAction(context =>
            {
                if (context.Compilation.Language == LanguageNames.CSharp)
                {
                    context.RegisterSyntaxNodeAction(AnalyzeMemberAccessExpressionsCsharp, CS.SyntaxKind.SimpleMemberAccessExpression);
                }
                else if (context.Compilation.Language == LanguageNames.VisualBasic)
                {
                    context.RegisterSyntaxNodeAction(AnalyzeMemberAccessExpressionsVb, VB.SyntaxKind.SimpleMemberAccessExpression);
                }
            });
        }

        private void AnalyzeMemberAccessExpressionsCsharp(SyntaxNodeAnalysisContext context)
        {
            var memberAccessExpression = (CSSyntax.MemberAccessExpressionSyntax)context.Node;

            // If the accessed member isn't named "Current" bail out
            if (!TargetMember.Equals(memberAccessExpression.Name.Identifier.ValueText, StringComparison.Ordinal))
            {
                return;
            }

            // If the call is to a method called Current then bail out since they're
            // not using the HttpContext.Current property
            if (memberAccessExpression.Parent is CSSyntax.InvocationExpressionSyntax)
            {
                return;
            }

            // Get the identifier accessed
            var accessedIdentifier = memberAccessExpression.Expression switch
            {
                CSSyntax.IdentifierNameSyntax i => i,
                CSSyntax.MemberAccessExpressionSyntax m => m.DescendantNodes().OfType<CSSyntax.IdentifierNameSyntax>().LastOrDefault(),
                _ => null
            };

            AnalyzeMemberAccessExpressions(context, memberAccessExpression, accessedIdentifier, accessedIdentifier?.Identifier.ValueText);
        }

        private void AnalyzeMemberAccessExpressionsVb(SyntaxNodeAnalysisContext context)
        {
            var memberAccessExpression = (VBSyntax.MemberAccessExpressionSyntax)context.Node;

            // If the accessed member isn't named "Current" bail out
            if (!TargetMember.Equals(memberAccessExpression.Name.Identifier.ValueText, StringComparison.Ordinal))
            {
                return;
            }

            // If the call is to a method called Current then bail out since they're
            // not using the HttpContext.Current property
            if (memberAccessExpression.Parent is VBSyntax.InvocationExpressionSyntax)
            {
                return;
            }

            // Get the identifier accessed
            var accessedIdentifier = memberAccessExpression.Expression switch
            {
                VBSyntax.IdentifierNameSyntax i => i,
                VBSyntax.MemberAccessExpressionSyntax m => m.DescendantNodes().OfType<VBSyntax.IdentifierNameSyntax>().LastOrDefault(),
                _ => null
            };

            AnalyzeMemberAccessExpressions(context, memberAccessExpression, accessedIdentifier, accessedIdentifier?.Identifier.ValueText);
        }

        private static void AnalyzeMemberAccessExpressions(SyntaxNodeAnalysisContext context, SyntaxNode memberAccessExpression, SyntaxNode? accessedIdentifier, string? identifierValue)
        {
            // Return if the accessed identifier wasn't from a simple member access expression or identifier, or if it doesn't match HttpContext
            if (accessedIdentifier is null || !TargetTypeSimpleName.Equals(identifierValue, StringComparison.Ordinal))
            {
                return;
            }

            if (!TryMatchSymbol(context, accessedIdentifier))
            {
                return;
            }

            var diagnostic = Diagnostic.Create(Rule, memberAccessExpression.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }

        /// <summary>
        /// Attempts to match against a symbol if there. If a symbol is resolved, it must match exactly. Otherwise, we just match on name.
        /// </summary>
        /// <param name="context">The analysis context.</param>
        /// <param name="accessedIdentifier">The accessedIdentifier that was found</param>
        /// <returns>Whether a symbol was found and was matched.</returns>
        private static bool TryMatchSymbol(SyntaxNodeAnalysisContext context, SyntaxNode accessedIdentifier)
        {
            // If the accessed identifier resolves to a type symbol other than System.Web.HttpContext, then bail out
            // since it means the user is calling some other similarly named API.
            var accessedSymbol = context.SemanticModel.GetSymbolInfo(accessedIdentifier).Symbol;
            if (accessedSymbol is INamedTypeSymbol symbol)
            {
                if (!symbol.ToDisplayString(NullableFlowState.NotNull).Equals(TargetTypeSymbolName, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            else if (accessedSymbol != null)
            {
                // If the accessed identifier resolves to a symbol other than a type symbol, bail out
                // since it's not a reference to System.Web.HttpContext
                return false;
            }

            return true;
        }
    }
}
