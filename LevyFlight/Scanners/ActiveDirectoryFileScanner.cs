using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace LevyFlight
{
    public sealed class ActiveDirectoryFileScanner : Scanner
    {
        public override string Id => "ActiveDirectoryFile";
        public override string DisplayName => "Active Directory File";

        internal override Task<IEnumerable<JumpItem>> ScanAsync(ScannerContext context, ScannerDumpSection dump)
        {
            return Task.FromResult(EnumerateItems(context, dump));
        }

        private IEnumerable<JumpItem> EnumerateItems(ScannerContext context, ScannerDumpSection dump)
        {
            var knownFolders = new HashSet<string>();
            foreach (string activeFile in context.ActiveFiles)
            {
                string currentFolder = Path.GetDirectoryName(activeFile);
                if (!Directory.Exists(currentFolder))
                {
                    dump.Detail("skip folder " + (currentFolder ?? "<none>") + ": does not exist");
                    continue;
                }

                if (knownFolders.Contains(currentFolder))
                {
                    dump.Detail("skip folder " + currentFolder + ": already scanned");
                    continue;
                }

                knownFolders.Add(currentFolder);
                dump.Detail("scan folder " + currentFolder);
                foreach (string filePath in Directory.GetFiles(currentFolder))
                {
                    dump.Input(filePath);
                    if (CommonMixin.IsExcluded(filePath))
                    {
                        dump.Discarded(filePath, "excluded file type");
                        continue;
                    }

                    JumpItem jumpItem = context.CreateClaimedFileItem(this, filePath, dump);
                    if (jumpItem != null)
                    {
                        dump.Produced(filePath);
                        yield return jumpItem;
                    }
                }
            }
        }
    }
}
