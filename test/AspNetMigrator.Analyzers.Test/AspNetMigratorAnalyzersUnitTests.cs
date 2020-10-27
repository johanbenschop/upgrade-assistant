﻿using AspNetMigrator.MSBuild;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using TestProject;

namespace AspNetMigrator.Analyzers.Test
{
    [TestClass]
    public class AspNetMigratorAnalyzersUnitTest
    {
        private static readonly Dictionary<string, ExpectedDiagnostic[]> ExpectedDiagnostics = new Dictionary<string, ExpectedDiagnostic[]>
        {
            {
                "AM0001",
                new[]
                {
                    new ExpectedDiagnostic("AM0001", new TextSpan(15, 17)),
                    new ExpectedDiagnostic("AM0001", new TextSpan(34, 23)),
                    new ExpectedDiagnostic("AM0001", new TextSpan(59, 37)),
                    new ExpectedDiagnostic("AM0001", new TextSpan(184, 11))
                }
            },
            {
                "AM0002",
                new[]
                {
                    new ExpectedDiagnostic("AM0002", new TextSpan(121, 11)),
                    new ExpectedDiagnostic("AM0002", new TextSpan(171, 10)),
                    new ExpectedDiagnostic("AM0002", new TextSpan(307, 10)),
                    new ExpectedDiagnostic("AM0002", new TextSpan(375, 13)),
                    new ExpectedDiagnostic("AM0002", new TextSpan(434, 13)),
                    new ExpectedDiagnostic("AM0002", new TextSpan(486, 13))
                }
            },
            {
                "AM0003",
                new[]
                {
                    new ExpectedDiagnostic("AM0003", new TextSpan(248, 10)),
                    new ExpectedDiagnostic("AM0003", new TextSpan(339, 14)),
                    new ExpectedDiagnostic("AM0003", new TextSpan(375, 14)),
                    new ExpectedDiagnostic("AM0003", new TextSpan(416, 12)),
                    new ExpectedDiagnostic("AM0003", new TextSpan(485, 12)),
                    new ExpectedDiagnostic("AM0003", new TextSpan(521, 10)),
                    new ExpectedDiagnostic("AM0003", new TextSpan(556, 14)),
                    new ExpectedDiagnostic("AM0003", new TextSpan(605, 18))
                }
            },
        };

        [AssemblyInitialize]
        public static void Initialize(TestContext _)
        {
            MSBuildHelper.RegisterMSBuildInstance();
        }

        //No diagnostics expected to show up
        [TestMethod]
        public async Task NegativeTest()
        {
            var diagnostics = await TestHelper.GetDiagnosticsAsync("Startup.cs", null).ConfigureAwait(false);

            Assert.AreEqual(0, diagnostics.Count());
        }

        [DataRow("AM0001")]
        [DataRow("AM0002")]
        [DataRow("AM0003")]
        [DataTestMethod]
        public async Task MigrationAnalyzers(string diagnosticId)
        {
            var diagnostics = await TestHelper.GetDiagnosticsAsync($"{diagnosticId}.cs", diagnosticId).ConfigureAwait(false);

            AssertDiagnosticsCorrect(diagnostics, ExpectedDiagnostics[diagnosticId]);
        }

        [DataRow("AM0001")]
        [DataRow("AM0002")]
        [DataRow("AM0003")]
        [DataTestMethod]
        public async Task MigrationCodeFixer(string diagnosticId)
        {
            var fixedSource = await TestHelper.FixSourceAsync($"{diagnosticId}.cs", diagnosticId).ConfigureAwait(false);
            var expectedSource = await TestHelper.GetSourceAsync($"{diagnosticId}.Fixed.cs").ConfigureAwait(false);

            var expectedText = (await expectedSource.GetTextAsync().ConfigureAwait(false)).ToString();
            var fixedText = (await fixedSource.GetTextAsync().ConfigureAwait(false)).ToString();
            Assert.AreEqual(expectedText, fixedText);
        }

        private static void AssertDiagnosticsCorrect(IEnumerable<Diagnostic> diagnostics, ExpectedDiagnostic[] expectedDiagnostics)
        {
            Assert.AreEqual(expectedDiagnostics.Length, diagnostics.Count());
            var count = 0;
            foreach (var d in diagnostics.OrderBy(d => d.Location.SourceSpan.Start))
            {
                Assert.IsTrue(expectedDiagnostics[count++].Equals(d), $"Expected diagnostic {count} to be at {expectedDiagnostics[count - 1].SourceSpan}; actually at {d.Location.SourceSpan}");
            }
        }
    }
}
