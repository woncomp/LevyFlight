using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace LevyFlight
{
    public sealed class ActiveDirectoryFileScanner : Scanner
    {
        public override string Id => "ActiveDirectoryFile";
        public override string DisplayName => "Active Directory File";

        internal override Task<IEnumerable<JumpItem>> ScanAsync(ScannerContext context)
        {
            return Task.FromResult(EnumerateItems(context));
        }

        private IEnumerable<JumpItem> EnumerateItems(ScannerContext context)
        {
            var knownFolders = new HashSet<string>();
            foreach (string activeFile in context.ActiveFiles)
            {
                string currentFolder = Path.GetDirectoryName(activeFile);
                if (!Directory.Exists(currentFolder) || knownFolders.Contains(currentFolder))
                {
                    continue;
                }

                knownFolders.Add(currentFolder);
                foreach (string filePath in Directory.GetFiles(currentFolder))
                {
                    if (CommonMixin.IsExcluded(filePath))
                    {
                        continue;
                    }

                    JumpItem jumpItem = context.CreateClaimedFileItem(this, filePath);
                    if (jumpItem != null)
                    {
                        yield return jumpItem;
                    }
                }
            }
        }
    }
}
