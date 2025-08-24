using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

namespace LevyFlight
{
    using CMD = LevyFlightWindowCommand;

    public class CommonMixin
    {
        public static readonly string[] EXCLUDES = new string[] { ".vcxproj" };

        public static bool IsExcluded(string path)
        {
            foreach (var keyword in EXCLUDES)
            {
                if (path.Contains(keyword))
                {
                    return true;
                }
            }
            return false;
        }

        public static string ToRelativePath(string path)
        {
            return PackageUtilities.MakeRelative(CMD.SolutionFolder, path);
        }

        public static string ToAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }
            else if (path.Length < 2 || path[1] == ':')
            {
                return path;
            }
            else
            {
                return Path.Combine(CMD.SolutionFolder, path);
            }
        }
    }
}
