using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;

namespace MadsKristensen.AddAnyFile
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", Vsix.Version, IconResourceID = 400)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuids.guidAddAnyFilePkgString)]
    public sealed class AddAnyFilePackage : AsyncPackage
    {
        public static DTE2 _dte;

        protected override async System.Threading.Tasks.Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();

            _dte = await GetServiceAsync(typeof(DTE)) as DTE2;

            Logger.Initialize(this, Vsix.Name);

            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService mcs)
            {
                var menuCommandID = new CommandID(PackageGuids.guidAddAnyFileCmdSet, PackageIds.cmdidMyCommand);
                var menuItem = new OleMenuCommand(ExecuteAsync, menuCommandID);
                mcs.AddCommand(menuItem);
            }
        }

        private async void ExecuteAsync(object sender, EventArgs e)
        {
            object selectedItem = ProjectHelpers.GetSelectedItem();
            string folder = FindFolder(selectedItem);

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return;
            }

            var selectedProjectItem = selectedItem as ProjectItem;
            var selectedProject = selectedItem as Project;
            Project project = selectedProjectItem?.ContainingProject ?? selectedProject ?? ProjectHelpers.GetActiveProject();

            if (project == null)
            {
                return;
            }

            string input = PromptForFileName(folder).TrimStart('/', '\\').Replace("/", "\\");

            if (string.IsNullOrEmpty(input))
            {
                return;
            }

            string[] parsedInputs = GetParsedInput(input);

            foreach (string inputItem in parsedInputs)
            {
                input = inputItem;

                if (input.EndsWith("\\", StringComparison.Ordinal))
                {
                    input = input + "__dummy__";
                }

                var inputLower = input.ToLower();
                var inputCamel = inputLower.Substring(0, 1).ToUpper() + inputLower.Substring(1);

                //添加父级文件夹
                var appFolder = Path.Combine(folder, inputCamel);
                if (!Directory.Exists(appFolder))
                {
                    Directory.CreateDirectory(appFolder);
                    project.AddDirectoryToProject(new DirectoryInfo(appFolder));
                }


                foreach (ProjectItem childProject in project.ProjectItems)
                {

                    if (childProject.Name == inputCamel)
                    {
                        //添加dto文件夹
                        var dtoFolder = Path.Combine(appFolder, "Dto");
                        if (!Directory.Exists(dtoFolder))
                        {
                            Directory.CreateDirectory(dtoFolder);
                            childProject.ProjectItems.AddFromDirectory(dtoFolder);
                        }

                        //添加几个dto类及映射类
                        await CreateDtoFile(inputCamel, dtoFolder, selectedItem, childProject, project, TemplateType.DefaultDto);
                        await CreateDtoFile(inputCamel, dtoFolder, selectedItem, childProject, project, TemplateType.CreateDto);
                        await CreateDtoFile(inputCamel, dtoFolder, selectedItem, childProject, project, TemplateType.UpdateDto);
                        await CreateDtoFile(inputCamel, dtoFolder, selectedItem, childProject, project, TemplateType.PagedDto);
                        await CreateDtoFile(inputCamel, dtoFolder, selectedItem, childProject, project, TemplateType.MapProfile);
                        break;
                    }
                }

                //添加接口
                await CreateFile(inputCamel, appFolder, selectedItem, project, TemplateType.Interface);

                //添加业务类
                await CreateFile(inputCamel, appFolder, selectedItem, project, TemplateType.Class);
            }
        }



        private async System.Threading.Tasks.Task CreateFile(string inputCamel, string folder, object selectedItem, Project project, TemplateType templateType)
        {
            var file = CreateFileInfo(inputCamel, folder, templateType);

            PackageUtilities.EnsureOutputPath(folder);

            if (!file.Exists)
            {
                int position = await WriteFileAsync(project, file.FullName, inputCamel, templateType);
                try
                {
                    ProjectItem projectItem = null;
                    if (selectedItem is ProjectItem projItem)
                    {
                        if ("{6BB5F8F0-4483-11D3-8BCF-00C04F8EC28C}" == projItem.Kind) // Constants.vsProjectItemKindVirtualFolder
                        {
                            projectItem = projItem.ProjectItems.AddFromFile(file.FullName);
                        }
                    }

                    if (projectItem == null)
                    {
                        projectItem = project.AddFileToProject(file);
                    }

                    if (file.FullName.EndsWith("__dummy__"))
                    {
                        projectItem?.Delete();
                        return;
                    }

                    VsShellUtilities.OpenDocument(this, file.FullName);

                    // Move cursor into position
                    if (position > 0)
                    {
                        Microsoft.VisualStudio.Text.Editor.IWpfTextView view = ProjectHelpers.GetCurentTextView();

                        view?.Caret.MoveTo(new SnapshotPoint(view.TextBuffer.CurrentSnapshot, position));
                    }

                    _dte.ExecuteCommand("SolutionExplorer.SyncWithActiveDocument");
                    _dte.ActiveDocument.Activate();
                }
                catch (Exception ex)
                {
                    Logger.Log(ex);
                }
            }
            else
            {
                System.Windows.Forms.MessageBox.Show("The file '" + file + "' already exist.");
            }
        }

        private static FileInfo CreateFileInfo(string inputCamel, string folder, TemplateType templateType)
        {
            FileInfo file;
            switch (templateType)
            {
                case TemplateType.CreateDto:
                    file = new FileInfo(Path.Combine(folder, $"Create{inputCamel}Dto.cs"));
                    break;
                case TemplateType.UpdateDto:
                    file = new FileInfo(Path.Combine(folder, $"Update{inputCamel}Dto.cs"));
                    break;
                case TemplateType.DefaultDto:
                    file = new FileInfo(Path.Combine(folder, $"{inputCamel}Dto.cs"));
                    break;
                case TemplateType.PagedDto:
                    file = new FileInfo(Path.Combine(folder, $"Paged{inputCamel}ResultRequestDto.cs"));
                    break;
                case TemplateType.MapProfile:
                    file = new FileInfo(Path.Combine(folder, $"{inputCamel}MapProfile.cs"));
                    break;
                case TemplateType.Interface:
                    file = new FileInfo(Path.Combine(folder, $"I{inputCamel}AppService.cs"));
                    break;
                case TemplateType.Class:
                    file = new FileInfo(Path.Combine(folder, $"{inputCamel}AppService.cs"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(templateType), templateType, null);
            }

            return file;
        }


        private async System.Threading.Tasks.Task CreateDtoFile(string inputCamel, string folder,
            object selectedItem,
            ProjectItem dtoProject,
            Project project,
            TemplateType templateType)
        {
            var file = CreateFileInfo(inputCamel, folder, templateType);
            PackageUtilities.EnsureOutputPath(folder);

            if (!file.Exists)
            {
                int position = await WriteFileAsync(project, file.FullName, inputCamel, templateType);
                try
                {
                    ProjectItem projectItem = null;
                    if (selectedItem is ProjectItem projItem)
                    {
                        if ("{6BB5F8F0-4483-11D3-8BCF-00C04F8EC28C}" == projItem.Kind) // Constants.vsProjectItemKindVirtualFolder
                        {
                            projectItem = projItem.ProjectItems.AddFromFile(file.FullName);
                        }
                    }

                    if (projectItem == null)
                    {
                        projectItem = dtoProject.ProjectItems.AddFromFile(file.FullName);
                    }

                    if (file.FullName.EndsWith("__dummy__"))
                    {
                        projectItem?.Delete();
                        return;
                    }

                    VsShellUtilities.OpenDocument(this, file.FullName);

                    // Move cursor into position
                    if (position > 0)
                    {
                        Microsoft.VisualStudio.Text.Editor.IWpfTextView view = ProjectHelpers.GetCurentTextView();

                        view?.Caret.MoveTo(new SnapshotPoint(view.TextBuffer.CurrentSnapshot, position));
                    }

                    _dte.ExecuteCommand("SolutionExplorer.SyncWithActiveDocument");
                    _dte.ActiveDocument.Activate();
                }
                catch (Exception ex)
                {
                    Logger.Log(ex);
                }
            }
            else
            {
                System.Windows.Forms.MessageBox.Show("The file '" + file + "' already exist.");
            }
        }

        private static async Task<int> WriteFileAsync(Project project, string file, string inputCamel, TemplateType templateType)
        {
            try
            {
                string template = await TemplateMap.GetTemplateFilePathAsync(project, file, templateType, inputCamel);

                if (!string.IsNullOrEmpty(template))
                {
                    int index = template.IndexOf('$');

                    if (index > -1)
                    {
                        template = template.Remove(index, 1);
                    }

                    await WriteToDiskAsync(file, template);
                    return index;
                }

                await WriteToDiskAsync(file, string.Empty);

                return 0;
            }
            catch (Exception e)
            {
                return 0;
            }
        }

        private static async System.Threading.Tasks.Task WriteToDiskAsync(string file, string content)
        {
            using (var writer = new StreamWriter(file, false, GetFileEncoding(file)))
            {
                await writer.WriteAsync(content);
            }
        }

        private static Encoding GetFileEncoding(string file)
        {
            string[] noBom = { ".cmd", ".bat", ".json" };
            string ext = Path.GetExtension(file).ToLowerInvariant();

            if (noBom.Contains(ext))
                return new UTF8Encoding(false);

            return new UTF8Encoding(true);
        }

        static string[] GetParsedInput(string input)
        {
            // var tests = new string[] { "file1.txt", "file1.txt, file2.txt", ".ignore", ".ignore.(old,new)", "license", "folder/",
            //    "folder\\", "folder\\file.txt", "folder/.thing", "page.aspx.cs", "widget-1.(html,js)", "pages\\home.(aspx, aspx.cs)",
            //    "home.(html,js), about.(html,js,css)", "backup.2016.(old, new)", "file.(txt,txt,,)", "file_@#d+|%.3-2...3^&.txt" };
            var pattern = new Regex(@"[,]?([^(,]*)([\.\/\\]?)[(]?((?<=[^(])[^,]*|[^)]+)[)]?");
            var results = new List<string>();
            Match match = pattern.Match(input);

            while (match.Success)
            {
                // Always 4 matches w. Group[3] being the extension, extension list, folder terminator ("/" or "\"), or empty string
                string path = match.Groups[1].Value.Trim() + match.Groups[2].Value;
                string[] extensions = match.Groups[3].Value.Split(',');

                foreach (string ext in extensions)
                {
                    string value = path + ext.Trim();

                    // ensure "file.(txt,,txt)" or "file.txt,,file.txt,File.TXT" returns as just ["file.txt"]
                    if (value != "" && !value.EndsWith(".", StringComparison.Ordinal) && !results.Contains(value, StringComparer.OrdinalIgnoreCase))
                    {
                        results.Add(value);
                    }
                }
                match = match.NextMatch();
            }
            return results.ToArray();
        }

        private string PromptForFileName(string folder)
        {
            var dir = new DirectoryInfo(folder);
            var dialog = new FileNameDialog(dir.Name);

            var hwnd = new IntPtr(_dte.MainWindow.HWnd);
            var window = (System.Windows.Window)HwndSource.FromHwnd(hwnd).RootVisual;
            dialog.Owner = window;

            bool? result = dialog.ShowDialog();
            return (result.HasValue && result.Value) ? dialog.Input : string.Empty;
        }

        private static string FindFolder(object item)
        {
            if (item == null)
                return null;


            if (_dte.ActiveWindow is Window2 window && window.Type == vsWindowType.vsWindowTypeDocument)
            {
                // if a document is active, use the document's containing directory
                Document doc = _dte.ActiveDocument;
                if (doc != null && !string.IsNullOrEmpty(doc.FullName))
                {
                    ProjectItem docItem = _dte.Solution.FindProjectItem(doc.FullName);

                    if (docItem != null && docItem.Properties != null)
                    {
                        string fileName = docItem.Properties.Item("FullPath").Value.ToString();
                        if (File.Exists(fileName))
                            return Path.GetDirectoryName(fileName);
                    }
                }
            }

            string folder = null;

            var projectItem = item as ProjectItem;
            if (projectItem != null && "{6BB5F8F0-4483-11D3-8BCF-00C04F8EC28C}" == projectItem.Kind) //Constants.vsProjectItemKindVirtualFolder
            {
                ProjectItems items = projectItem.ProjectItems;
                foreach (ProjectItem it in items)
                {
                    if (File.Exists(it.FileNames[1]))
                    {
                        folder = Path.GetDirectoryName(it.FileNames[1]);
                        break;
                    }
                }
            }
            else
            {
                var project = item as Project;
                if (projectItem != null)
                {
                    string fileName = projectItem.FileNames[1];

                    if (File.Exists(fileName))
                    {
                        folder = Path.GetDirectoryName(fileName);
                    }
                    else
                    {
                        folder = fileName;
                    }


                }
                else if (project != null)
                {
                    folder = project.GetRootFolder();
                }
            }
            return folder;
        }
    }
}