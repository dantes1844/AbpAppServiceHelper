using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AbpAppServiceHelper.Helpers;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace MadsKristensen.AddAnyFile
{
    public enum TemplateType
    {
        CreateDto,
        UpdateDto,
        DefaultDto,
        PagedDto,
        MapProfile,
        Interface,
        Class
    }

    static class TemplateMap
    {
        static readonly string _folder;
        static readonly string[] _templateFiles;
        const string _defaultExt = ".txt";

        static TemplateMap()
        {
            string assembly = Assembly.GetExecutingAssembly().Location;
            _folder = Path.Combine(Path.GetDirectoryName(assembly), "Templates");
            _templateFiles = Directory.GetFiles(_folder, "*" + _defaultExt, SearchOption.AllDirectories);
        }

        public static async Task<string> GetTemplateFilePathAsync(Project project, string file, TemplateType templateType, string inputCamel)
        {
            string name = Path.GetFileName(file);
            string safeName = name.StartsWith(".") ? name : Path.GetFileNameWithoutExtension(file);
            string relative = PackageUtilities.MakeRelative(project.GetRootFolder(), Path.GetDirectoryName(file));

            string templateFile = _templateFiles.FirstOrDefault(f => Path.GetFileName(f).Equals(templateType + _defaultExt, StringComparison.OrdinalIgnoreCase));

            string template = await ReplaceTokensAsync(project, safeName, relative, templateFile, templateType, inputCamel);
            return NormalizeLineEndings(template);
        }

        private static string GetTemplate(string name)
        {
            return Path.Combine(_folder, name + _defaultExt);
        }

        private static async Task<string> ReplaceTokensAsync(Project project,
            string name,
            string relative,
            string templateFile,
            TemplateType templateType,
            string inputCamel)
        {
            if (string.IsNullOrEmpty(templateFile))
                return templateFile;

            string rootNs = project.GetRootNamespace();
            string ns = string.IsNullOrEmpty(rootNs) ? "MyNamespace" : rootNs;

            if (!string.IsNullOrEmpty(relative))
            {
                ns += "." + ProjectHelpers.CleanNameSpace(relative);
            }

            if (templateType.ToString().EndsWith("Dto") && !ns.EndsWith("Dto"))
            {
                ns += ".Dto";
            }

            using (var reader = new StreamReader(templateFile))
            {
                string content = await reader.ReadToEndAsync();

                content = content.Replace("{namespace}", ns)
                              .Replace("{classname}", name);

                if (templateType == TemplateType.Class || templateType == TemplateType.Interface)
                {
                    content = content.Replace("{entity}", inputCamel)
                        .Replace("{pageddto}", $"Paged{inputCamel}ResultRequestDto")
                        .Replace("{defaultdto}", $"Create{inputCamel}Dto")
                        .Replace("{createdto}", $"Create{inputCamel}Dto")
                        .Replace("{updatedto}", $"Create{inputCamel}Dto")
                        .Replace("{interface}", $"I{inputCamel}AppService")
                        ;
                }
                else if (templateType == TemplateType.MapProfile)
                {
                    content = content.Replace("{entity}", inputCamel)
                            .Replace("{defaultdto}", $"Create{inputCamel}Dto")
                            .Replace("{createdto}", $"Create{inputCamel}Dto")
                            .Replace("{updatedto}", $"Create{inputCamel}Dto")
                        ;
                }


                return content;
            }
        }

        private static string NormalizeLineEndings(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            return Regex.Replace(content, @"\r\n|\n\r|\n|\r", "\r\n");
        }

        private static string AdjustForSpecific(string safeName, string extension)
        {
            if (Regex.IsMatch(safeName, "^I[A-Z].*"))
                return extension += "-interface";

            return extension;
        }
    }
}
