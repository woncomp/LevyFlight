using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LevyFlight
{
    public enum ProjectScannerScope
    {
        Solution,
        ActiveProject,
        CurrentProject,
    }

    /// <summary>
    /// Shared quick-open state used by scanners during one discovery pass.
    /// </summary>
    internal sealed class ScannerContext
    {
        private List<Project> allProjects;
        private Project currentProject;
        private HashSet<Project> activeProjects;
        private Dictionary<string, string> claimOwners;

        public ScannerContext(string currentFile)
        {
            CurrentFile = currentFile;
            KnownFiles = new HashSet<string>();
            if (!string.IsNullOrEmpty(currentFile))
            {
                KnownFiles.Add(currentFile);
            }

            ActiveFiles = LevyFlightWindowCommand.Instance.GetActiveFiles();
            RecentFiles = TransitionStore.Instance.Recents;
            Transitions = TransitionStore.Instance.GetTransitionsForFile(currentFile);
            RecentEnd = Math.Max(Math.Min(20, RecentFiles.Count), RecentFiles.Count * 3 / 4);
            TransitionEnd = Math.Max(Math.Min(20, Transitions.Count), Transitions.Count * 3 / 4);
        }

        public string CurrentFile { get; }

        internal ScannerDump DumpSession { get; set; }

        public HashSet<string> KnownFiles { get; }

        public string[] ActiveFiles { get; }

        public IReadOnlyList<string> RecentFiles { get; }

        public IReadOnlyList<TransitionRecord> Transitions { get; }

        public int RecentIndex { get; set; }

        public int TransitionIndex { get; set; }

        public int RecentEnd { get; }

        public int TransitionEnd { get; }

        internal void EnableClaimTracking()
        {
            claimOwners = new Dictionary<string, string>();
        }

        internal bool TryClaimFile(string filePath, string owner)
        {
            if (string.IsNullOrEmpty(filePath) || KnownFiles.Contains(filePath))
            {
                return false;
            }

            KnownFiles.Add(filePath);
            if (owner != null && claimOwners != null)
            {
                claimOwners[filePath] = owner;
            }
            return true;
        }

        internal string DescribeClaimFailure(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return "empty path";
            }

            string owner;
            if (claimOwners != null && claimOwners.TryGetValue(filePath, out owner))
            {
                return "already claimed by " + owner;
            }

            if (string.Equals(filePath, CurrentFile, StringComparison.OrdinalIgnoreCase))
            {
                return "current file";
            }

            return "already known";
        }

        internal void ClaimFile(string filePath, string owner)
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                KnownFiles.Add(filePath);
                if (owner != null && claimOwners != null)
                {
                    claimOwners[filePath] = owner;
                }
            }
        }

        internal JumpItem CreateClaimedFileItem(Scanner scanner, string filePath, ScannerDumpSection dump)
        {
            if (TryClaimFile(filePath, scanner.Id))
            {
                return scanner.CreateJumpItem(filePath);
            }

            dump.Discarded(filePath, DescribeClaimFailure(filePath));
            return null;
        }

        internal IEnumerable<JumpItem> EnumerateProjectFileItems(Scanner scanner, ProjectScannerScope scope, ScannerDumpSection dump)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            EnsureProjects();
            dump.Detail("scope: " + scope + ", projects in solution: " + allProjects.Count);

            foreach (Project project in allProjects)
            {
                if (!MatchesScope(project, scope))
                {
                    dump.Detail("skip project " + project.Name + ": out of scope");
                    continue;
                }

                foreach (ProjectItem item in LevyFlightWindowCommand.Instance.EnumerateProjectItems(project.ProjectItems))
                {
                    string filePath = item.FileNames[0];
                    if (filePath.Contains(project.FullName))
                    {
                        dump.Discarded(filePath, "project file itself");
                        continue;
                    }

                    dump.Input(filePath);
                    JumpItem jumpItem = CreateClaimedFileItem(scanner, filePath, dump);
                    if (jumpItem != null)
                    {
                        dump.Produced(filePath);
                        yield return jumpItem;
                    }
                }
            }
        }

        private void EnsureProjects()
        {
            if (allProjects != null)
            {
                return;
            }

            ThreadHelper.ThrowIfNotOnUIThread();
            DTE ide = LevyFlightWindowCommand.GetActiveIDE();
            allProjects = new List<Project>();
            foreach (Project rootProject in ide.Solution.Projects)
            {
                allProjects.AddRange(LevyFlightWindowCommand.ExpandProjectRecursive(rootProject));
            }

            currentProject = ide.ActiveDocument?.ProjectItem?.ContainingProject;
            activeProjects = new HashSet<Project>(LevyFlightWindowCommand.Instance.FindActiveProjects(new HashSet<Project>(allProjects)));
        }

        private bool MatchesScope(Project project, ProjectScannerScope scope)
        {
            switch (scope)
            {
                case ProjectScannerScope.CurrentProject:
                    return project == currentProject;
                case ProjectScannerScope.ActiveProject:
                    return project != currentProject && activeProjects.Contains(project);
                case ProjectScannerScope.Solution:
                    return project != currentProject && !activeProjects.Contains(project);
                default:
                    throw new ArgumentOutOfRangeException(nameof(scope));
            }
        }
    }
}
