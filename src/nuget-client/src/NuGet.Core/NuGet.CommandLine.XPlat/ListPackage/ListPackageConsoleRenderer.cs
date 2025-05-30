// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NuGet.CommandLine.XPlat.Utility;
using NuGet.Configuration;

namespace NuGet.CommandLine.XPlat.ListPackage
{
    /// <summary>
    /// Console output renderer for dotnet list package command
    /// </summary>
    internal class ListPackageConsoleRenderer : IReportRenderer
    {
        protected List<ReportProblem> _problems = new();
        private readonly TextWriter _consoleOut;
        private readonly TextWriter _consoleError;

        public ListPackageConsoleRenderer()
            : this(Console.Out, Console.Error)
        { }

        internal ListPackageConsoleRenderer(TextWriter consoleOut, TextWriter consoleError)
        {
            _consoleOut = consoleOut;
            _consoleError = consoleError;
        }

        public void AddProblem(ProblemType problemType, string text)
        {
            _problems.Add(new ReportProblem(problemType, string.Empty, text));
        }

        public IEnumerable<ReportProblem> GetProblems()
        {
            return _problems;
        }

        public void Render(ListPackageReportModel listPackageReportModel)
        {
            WriteToConsole(listPackageReportModel);
        }

        private void WriteToConsole(ListPackageReportModel listPackageReportModel)
        {
            // Print non-project related problems first.
            PrintProblems(_consoleOut, _consoleError, _problems, listPackageReportModel.ListPackageArgs);

            if (_problems?.Any(p => p.ProblemType == ProblemType.Error) == true)
            {
                return;
            }

            if (listPackageReportModel.ListPackageArgs.ReportType == ReportType.Vulnerable && listPackageReportModel.AuditSourcesUsed.Count > 0)
            {
                _consoleOut.WriteLine();
                _consoleOut.WriteLine(Strings.ListPkg_SourcesUsedDescription);
                PrintSources(_consoleOut, listPackageReportModel.AuditSourcesUsed);
                _consoleOut.WriteLine();
            }
            else
            {
                WriteSources(_consoleOut, listPackageReportModel.ListPackageArgs);
            }

            WriteProjects(_consoleOut, _consoleError, listPackageReportModel.Projects, listPackageReportModel.ListPackageArgs);

            // Print a legend message for auto-reference markers used
            if (listPackageReportModel.Projects.Any(p => p.AutoReferenceFound))
            {
                _consoleOut.WriteLine(Strings.ListPkg_AutoReferenceDescription);
            }
        }

        private static void WriteSources(TextWriter consoleOut, ListPackageArgs listPackageArgs)
        {
            // Print sources, but not for generic list (which is offline)
            if (listPackageArgs.ReportType != ReportType.Default)
            {
                consoleOut.WriteLine();
                consoleOut.WriteLine(Strings.ListPkg_SourcesUsedDescription);
                PrintSources(consoleOut, listPackageArgs.PackageSources);
                consoleOut.WriteLine();
            }
        }

