#define SHOW_DEBUG_INFO

using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Shell;

namespace LevyFlight
{
    using CMD = LevyFlightWindowCommand;

    public class JumpItem
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public Scanner Scanner { get; set; }
        public int LineNumber { get; set; }
        public int CaretColumn { get; set; }

        public uint Score { get; set; }
        public ScoreComponent[] ScoreComponents { get; private set; }

        /// <summary>Icon shown in the jump list. Set automatically by scanner; overridden for TreeSitter items.</summary>
        public ImageMoniker IconMoniker { get; set; }

        /// <summary>Index used for quick-open hotkey hints. -1 = no hint, 0 = first item (no hotkey), 1..15 = hotkey 1-9,Q,W,E,R,T,Y.</summary>
        public int QuickOpenIndex { get; set; } = -1;

        /// <summary>True for function prototypes/forward declarations (vs full definitions).</summary>
        public bool IsDeclaration { get; set; }

        /// <summary>Score bonus applied within a category (e.g. recency rank of edit regions). Must stay below the category score quantum.</summary>
        public uint ExtraScore { get; set; }

        private ScoreComponent_WholeWord _scWholeWord;
        private ScoreComponent_PathKeywordCI _scPathKeywordCI;
        private ScoreComponent_NameKeywordCI _scNameKeywordCI;

        public string DisplayName
        {
            get
            {
                if (LineNumber >= 0)
                {
                    // LineNumber is a 0-based VS line; display it 1-based like the editor does.
                    return $"{Name}(Line:{LineNumber + 1})";
                }
                else
                {
                    return Name;
                }
            }
        }

        public string DebugString
        {
            get
            {
                var lines = new List<string> { $"{Score,11}  {Name}", "-------------------" }.Concat(ScoreComponents.Select(x =>
                {
                    uint s = x.Score;
                    uint w = x.Weight;
                    uint o = s * w;
                    string extraDebugInfo = "";
#if SHOW_DEBUG_INFO
                    if (!string.IsNullOrEmpty(x.DebugInfo))
                    {
                        extraDebugInfo = "(" + x.DebugInfo + ")";
                    }
#endif
                    return $"{o,11}={w,10}*{s:00} [{x.Name}]{extraDebugInfo}";
                }));
                return string.Join("\n", lines);
            }
        }

        public static JumpItem MakeBookmark(string line)
        {
            // Support both tab (new) and ? (legacy) delimiters
            char delimiter = line.Contains('\t') ? '\t' : '?';
            var tokens = line.Split(delimiter);
            var jumpItem = new JumpItem(ScannerCatalog.Bookmark, tokens[1]);
            jumpItem.Name = tokens[0];
            jumpItem.SetPosition(int.Parse(tokens[2]), int.Parse(tokens[3]));
            return jumpItem;
        }

        public JumpItem(Scanner scanner, string fullPath)
        {
            if (scanner == null)
                throw new ArgumentNullException(nameof(scanner));

            Name = System.IO.Path.GetFileName(fullPath);
            FullPath = fullPath;
            Scanner = scanner;
            LineNumber = -1;
            CaretColumn = -1;
            Score = 1;

            IconMoniker = scanner.GetIconMoniker(fullPath);

            _scWholeWord = new ScoreComponent_WholeWord();
            _scPathKeywordCI = new ScoreComponent_PathKeywordCI();
            _scNameKeywordCI = new ScoreComponent_NameKeywordCI();
            var scores = new List<(uint, ScoreComponent)> // (ValueSpace, ScoreComponentClass)
            {
                (10, new ScoreComponent_NameKeywordCS()), // Match keywords on item name, case sensitive
                (10, _scPathKeywordCI), // Match keywords on item full path, case insensitive
                (10, new ScoreComponent_ScannerPriority()), // Sort on scanner priority
                (10, _scNameKeywordCI), // Match keywords on item name, case insensitive
                (10, _scWholeWord), // Whole word match. First check if the filename without extension matches, if true try to match more components in the full path
            };
            uint accumWeight = 1;
            ScoreComponents = scores.Select(x =>
            {
                x.Item2.Weight = accumWeight;
                accumWeight *= x.Item1;
                return x.Item2;
            }).ToArray();

            UpdateScore();
        }

        public void SetPosition(int line, int col)
        {
            LineNumber = line;
            CaretColumn = col;
        }

