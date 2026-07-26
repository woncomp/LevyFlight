using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace LevyFlight
{
    public abstract class ProjectFileScanner : Scanner
    {
        protected abstract ProjectScannerScope Scope { get; }

        internal override async Task<IEnumerable<JumpItem>> ScanAsync(ScannerContext context)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            return context.EnumerateProjectFileItems(this, Scope);
        }
    }

    public sealed class SolutionFileScanner : ProjectFileScanner
    {
        public override string Id => "SolutionFile";
        public override string DisplayName => "Solution File";
        protected override ProjectScannerScope Scope => ProjectScannerScope.Solution;
    }

    public sealed class ActiveProjectFileScanner : ProjectFileScanner
    {
        public override string Id => "ActiveProjectFile";
        public override string DisplayName => "Active Project File";
        protected override ProjectScannerScope Scope => ProjectScannerScope.ActiveProject;
    }

    public sealed class CurrentProjectFileScanner : ProjectFileScanner
    {
        public override string Id => "CurrentProjectFile";
        public override string DisplayName => "Current Project File";
        protected override ProjectScannerScope Scope => ProjectScannerScope.CurrentProject;
    }
}
