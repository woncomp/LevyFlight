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

        public HashSet<string> KnownFiles { get; }

        public string[] ActiveFiles { get; }

        public IReadOnlyList<string> RecentFiles { get; }

        public IReadOnlyList<TransitionRecord> Transitions { get; }

        public int RecentIndex { get; set; }

        public int TransitionIndex { get; set; }

        public int RecentEnd { get; }

        public int TransitionEnd { get; }

        public bool TryClaimFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || KnownFiles.Contains(filePath))
            {
                return false;
            }

            KnownFiles.Add(filePath);
            return true;
        }

        public void ClaimFile(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                KnownFiles.Add(filePath);
            }
        }

        public JumpItem CreateClaimedFileItem(Scanner scanner, string filePath)
        {
            return TryClaimFile(filePath) ? scanner.CreateJumpItem(filePath) : null;
        }

        public IEnumerable<JumpItem> EnumerateProjectFileItems(Scanner scanner, ProjectScannerScope scope)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            EnsureProjects();

            foreach (Project project in allProjects)
            {
                if (!MatchesScope(project, scope))
                {
                    continue;
                }

                foreach (ProjectItem item in LevyFlightWindowCommand.Instance.EnumerateProjectItems(project.ProjectItems))
                {
                    if (item.FileNames[0].Contains(project.FullName))
                    {
                        continue;
                    }

                    JumpItem jumpItem = CreateClaimedFileItem(scanner, item.FileNames[0]);
                    if (jumpItem != null)
                    {
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
