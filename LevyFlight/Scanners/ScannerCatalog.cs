using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LevyFlight
{
    internal static class ScannerCatalog
    {
        public static readonly SolutionFileScanner SolutionFile = new SolutionFileScanner();
        public static readonly ActiveProjectFileScanner ActiveProjectFile = new ActiveProjectFileScanner();
        public static readonly CurrentProjectFileScanner CurrentProjectFile = new CurrentProjectFileScanner();
        public static readonly FavoriteFileScanner FavoriteFile = new FavoriteFileScanner();
        public static readonly BookmarkScanner Bookmark = new BookmarkScanner();
        public static readonly ActiveDirectoryFileScanner ActiveDirectoryFile = new ActiveDirectoryFileScanner();
        public static readonly TreeSitterScanner TreeSitter = new TreeSitterScanner();
        public static readonly RecentFileScanner RecentFile = new RecentFileScanner();
        public static readonly OpenFileScanner OpenFile = new OpenFileScanner();
        public static readonly TransitionScanner Transition = new TransitionScanner();
        public static readonly HotFileScanner HotFile = new HotFileScanner();
        public static readonly RecentEditScanner RecentEdit = new RecentEditScanner();

        private static readonly IReadOnlyList<Scanner> all = new ReadOnlyCollection<Scanner>(new Scanner[]
        {
            SolutionFile,
            ActiveProjectFile,
            CurrentProjectFile,
            FavoriteFile,
            Bookmark,
            ActiveDirectoryFile,
            TreeSitter,
            RecentFile,
            OpenFile,
            Transition,
            HotFile,
            RecentEdit,
        });

        private static readonly IReadOnlyList<Scanner> defaultPriorityOrder = new ReadOnlyCollection<Scanner>(new Scanner[]
        {
            RecentEdit,
            HotFile,
            Transition,
            OpenFile,
            RecentFile,
            TreeSitter,
            ActiveDirectoryFile,
            Bookmark,
            FavoriteFile,
            CurrentProjectFile,
            ActiveProjectFile,
            SolutionFile,
        });

        public static IReadOnlyList<Scanner> All => all;

        public static IReadOnlyList<Scanner> DefaultPriorityOrder => defaultPriorityOrder;

        public static Scanner FindById(string id)
        {
            return all.FirstOrDefault(scanner => string.Equals(scanner.Id, id, StringComparison.Ordinal));
        }

        public static IReadOnlyList<Scanner> NormalizeOrder(IEnumerable<string> scannerIds)
        {
            if (scannerIds == null)
            {
                return defaultPriorityOrder;
            }

            var result = new List<Scanner>();
            foreach (string id in scannerIds)
            {
                Scanner scanner = FindById(id);
                if (scanner != null && !result.Contains(scanner))
                {
                    result.Add(scanner);
                }
            }

            if (result.Count == 0)
            {
                return defaultPriorityOrder;
            }

            foreach (Scanner scanner in defaultPriorityOrder)
            {
                if (!result.Contains(scanner))
                {
                    result.Add(scanner);
                }
            }

            return result;
        }

        public static IReadOnlyList<Scanner> NormalizeOrder(IEnumerable<Scanner> scanners)
        {
            return NormalizeOrder(scanners?.Select(scanner => scanner?.Id));
        }

        public static void ApplyPriorityOrder(IReadOnlyList<Scanner> scannerOrder)
        {
            for (int index = 0; index < scannerOrder.Count; index++)
            {
                scannerOrder[index].Priority = scannerOrder.Count - 1 - index;
            }
        }
    }
}