        private static void WriteProjects(TextWriter consoleOut, TextWriter consoleError, List<ListPackageProjectModel> projects, ListPackageArgs listPackageArgs)
        {
            foreach (ListPackageProjectModel project in projects)
            {
                PrintProblems(consoleOut, consoleError, project.ProjectProblems, listPackageArgs);

                if (project.ProjectProblems?.Any(p => p.ProblemType == ProblemType.Error) == true)
                {
                    return;
                }

                if (project.TargetFrameworkPackages == null)
                {
                    consoleOut.WriteLine(string.Format(CultureInfo.CurrentCulture, Strings.ListPkg_NoPackagesFoundForFrameworks, project.ProjectName));
                    continue;
                }

                bool printPackages = project.TargetFrameworkPackages.Any(p => p.TopLevelPackages?.Any() == true ||
                                                                            listPackageArgs.IncludeTransitive && p.TransitivePackages?.Any() == true);

                // Filter packages for dedicated reports, inform user if none
                if (listPackageArgs.ReportType != ReportType.Default && !printPackages)
                {
                    switch (listPackageArgs.ReportType)
                    {
                        case ReportType.Outdated:
                            consoleOut.WriteLine(string.Format(CultureInfo.CurrentCulture, Strings.ListPkg_NoUpdatesForProject, project.ProjectName));
                            break;
                        case ReportType.Deprecated:
                            consoleOut.WriteLine(string.Format(CultureInfo.CurrentCulture, Strings.ListPkg_NoDeprecatedPackagesForProject, project.ProjectName));
                            break;
                        case ReportType.Vulnerable:
                            consoleOut.WriteLine(string.Format(CultureInfo.CurrentCulture, Strings.ListPkg_NoVulnerablePackagesForProject, project.ProjectName));
                            break;
                    }
                }

                printPackages = printPackages || ReportType.Default == listPackageArgs.ReportType;
                if (!printPackages)
                {
                    continue;
                }

                consoleOut.WriteLine(GetProjectHeader(project.ProjectName, listPackageArgs));

                foreach (ListPackageReportFrameworkPackage frameworkPackages in project.TargetFrameworkPackages)
                {
                    List<ListReportPackage> frameworkTopLevelPackages = frameworkPackages.TopLevelPackages;
                    List<ListReportPackage> frameworkTransitivePackages = frameworkPackages.TransitivePackages;

                    // If no packages exist for this framework, print the
                    // appropriate message
                    if (frameworkTopLevelPackages?.Any() != true && frameworkTransitivePackages?.Any() != true)
                    {
                        Console.ForegroundColor = ConsoleColor.Blue;

                        switch (listPackageArgs.ReportType)
                        {
                            case ReportType.Outdated:
                                consoleOut.WriteLine(string.Format(CultureInfo.CurrentCulture, "   [{0}]: " + Strings.ListPkg_NoUpdatesForFramework, frameworkPackages.Framework));
                                break;
                            case ReportType.Deprecated:
                                consoleOut.WriteLine(string.Format(CultureInfo.CurrentCulture, "   [{0}]: " + Strings.ListPkg_NoDeprecationsForFramework, frameworkPackages.Framework));
                                break;
                            case ReportType.Vulnerable:
                                consoleOut.WriteLine(string.Format(CultureInfo.CurrentCulture, "   [{0}]: " + Strings.ListPkg_NoVulnerabilitiesForFramework, frameworkPackages.Framework));
                                break;
                            case ReportType.Default:
                                consoleOut.WriteLine(string.Format(CultureInfo.CurrentCulture, "   [{0}]: " + Strings.ListPkg_NoPackagesForFramework, frameworkPackages.Framework));
                                break;
                        }

                        Console.ResetColor();
                    }
                    else
                    {
                        // Print name of the framework
                        Console.ForegroundColor = ConsoleColor.Blue;
                        consoleOut.WriteLine(string.Format(CultureInfo.CurrentCulture, "   [{0}]: ", frameworkPackages.Framework));
                        Console.ResetColor();

                        // Print top-level packages
                        if (frameworkTopLevelPackages?.Any() == true)
                        {
                            var tableHasAutoReference = false;
                            var tableToPrint = ProjectPackagesPrintUtility.BuildPackagesTable(
                                frameworkTopLevelPackages, printingTransitive: false, listPackageArgs, ref tableHasAutoReference);
                            if (tableToPrint != null)
                            {
                                ProjectPackagesPrintUtility.PrintPackagesTable(tableToPrint);
                            }
                        }

                        // Print transitive packages
                        if (listPackageArgs.IncludeTransitive && frameworkTransitivePackages?.Any() == true)
                        {
                            var tableHasAutoReference = false;
                            var tableToPrint = ProjectPackagesPrintUtility.BuildPackagesTable(
                                frameworkTransitivePackages, printingTransitive: true, listPackageArgs, ref tableHasAutoReference);
                            if (tableToPrint != null)
                            {
                                ProjectPackagesPrintUtility.PrintPackagesTable(tableToPrint);
                            }
                        }
                    }
                }
            }
        }

        private static void PrintSources(TextWriter consoleOut, IEnumerable<PackageSource> packageSources)
        {
            foreach (var source in packageSources)
            {
                consoleOut.WriteLine("   " + source.Source);
            }
        }

        private static void PrintProblems(TextWriter consoleOut, TextWriter consoleError, IEnumerable<ReportProblem> problems, ListPackageArgs listPackageArgs)
        {
            if (problems == null)
            {
                return;
            }

            foreach (ReportProblem problem in problems)
            {
                switch (problem.ProblemType)
                {
                    case ProblemType.Warning:
                        listPackageArgs.Logger.LogWarning(problem.Text);
                        break;
                    case ProblemType.Error:
                        consoleError.WriteLine(problem.Text);
                        consoleOut.WriteLine();
                        break;
                    default:
                        break;
                }
            }
        }

        private static string GetProjectHeader(string projectName, ListPackageArgs listPackageArgs)
        {
            switch (listPackageArgs.ReportType)
            {
                case ReportType.Outdated:
                    return string.Format(Strings.ListPkg_ProjectUpdatesHeaderLog, projectName);
                case ReportType.Deprecated:
                    return string.Format(Strings.ListPkg_ProjectDeprecationsHeaderLog, projectName);
                case ReportType.Vulnerable:
                    return string.Format(Strings.ListPkg_ProjectVulnerabilitiesHeaderLog, projectName);
                case ReportType.Default:
                    break;
            }

            return string.Format(Strings.ListPkg_ProjectHeaderLog, projectName);
        }
    }
}
