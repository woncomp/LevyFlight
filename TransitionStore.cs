using EnvDTE;
using System;
using System.Collections.Generic;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using System.Diagnostics;
using System.Linq;
using System.IO;

namespace LevyFlight
{
    using CMD = LevyFlightWindowCommand;

    public class TransitionRecord : IComparable<TransitionRecord>
    {
        public string Path { get; set; }
        public int Count { get; set; }

        public TransitionRecord(string path)
        {
            Path = path;
        }

        public int CompareTo(TransitionRecord other)
        {
            // Sort descending by Count
            return other.Count.CompareTo(this.Count);
        }
    }

    public class TransitionList
    {
        public string SourceFile { get; set; }
        public List<TransitionRecord> Transitions { get; set; }

        public TransitionList(string filePath)
        {
            SourceFile = filePath;
            Transitions = new List<TransitionRecord>();
        }

        public void AddRecord(string filePath)
        {
            TransitionRecord record = Transitions.Find(x => x.Path == filePath);
            if(record == null)
            {
                record = new TransitionRecord(filePath);
                Transitions.Add(record);
            }
            record.Count++;
        }
    }

    public class TransitionStore : CommonMixin
    {
        public static TransitionStore Instance { get; private set; }

        public static string TransitionFolder
        {
            get { return Path.Combine(CMD.ExtCacheFolder, "transitions"); }
        }
        public const int TransitionHashMask = 0xff;


        public List<string> Recents { get; private set; }

        private string RecentsFile
        {
            get { return Path.Combine(CMD.ExtCacheFolder, "recents.txt"); }
        }

        private Dictionary<int, TransitionList> TransitionMap;

        private string _lastActiveDocument;
        private WindowEvents _windowEvents;   // keep reference so it isn't GC'd

        public void Initialize()
        {
            Instance = this;

            ThreadHelper.ThrowIfNotOnUIThread();
            var IDE = Package.GetGlobalService(typeof(DTE)) as DTE;

            _windowEvents = IDE.Events.WindowEvents;
            _windowEvents.WindowActivated += OnWindowActivated;
            _lastActiveDocument = IDE.ActiveDocument?.FullName;
            if(!string.IsNullOrEmpty(_lastActiveDocument))
            {
                _lastActiveDocument = ToRelativePath(_lastActiveDocument);
            }

            LoadRecents();
            LoadTransitions();
        }

        public List<TransitionRecord> GetTransitionsForFile(string file)
        {
            if(string.IsNullOrEmpty(file))
            {
                return new List<TransitionRecord>();
            }
            var rel = ToRelativePath(file);
            var trSrcHash = rel.GetHashCode();
            if(!TransitionMap.ContainsKey(trSrcHash))
            {
                TransitionMap[trSrcHash] = new TransitionList(rel);
            }
            var list = TransitionMap[trSrcHash];
            list.Transitions.Sort();
            return list.Transitions;
        }

        private void OnWindowActivated(Window gotFocus, Window lostFocus)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var newDocFullPath = gotFocus?.Document?.FullName;
            if (string.IsNullOrEmpty(newDocFullPath))
                return;
            var newDocPath = ToRelativePath(newDocFullPath);

            if (!StringComparer.OrdinalIgnoreCase.Equals(newDocPath, _lastActiveDocument))
            {
                var oldDocPath = _lastActiveDocument;
                if(!string.IsNullOrEmpty(oldDocPath))
                {
                    Debug.WriteLine("Transition: {0} => {1}", oldDocPath, newDocPath);

                    // Recent files
                    Recents.RemoveAll(x => x == newDocFullPath);
                    if(Recents.Count >= 50) // Max size of preserved recent items
                    {
                        Recents.RemoveAt(Recents.Count - 1);
                    }
                    Recents.Insert(0, newDocFullPath);
                    SaveRecents();

                    // Transitions
                    var trSrcHash = oldDocPath.GetHashCode();
                    if(!TransitionMap.ContainsKey(trSrcHash))
                    {
                        var list = new TransitionList(oldDocPath);
                        TransitionMap[trSrcHash] = list;
                    }
                    var trList = TransitionMap[trSrcHash];
                    trList.AddRecord(newDocPath);
                    SaveTransitions(trSrcHash);

                }
                _lastActiveDocument = newDocPath;
            }
        }

        private void SaveRecents()
        {
            var strs = Recents.Select(x => ToRelativePath(x)).ToArray();
            File.WriteAllLines(RecentsFile, strs);
        }

        private void LoadRecents()
        {
            Recents = new List<string>();
            var filePath = RecentsFile;
            if (File.Exists(filePath))
            {
                foreach (var line in File.ReadAllLines(filePath))
                {
                    Recents.Add(ToAbsolutePath(line));
                }
            }
        }

        public void SaveTransitions(int hash)
        {
            var transitionFolder = TransitionFolder;
            Directory.CreateDirectory(transitionFolder);
            int hash8 = hash & TransitionHashMask;
            var transitionFileName = Path.Combine(transitionFolder, $"{hash8:x4}.txt");

            using (var writer = new StreamWriter(transitionFileName))
            {
                foreach(var pair in TransitionMap)
                {
                    var trSrcHash = pair.Key;
                    var trList = pair.Value;
                    if ((trSrcHash & hash8) == hash8)
                    {
                        writer.WriteLine($"SourceFile:{trList.SourceFile}");
                        foreach (var tr in trList.Transitions)
                        {
                            writer.WriteLine($"{tr.Count,8}|{tr.Path}");
                        }
                        writer.WriteLine();
                    }
                }
            }
        }

        private void LoadTransitions()
        {
            TransitionMap = new Dictionary<int, TransitionList>();

            var transitionsFolder = TransitionFolder;
            var migrationFolder = transitionsFolder + "_old";
            if(Directory.Exists(migrationFolder))
            {
                transitionsFolder = migrationFolder;
            }
            else
            {
                migrationFolder = null;
            }
            if(!Directory.Exists(transitionsFolder))
            {
                return;
            }

            foreach(var name in Directory.EnumerateFiles(transitionsFolder))
            {
                using (var reader = new StreamReader(name))
                {
                    TransitionList currentList = null;
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        if (line.StartsWith("SourceFile:"))
                        {
                            var sourceFile = line.Substring("SourceFile:".Length).Trim();
                            currentList = new TransitionList(sourceFile);
                            TransitionMap[sourceFile.GetHashCode()] = currentList;
                        }
                        else if (currentList != null)
                        {
                            var parts = line.Split('|');
                            if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int count))
                            {
                                var path = parts[1].Trim();
                                if (!string.IsNullOrEmpty(path))
                                {
                                    var record = new TransitionRecord(path) { Count = count };
                                    currentList.Transitions.Add(record);
                                }
                            }
                        }
                    }
                }
            }

            // Migration cleanup
            if(migrationFolder != null)
            {
                Directory.Delete(migrationFolder, true);
                for(int i=0;i<= TransitionHashMask;i++)
                {
                    SaveTransitions(i);
                }
            }
        }
    }
}
