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

namespace LevyFlight
{
    using CMD = LevyFlightWindowCommand;

    public class JumpItem
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public int Score { get; set; }
        public int LineNumber { get; set; }
        public int CaretColumn { get; set; }

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
            Score = 0;
            LineNumber = -1;
            CaretColumn = -1;
        }

        public void SetPosition(int line, int col)
        {
            LineNumber = line;
            CaretColumn = col;
        }

        public override string ToString()
        {
            return string.Format("{0}?{1}?{2}?{3}",Name, FullPath, LineNumber, CaretColumn );
        }

        public static JumpItem ParseBookmark(string line)
        {
            var tokens = line.Split('?');
            var jumpItem = new JumpItem(tokens[0], tokens[1]);
            jumpItem.SetPosition(int.Parse(tokens[2]), int.Parse(tokens[3]));
            return jumpItem;
        }
    }
}
