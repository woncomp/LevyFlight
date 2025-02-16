using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Shell;

namespace LevyFlight
{
    using CMD = LevyFlightWindowCommand;

    public class JumpItem
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public Category Category { get; set; }
        public int LineNumber { get; set; }
        public int CaretColumn { get; set; }

        public uint Score { get; set; }
        public ScoreComponent[] ScoreComponents { get; private set; }

        private ScoreComponent_WholeWord _scWholeWord;
        private ScoreComponent_PathKeywordCI _scPathKeywordCI;

        public string DisplayName
        {
            get
            {
                if (LineNumber >= 0)
                {
                    return string.Format("{}(Line:{})", Name, LineNumber);
                }
                else
                {
                    return Name;
                }
            }
        }

        public static JumpItem MakeBookmark(string line)
        {
            var tokens = line.Split('?');
            var jumpItem = new JumpItem(Category.Bookmark, tokens[1]);
            jumpItem.Name = tokens[0];
            jumpItem.SetPosition(int.Parse(tokens[2]), int.Parse(tokens[3]));
            return jumpItem;
        }

        public JumpItem(Category category, string fullPath)
        {
            Name = System.IO.Path.GetFileName(fullPath);
            FullPath = fullPath;
            Category = category;
            LineNumber = -1;
            CaretColumn = -1;
            Score = 1;

            _scWholeWord = new ScoreComponent_WholeWord();
            _scPathKeywordCI = new ScoreComponent_PathKeywordCI();
            var scores = new List<(uint, ScoreComponent)> // (ValueSpace, ScoreComponentClass)
            {
                (10, new ScoreComponent_NameKeywordCS()), // Match keywords on item name, case sensitive
                (10, _scPathKeywordCI), // Match keywords on item full path, case insensitive
                (10, new ScoreComponent_Category()), // Sort on category
                (10, new ScoreComponent_NameKeywordCI()), // Match keywords on item name, case insensitive
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
            return string.Format("{0}?{1}?{2}?{3}", Name, FullPath, LineNumber, CaretColumn);
        }

        public void UpdateScore()
        {
            uint score = 0;
            foreach(var c in ScoreComponents)
            {
                c.Evaluate(this);
                score += c.Score * c.Weight;
            }
            if(_scWholeWord.Score == 0 && Filter.Instance.FilterStringsI.Length > _scPathKeywordCI.Score)
            {
                score = 0; // Only accept items that at least match all keywords in the fullpath or get a whole word match
            }
            this.Score = score;
        }
    }

    public enum Category
    {
        SolutionFile,
        RecentFile,
        FavoriteFile,
        Bookmark,
        ActiveDirectoryFile,
        ActiveFile,
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
#if DEBUG
        public readonly bool USE_DEBUG_INFO = true;

        public string DebugInfo;
#endif
        public uint Score;
        public uint Weight;

        public ScoreComponent()
        {
#if DEBUG
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
            if(filter.FilterStringsI.Length == 0)
            {
                // Always accept file when the filter is empty
                Score = 1;
#if DEBUG
                DebugInfo = "EmptyFilter";
#endif
                return;
            }
            string itemNameI = jumpItem.Name;
            string[] itemNameSplit = itemNameI.Split('.');
            string itemNameNoExt = (itemNameSplit != null && itemNameSplit.Length > 0) ? itemNameSplit[0] : itemNameI;
            if(itemNameNoExt.Length > 0 && itemNameNoExt.Equals(filter.FilterStringRaw, StringComparison.OrdinalIgnoreCase) )
            {
                Score = 1;
#if DEBUG
                DebugInfo = $"FullMatch:{filter.FilterStringRaw}";
#endif
            }
            else
            {
                Score = 0;
#if DEBUG
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
#if DEBUG
            var matchedKeyWords = new List<string>();
#endif
            {
                string itemName = jumpItem.Name;
                foreach (var keyword in filter.FilterStrings)
                {
                    if (itemName.Contains(keyword))
                    {
                        numMatchKeywordsCaseSensitive++;
#if DEBUG
                        if (USE_DEBUG_INFO)
                        {
                            matchedKeyWords.Add(keyword);
                        }
#endif
                    }
                }
            }

            this.Score = numMatchKeywordsCaseSensitive;
#if DEBUG
            this.DebugInfo = string.Join(" ", matchedKeyWords.ToArray());
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
#if DEBUG
            var matchedKeyWords = new List<string>();
#endif
            {
                string itemNameI = jumpItem.Name.ToLower();
                foreach (var keyword in filter.FilterStringsI)
                {
                    if (itemNameI.Contains(keyword))
                    {
                        numMatchKeywordsCaseInsensitive++;
#if DEBUG
                        if (USE_DEBUG_INFO)
                        {
                            matchedKeyWords.Add(keyword);
                        }
#endif
                    }
                }
            }

            this.Score = numMatchKeywordsCaseInsensitive;
#if DEBUG
            this.DebugInfo = string.Join(" ", matchedKeyWords.ToArray());
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
#if DEBUG
            var matchedKeyWords = new List<string>();
#endif
            {
                string fullPath = jumpItem.FullPath.ToLower();
                foreach (var keyword in filter.FilterStringsI)
                {
                    if (fullPath.Contains(keyword))
                    {
                        numFullPathMatchKeywordsCaseInsensitive++;
#if DEBUG
                        if (USE_DEBUG_INFO)
                        {
                            matchedKeyWords.Add(keyword);
                        }
#endif
                    }
                }
            }

            this.Score = numFullPathMatchKeywordsCaseInsensitive;
#if DEBUG
            this.DebugInfo = string.Join(" ", matchedKeyWords.ToArray());
#endif
        }
    }

    public class ScoreComponent_Category : ScoreComponent
    {
        public override string Name => "Category";

        public override void Evaluate(JumpItem jumpItem)
        {
            this.Score = (uint)jumpItem.Category;
#if DEBUG
            this.DebugInfo = jumpItem.Category.ToString();
#endif
        }
    }
}