﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Simplification;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace AspNetMigrator.Analyzers
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = "AM005 CodeFix Provider")]
    public class HttpContextCurrentCodeFixer : CodeFixProvider
    {
        private const string HttpContextHelperName = "HttpContextHelper";
        private const string HttpContextHelperResourceName = "AspNetMigrator.Analyzers.Templates.HttpContextHelper.cs";

        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(HttpContextCurrentAnalyzer.DiagnosticId);

        public sealed override FixAllProvider GetFixAllProvider()
        {
            // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var node = root.FindNode(context.Span, false, true);

            if (node == null)
            {
                return;
            }

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    CodeFixResources.HttpContextCurrentTitle,
                    cancellationToken => ReplaceHttpContextCurrentAsync(context.Document, node, cancellationToken),
                    nameof(CodeFixResources.HttpContextCurrentTitle)),
                context.Diagnostics);
        }

        private static async Task<Solution> ReplaceHttpContextCurrentAsync(Document document, SyntaxNode node, CancellationToken cancellationToken)
        {
            var project = document.Project;

            // Ensure HttpContextHelper.cs exists in the project
            var httpContextHelperClass = await GetHttpContextHelperClassAsync(project).ConfigureAwait(false);
            if (httpContextHelperClass is null)
            {
                using var sr = new StreamReader(typeof(HttpContextCurrentCodeFixer).Assembly.GetManifestResourceStream(HttpContextHelperResourceName));
                project = document.Project.AddDocument($"{HttpContextHelperName}.cs", await sr.ReadToEndAsync().ConfigureAwait(false)).Project;
                httpContextHelperClass = await GetHttpContextHelperClassAsync(project).ConfigureAwait(false);
            }

            var slnEditor = new SolutionEditor(project.Solution);

            // Update Startup.cs to call HttpContextHelper.Initialize
            var startup = project.Documents.FirstOrDefault(d => d.Name.Equals("Startup.cs", StringComparison.OrdinalIgnoreCase));
            if (startup is null)
            {
                return null;
            }

            var startupDocEditor = await slnEditor.GetDocumentEditorAsync(startup.Id, cancellationToken).ConfigureAwait(false);
            InitializeHttpContextHelperInStartup(startupDocEditor, httpContextHelperClass);

            // Update the HttpContext.Current usage to use HttpContextHelper
            var docEditor = await slnEditor.GetDocumentEditorAsync(document.Id, cancellationToken).ConfigureAwait(false);
            var docRoot = docEditor.OriginalRoot as CompilationUnitSyntax;

            // Add a using statement, if necessary
            docRoot = docRoot.AddUsingIfMissing(httpContextHelperClass.ContainingNamespace.ToString());

            // Update the HttpContext.Current reference
            var replacementSyntax = ParseExpression($"{httpContextHelperClass.Name}.Current")
                .WithTriviaFrom(node);
            docRoot = docRoot.ReplaceNode(node, replacementSyntax);
            docEditor.ReplaceNode(docEditor.OriginalRoot, docRoot);

            return slnEditor.GetChangedSolution();
        }

        private static async Task<INamedTypeSymbol> GetHttpContextHelperClassAsync(Project project)
        {
            foreach (var document in project.Documents)
            {
                var syntaxRoot = await document.GetSyntaxRootAsync().ConfigureAwait(false);

                // Find all classes named "HttpContextHelper"
                var candidateClasses = syntaxRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().Where(c => c.Identifier.ToString().Equals(HttpContextHelperName, StringComparison.Ordinal));

                // Find the first HttpContextHelperClass with a public static property named Current that returns an HttpContext
                var httpContextClass = candidateClasses.FirstOrDefault(c => c.Members.OfType<PropertyDeclarationSyntax>().Select(p =>
                    p.Identifier.ToString().Equals("Current", StringComparison.Ordinal) &&
                    p.Modifiers.Contains(SyntaxFactory.Token(SyntaxKind.PublicKeyword)) &&
                    p.Modifiers.Contains(SyntaxFactory.Token(SyntaxKind.StaticKeyword)) &&
                    p.Type.ToString().Contains("HttpContext")).Any());

                if (httpContextClass != null)
                {
                    var semanticModel = await document.GetSemanticModelAsync().ConfigureAwait(false);
                    return semanticModel?.GetDeclaredSymbol(httpContextClass);
                }
            }

            return null;
        }

        private static void InitializeHttpContextHelperInStartup(DocumentEditor editor, INamedTypeSymbol httpContextHelperClass)
        {
            var documentRoot = editor.OriginalRoot as CompilationUnitSyntax;

            // Add using declarations if needed
            documentRoot = documentRoot
                .AddUsingIfMissing(httpContextHelperClass.ContainingNamespace.ToString()) // For HttpContextHelper
                .AddUsingIfMissing("Microsoft.AspNetCore.Http") // For IHttpContextAccessor
                .AddUsingIfMissing("Microsoft.Extensions.DependencyInjection"); // For AddHttpContextAccessor

            // Add AddHttpContextAccessor call if needed
            var configureServicesMethod = documentRoot.GetMethodDeclaration("ConfigureServices", "IServiceCollection");
            var serviceCollectionParameter = configureServicesMethod?.ParameterList.Parameters.FirstOrDefault(p => p.Type.ToString().Equals("IServiceCollection", StringComparison.Ordinal));

            if (serviceCollectionParameter != null)
            {
                var configureServicesBody = configureServicesMethod.Body;
                var addHttpContextAccessorStatement = ParseStatement($"{serviceCollectionParameter.Identifier}.AddHttpContextAccessor();")
                    .WithWhitespaceTriviaFrom(configureServicesBody.Statements.First());

                // Check whether the statement already exists
                if (!configureServicesBody.Statements.Any(s => addHttpContextAccessorStatement.IsEquivalentTo(s)))
                {
                    documentRoot = documentRoot.ReplaceNode(configureServicesBody, configureServicesBody.AddStatements(addHttpContextAccessorStatement));
                }
            }

            // Add Initialize call in Configure method
            var configureMethod = documentRoot.GetMethodDeclaration("Configure", "IApplicationBuilder");
            var appBuilderParameter = configureMethod?.ParameterList.Parameters.FirstOrDefault(p => p.Type.ToString().Equals("IApplicationBuilder", StringComparison.Ordinal));

            if (appBuilderParameter != null)
            {
                var configureMethodBody = configureMethod.Body;
                var initializeStatement = ParseStatement($"{httpContextHelperClass.Name}.Initialize({appBuilderParameter.Identifier}.ApplicationServices.GetRequiredService<IHttpContextAccessor>());")
                    .WithWhitespaceTriviaFrom(configureMethodBody.Statements.First());

                // Check whether the statement already exists
                if (!configureMethodBody.Statements.Any(s => initializeStatement.IsEquivalentTo(s)))
                {
                    var updatedMethodBody = configureMethodBody.WithStatements(configureMethodBody.Statements.Insert(0, initializeStatement));
                    documentRoot = documentRoot.ReplaceNode(configureMethodBody, updatedMethodBody);
                }
            }

            editor.ReplaceNode(editor.OriginalRoot, documentRoot);
        }
    }
}
