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

        public JumpItem(string name, string fullPath)
        {
            Name = name;
            FullPath = fullPath;
            LineNumber = -1;
            CaretColumn = -1;
            Score = 1;

            _scWholeWord = new ScoreComponent_WholeWord();
            _scPathKeywordCI = new ScoreComponent_PathKeywordCI();
            var scores = new List<(uint, ScoreComponent)>
            {
                (10000, _scWholeWord), // Whole word match. First check if the filename without extension matches, if true try to match more components in the full path
                (1000, new ScoreComponent_NameKeywordCI()), // Match keywords on item name, case insensitive
                (100, _scPathKeywordCI), // Match keywords on item full path, case insensitive
                (10, new ScoreComponent_NameKeywordCS()), // Match keywords on item name, case sensitive
            };
            ScoreComponents = scores.Select(x =>
            {
                x.Item2.Weight = x.Item1;
                return x.Item2;
            }).ToArray();
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

        public static JumpItem ParseBookmark(string line)
        {
            var tokens = line.Split('?');
            var jumpItem = new JumpItem(tokens[0], tokens[1]);
            jumpItem.SetPosition(int.Parse(tokens[2]), int.Parse(tokens[3]));
            return jumpItem;
        }

        public void UpdateScore(Filter filter)
        {
            uint score = 0;
            foreach(var c in ScoreComponents)
            {
                c.Evaluate(this, filter);
                score += c.Score * c.Weight;
            }
            if(score == 0 && filter.FilterStrings.Length == 0)
            {
                score = 1; // Give it at least 1 score when the filter is empty to avoid being filtered away
            }
            if(_scWholeWord.Score == 0 && filter.FilterStringsI.Length > _scPathKeywordCI.Score)
            {
                score = 0; // Only accept items that at least match all keywords in the fullpath or get a whole word match
            }
            this.Score = score;
        }
    }

    public class Filter
    {
        private static readonly char[] FILTER_SEPERATOR = { ' ' };

        public string FilterStringRaw { get; private set; }
        public string[] FilterStrings { get; private set; }
        public string[] FilterStringsI { get; private set; }

        public Filter()
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
        public readonly bool USE_DEBUG_INFO = false;

        public string DebugInfo;
        public uint Score;
        public uint Weight;

        public ScoreComponent()
        {
            DebugInfo = "";
            Score = 0;
            Weight = 0;
        }

        public abstract string Name { get; }

        public abstract void Evaluate(JumpItem jumpItem, Filter filter);
    }

    public class ScoreComponent_WholeWord : ScoreComponent
    {
        public override string Name => "NameWholeWord";

        public override void Evaluate(JumpItem jumpItem, Filter filter)
        {
            uint numWholeWordMatch = 0;
            string itemNameI = jumpItem.Name;
            string[] itemNameSplit = itemNameI.Split('.');
            string itemNameNoExt = (itemNameSplit != null && itemNameSplit.Length > 0) ? itemNameSplit[0] : itemNameI;
            if(itemNameNoExt.Length > 0 && itemNameNoExt.Equals(filter.FilterStringRaw, StringComparison.OrdinalIgnoreCase) )
            {
                numWholeWordMatch = 1;
            }
            this.Score = numWholeWordMatch;
            this.DebugInfo = numWholeWordMatch > 0 ? $"FullMatch:{filter.FilterStringRaw}" : "NoFullMatch";
        }
    }

    public class ScoreComponent_NameKeywordCS : ScoreComponent
    {
        public override string Name => "Name-Keyword CS";

        public override void Evaluate(JumpItem jumpItem, Filter filter)
        {
            uint numMatchKeywordsCaseSensitive = 0;
            var matchedKeyWords = new List<string>();
            {
                string itemName = jumpItem.Name;
                foreach (var keyword in filter.FilterStrings)
                {
                    if (itemName.Contains(keyword))
                    {
                        numMatchKeywordsCaseSensitive++;
                        if (USE_DEBUG_INFO)
                        {
                            matchedKeyWords.Add(keyword);
                        }
                    }
                }
            }

            this.Score = numMatchKeywordsCaseSensitive;
            this.DebugInfo = string.Join(" ", matchedKeyWords.ToArray());
        }
    }

    public class ScoreComponent_NameKeywordCI : ScoreComponent
    {
        public override string Name => "Name-Keyword CI";

        public override void Evaluate(JumpItem jumpItem, Filter filter)
        {
            uint numMatchKeywordsCaseInsensitive = 0;
            var matchedKeyWords = new List<string>();
            {
                string itemNameI = jumpItem.Name.ToLower();
                foreach (var keyword in filter.FilterStringsI)
                {
                    if (itemNameI.Contains(keyword))
                    {
                        numMatchKeywordsCaseInsensitive++;
                        if (USE_DEBUG_INFO)
                        {
                            matchedKeyWords.Add(keyword);
                        }
                    }
                }
            }

            this.Score = numMatchKeywordsCaseInsensitive;
            this.DebugInfo = string.Join(" ", matchedKeyWords.ToArray());
        }
    }

    public class ScoreComponent_PathKeywordCI : ScoreComponent
    {
        public override string Name => "Path-Keyword CI";

        public override void Evaluate(JumpItem jumpItem, Filter filter)
        {
            uint numFullPathMatchKeywordsCaseInsensitive = 0;
            var matchedKeyWords = new List<string>();
            {
                string fullPath = jumpItem.FullPath.ToLower();
                foreach (var keyword in filter.FilterStringsI)
                {
                    if (fullPath.Contains(keyword))
                    {
                        numFullPathMatchKeywordsCaseInsensitive++;
                        if (USE_DEBUG_INFO)
                        {
                            matchedKeyWords.Add(keyword);
                        }
                    }
                }
            }

            this.Score = numFullPathMatchKeywordsCaseInsensitive;
            this.DebugInfo = string.Join(" ", matchedKeyWords.ToArray());
        }
    }
}