using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using Task = System.Threading.Tasks.Task;

namespace LevyFlight
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class LevyFlightWindowCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Collection Name used for ShellSettingsManager
        /// </summary>
        public const string SettingsCollectionName = "LevyFlight";

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("e0650b9a-53f3-4f9e-a1ea-e8a9be49290c");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="LevyFlightWindowCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private LevyFlightWindowCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);

            SettingsManager settingsManager = new ShellSettingsManager(this.package);
            this.SettingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);
            if (!SettingsStore.CollectionExists(SettingsCollectionName))
            {
                SettingsStore.CreateCollection(SettingsCollectionName);
            }

            LoadBookmarks();
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static LevyFlightWindowCommand Instance
        {
            get;
            private set;
        }

        public static string SolutionFolder { get; private set; }
        public static string ExtCacheFolder { get; private set; }

        public WritableSettingsStore SettingsStore { get; private set; }

        public List<JumpItem> Bookmarks { get; private set; }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        internal IVsTextManager TextManager { get; set; }

        internal IComponentModel ComponentModel { get; set; }
        internal IVsMRUItemsStore MRUItemsStore { get; private set; }

        private string BookmarksFile
        {
            get { return Path.Combine(ExtCacheFolder, "bookmarks_test.txt"); }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in LevyFlightWindowCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            DTE IDE = Package.GetGlobalService(typeof(DTE)) as DTE;
            SolutionFolder = Path.GetDirectoryName(IDE.Solution.FullName);
            ExtCacheFolder = Path.Combine(SolutionFolder, ".vs", SettingsCollectionName);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new LevyFlightWindowCommand(package, commandService);

            Instance.TextManager = await package.GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
            Instance.ComponentModel = await package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
            Instance.MRUItemsStore = await package.GetServiceAsync(typeof(SVsMRUItemsStore)) as IVsMRUItemsStore;
        }

        public static DTE GetActiveIDE()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // Get an instance of currently running Visual Studio IDE.
            DTE dte2 = Package.GetGlobalService(typeof(DTE)) as DTE;
            return dte2;
        }

        public IVsTextView GetTextView()
        {
            TextManager.GetActiveView(1, null, out IVsTextView textViewCurrent);
            return textViewCurrent;
        }

        public JumpItem AddBookmarkFromCurrentPosition()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var textView = GetTextView();
            textView.GetCaretPos(out int lineNo, out int col);
            textView.GetSelectedText(out string text);

            var doc = GetActiveIDE().ActiveDocument;
            var filePath = doc.FullName;

            if (string.IsNullOrEmpty(text))
            {
                text = string.Format("{0} (Line:{1})", doc.Name, lineNo);
            }

            var jumpItem = new JumpItem(Category.Bookmark, filePath);
            jumpItem.SetPosition(lineNo, col);
            Bookmarks.Add(jumpItem);
            SaveBookmarks();

            return jumpItem;
        }

        public void SaveBookmarks()
        {
            var strs = Bookmarks.Select(x => x.ToString()).ToArray();
            File.WriteAllLines(BookmarksFile, strs);
        }

        private void LoadBookmarks()
        {
            Bookmarks = new List<JumpItem>();
            var filePath = BookmarksFile;
            if (File.Exists(filePath))
            {
                foreach (var line in File.ReadAllLines(filePath))
                {
                    Bookmarks.Add(JumpItem.MakeBookmark(line));
                }
            }
        }

        public string GetActiveFile()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            DTE IDE = GetActiveIDE();
            if (IDE.ActiveDocument == null)
            {
                return null;
            }
            else
            {
                return IDE.ActiveDocument.FullName;
            }
        }

        public string[] GetActiveFiles()
        {
            var list = new List<string>();
            var rdt = new RunningDocumentTable(package);
            foreach (var info in rdt)
            {
                uint ignoreFlags = (uint)(_VSRDTFLAGS.RDT_ProjSlnDocument | _VSRDTFLAGS.RDT_VirtualDocument);
                if ((info.Flags & ignoreFlags) == 0)
                {
                    list.Add(info.Moniker);
                }
            }
            return list.ToArray();
        }

        public string[] GetRecentFiles(uint count)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var items = new string[count];
            MRUItemsStore.GetMRUItems(VSConstants.MruList.SolutionFiles, "", count, items);

            return items.Select(x => x?.Split('|')?.FirstOrDefault()).Where(x => !string.IsNullOrEmpty(x)).ToArray();
        }

        public IEnumerable<(Category, string)> EnumerateSolutionFiles(HashSet<string> knownFiles)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            DTE IDE = GetActiveIDE();
            //Debug.WriteLine("Active Doc: " + IDE.ActiveDocument.FullName + " " + IDE.ActiveDocument.ProjectItem.Name);

            // Current openning files
            var allProjects = new HashSet<Project>();
            foreach (Project _proj in IDE.Solution.Projects)
            {
                foreach (Project project in ExpandProjectRecursive(_proj))
                {
                    allProjects.Add(project);
                }
            }

            var currentProject = IDE.ActiveDocument?.ProjectItem?.ContainingProject;
            var activeProjects = FindActiveProjects(allProjects);

            var sortedProjects = allProjects.Select(p =>
            {
                if (p == currentProject)
                {
                    return (p, Category.CurrentProjectFile);
                }
                else if (activeProjects.Contains(p))
                {
                    return (p, Category.ActiveProjectFile);
                }
                else
                {
                    return (p, Category.SolutionFile);
                }
            }).OrderByDescending(x => x.Item2).ToArray();

            // Files in projects of open files
            foreach (var (project, category) in sortedProjects)
            {
                foreach (var item in EnumerateProjectItems(project.ProjectItems))
                {
                    if (item.FileNames[0].Contains(project.FullName))
                    {
                        continue;
                    }
                    var filePath = item.FileNames[0];
                    if (!knownFiles.Contains(filePath))
                    {
                        knownFiles.Add(filePath);
                        yield return (category, filePath);
                    }
                }
            }
        }

        private List<Project> FindActiveProjects(HashSet<Project> knownProjects)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // IDE.Documents => doesn't work
            // project.Properties.Cast<Property>().FirstOrDefault(p => p.Name == "ProjectGuid") => doesn't work
            //
            // Using workaround comparing both project directory and project name

            var projectIdentifiers = new HashSet<string>();

            var rdt = new RunningDocumentTable(package);
            foreach (var info in rdt)
            {
                uint ignoreFlags = (uint)(_VSRDTFLAGS.RDT_ProjSlnDocument | _VSRDTFLAGS.RDT_VirtualDocument);
                if ((info.Flags & ignoreFlags) != 0)
                {
                    continue;
                }

                info.Hierarchy.GetProperty((uint)VSConstants.VSITEMID.Root, (int)__VSHPROPID.VSHPROPID_ProjectName, out var projectName);
                info.Hierarchy.GetProperty((uint)VSConstants.VSITEMID.Root, (int)__VSHPROPID.VSHPROPID_ProjectDir, out var projectDir);

                if (projectName != null && projectDir != null)
                {
                    var projectIdentifier = projectDir + "\\" + projectName;
                    projectIdentifiers.Add(projectIdentifier);
                    //Debug.WriteLine($"RunningDocumentTable {info.Moniker} Proj:{projectIdentifier}");
                }
            }

            var results = new List<Project>();
            foreach (string projectIdentifier in projectIdentifiers)
            {
                var dir = Path.GetDirectoryName(projectIdentifier);
                var name = Path.GetFileName(projectIdentifier);
                foreach (var project in knownProjects)
                {
                    var fullname = project.FullName;
                    if (!File.Exists(fullname))
                    {
                        continue;
                    }
                    //Debug.WriteLine($"Check Project: {Path.GetDirectoryName(fullname)} {project.Name} # {dir} {name}");
                    if (Path.GetDirectoryName(fullname) == dir && project.Name == name)
                    {
                        results.Add(project);
                        //Debug.WriteLine($"Found Project: {project.FullName} {project.Name}");
                    }
                }
            }
            return results;
        }

        private static IEnumerable<Project> ExpandProjectRecursive(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            const string SolutionFolder = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";
            if (project.Kind == SolutionFolder)
            {
                foreach (ProjectItem item in project.ProjectItems)
                {
                    var sub = item.SubProject;
                    if (sub != null)
                    {
                        foreach (Project project2 in ExpandProjectRecursive(sub))
                        {
                            yield return project2;
                        }
                    }
                }
            }
            else
            {
                yield return project;
            }
        }


        private IEnumerable<ProjectItem> EnumerateProjectItems(ProjectItems items)
        {
            const string ProjectFileGuid = VSConstants.ItemTypeGuid.PhysicalFile_string;
            const string ProjectFolderGuid = VSConstants.ItemTypeGuid.PhysicalFolder_string;
            const string ProjectVirtualFolderGuid = VSConstants.ItemTypeGuid.VirtualFolder_string;

            if (items == null)
            {
                yield return null;
            }
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (ProjectItem item in items)
            {
                string itemKind = "";
                try
                {
                    itemKind = item.Kind;
                }
                catch (Exception)
                {
                    // itm.Kind may throw an exception with certain node types like WixExtension (COMException)
                }

                if (itemKind == ProjectFileGuid)
                {
                    yield return item;
                }
                else if (itemKind == ProjectFolderGuid || itemKind == ProjectVirtualFolderGuid)
                {
                    foreach (var item2 in EnumerateProjectItems(item.ProjectItems))
                    {
                        yield return item2;
                    }
                }
                else
                {
                    //Debug.WriteLine(string.Format("  Item:{0} {1}", item.Name, item.Kind));
                }
            }
        }

        /// <summary>
        /// Shows the tool window when the menu item is clicked.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Get the instance number 0 of this tool window. This window is single instance so this instance
            // is actually the only one.
            // The last flag is set to true so that if the tool window does not exists it will be created.
            //ToolWindowPane window = this.package.FindToolWindow(typeof(LevyFlightWindow), 0, true);
            //if ((null == window) || (null == window.Frame))
            //{
            //    throw new NotSupportedException("Cannot create tool window");
            //}

            //IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
            //Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
            var wnd = new LevyFlightWindow(this);
            wnd.ShowDialog();
        }
    }
}