        public override string ToString()
        {
            return string.Format("{0}\t{1}\t{2}\t{3}", Name, FullPath, LineNumber, CaretColumn);
        }

        public void UpdateScore()
        {
            uint score = 0;
            foreach (var c in ScoreComponents)
            {
                c.Evaluate(this);
                score += c.Score * c.Weight;
            }
            int numKeywords = Filter.Instance.FilterStringsI.Length;
            if (_scWholeWord.Score == 0 && numKeywords > _scPathKeywordCI.Score && numKeywords > _scNameKeywordCI.Score)
            {
                score = 0; // Only accept items that at least match all keywords in the fullpath or get a whole word match
            }
            if (score > 0)
            {
                score += ExtraScore;
            }
            this.Score = score;
        }

    }

    public class QuickOpenPreset
    {
        public string Name { get; set; }
        public Key ShortcutKey { get; set; }
        public string ShortcutLetter { get; set; }
        public HashSet<Scanner> IncludedScanners { get; set; }
        public bool IncludeAll => IncludedScanners == null || IncludedScanners.Count == 0;

        public static readonly QuickOpenPreset[] DefaultPresets = new[]
        {
            new QuickOpenPreset
            {
                Name = "All In One",
                ShortcutKey = Key.H,
                ShortcutLetter = "H",
                IncludedScanners = null
            },
            new QuickOpenPreset
            {
                Name = "Files",
                ShortcutKey = Key.F,
                ShortcutLetter = "F",
                IncludedScanners = new HashSet<Scanner>
                {
                    ScannerCatalog.HotFile, ScannerCatalog.Transition, ScannerCatalog.OpenFile,
                    ScannerCatalog.RecentFile, ScannerCatalog.ActiveDirectoryFile
                }
            },
            new QuickOpenPreset
            {
                Name = "Bookmarks",
                ShortcutKey = Key.B,
                ShortcutLetter = "B",
                IncludedScanners = new HashSet<Scanner> { ScannerCatalog.Bookmark }
            },
            new QuickOpenPreset
            {
                Name = "Edits",
                ShortcutKey = Key.M,
                ShortcutLetter = "M",
                IncludedScanners = new HashSet<Scanner> { ScannerCatalog.RecentEdit, ScannerCatalog.RecentFile }
            },
        };
    }

    public class PresetViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string Name { get; set; }
        public string ShortcutLetter { get; set; }
        public Key ShortcutKey { get; set; }
        public HashSet<Scanner> IncludedScanners { get; set; }
        public bool IncludeAll => IncludedScanners == null || IncludedScanners.Count == 0;

        private bool isActive;
        public bool IsActive
        {
            get => isActive;
            set { isActive = value; OnPropertyChanged(); }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class Filter
    {
        private static readonly char[] FILTER_SEPERATOR = { ' ' };

        public static Filter Instance { get; private set; }

        static Filter()
        {
            Instance = new Filter();
            Instance.Reset();
        }

        public string FilterStringRaw { get; private set; }
        public string[] FilterStrings { get; private set; }
        public string[] FilterStringsI { get; private set; }

        public void Reset()
        {
            FilterStringRaw = "";
            FilterStrings = FilterStringsI = new string[0];
        }

        public void UpdateFilterString(string input)
        {
            FilterStringRaw = input;
            FilterStrings = input.Split(FILTER_SEPERATOR, StringSplitOptions.RemoveEmptyEntries).Select(str => str.Trim()).ToArray();
            FilterStringsI = FilterStrings.Select(str => str.ToLower()).ToArray();
        }
    }

    public abstract class ScoreComponent
    {
#if SHOW_DEBUG_INFO
        public readonly bool USE_DEBUG_INFO = true;

        public string DebugInfo;
#endif
        public uint Score;
        public uint Weight;

        public ScoreComponent()
        {
#if SHOW_DEBUG_INFO
            DebugInfo = "";
#endif
            Score = 0;
            Weight = 0;
        }

        public abstract string Name { get; }

        public abstract void Evaluate(JumpItem jumpItem);
    }

    public class ScoreComponent_WholeWord : ScoreComponent
    {
        public override string Name => "NameWholeWord";

        public override void Evaluate(JumpItem jumpItem)
        {
            var filter = Filter.Instance;
            if (filter.FilterStringsI.Length == 0)
            {
                // Always accept file when the filter is empty
                Score = 1;
#if SHOW_DEBUG_INFO
                DebugInfo = "EmptyFilter";
#endif
                return;
            }
            string itemNameI = jumpItem.Name;
            string[] itemNameSplit = itemNameI.Split('.');
            string itemNameNoExt = (itemNameSplit != null && itemNameSplit.Length > 0) ? itemNameSplit[0] : itemNameI;
            if (itemNameNoExt.Length > 0 && itemNameNoExt.Equals(filter.FilterStringRaw, StringComparison.OrdinalIgnoreCase))
            {
                Score = 1;
#if SHOW_DEBUG_INFO
                DebugInfo = $"FullMatch:{filter.FilterStringRaw}";
#endif
            }
            else
            {
                Score = 0;
#if SHOW_DEBUG_INFO
                DebugInfo = "FullMatch:<NO>";
#endif
            }
        }
    }

    public class ScoreComponent_NameKeywordCS : ScoreComponent
    {
        public override string Name => "Name-Keyword CS";

        public override void Evaluate(JumpItem jumpItem)
        {
            var filter = Filter.Instance;
            uint numMatchKeywordsCaseSensitive = 0;
#if SHOW_DEBUG_INFO
            var matchedKeyWords = new List<string>();
#endif
            {
                string itemName = jumpItem.Name;
                foreach (var keyword in filter.FilterStrings)
                {
                    if (itemName.Contains(keyword))
                    {
                        numMatchKeywordsCaseSensitive++;
#if SHOW_DEBUG_INFO
                        if (USE_DEBUG_INFO)
                        {
                            matchedKeyWords.Add(keyword);
                        }
#endif
                    }
                }
            }

            this.Score = numMatchKeywordsCaseSensitive;
#if SHOW_DEBUG_INFO
            this.DebugInfo = string.Join(",", matchedKeyWords.ToArray());
#endif
        }
    }

    public class ScoreComponent_NameKeywordCI : ScoreComponent
    {
        public override string Name => "Name-Keyword CI";

        public override void Evaluate(JumpItem jumpItem)
        {
            var filter = Filter.Instance;
            uint numMatchKeywordsCaseInsensitive = 0;
#if SHOW_DEBUG_INFO
            var matchedKeyWords = new List<string>();
#endif
            {
                string itemNameI = jumpItem.Name.ToLower();
                foreach (var keyword in filter.FilterStringsI)
                {
                    if (itemNameI.Contains(keyword))
                    {
                        numMatchKeywordsCaseInsensitive++;
#if SHOW_DEBUG_INFO
                        if (USE_DEBUG_INFO)
                        {
                            matchedKeyWords.Add(keyword);
                        }
#endif
                    }
                }
            }

            this.Score = numMatchKeywordsCaseInsensitive;
#if SHOW_DEBUG_INFO
            this.DebugInfo = string.Join(",", matchedKeyWords.ToArray());
#endif
        }
    }

    public class ScoreComponent_PathKeywordCI : ScoreComponent
    {
        public override string Name => "Path-Keyword CI";

        public override void Evaluate(JumpItem jumpItem)
        {
            var filter = Filter.Instance;
            uint numFullPathMatchKeywordsCaseInsensitive = 0;
#if SHOW_DEBUG_INFO
            var matchedKeyWords = new List<string>();
#endif
            {
                string fullPath = jumpItem.FullPath.ToLower();
                foreach (var keyword in filter.FilterStringsI)
                {
                    if (fullPath.Contains(keyword))
                    {
                        numFullPathMatchKeywordsCaseInsensitive++;
#if SHOW_DEBUG_INFO
                        if (USE_DEBUG_INFO)
                        {
                            matchedKeyWords.Add(keyword);
                        }
#endif
                    }
                }
            }

            this.Score = numFullPathMatchKeywordsCaseInsensitive;
#if SHOW_DEBUG_INFO
            this.DebugInfo = string.Join(",", matchedKeyWords.ToArray());
#endif
        }
    }

    public class ScoreComponent_ScannerPriority : ScoreComponent
    {
        public override string Name => "Scanner";

        public override void Evaluate(JumpItem jumpItem)
        {
            this.Score = (uint)jumpItem.Scanner.Priority;
#if SHOW_DEBUG_INFO
            this.DebugInfo = jumpItem.Scanner.DisplayName;
#endif
        }
    }
}
