using EnvDTE;
using Microsoft.VisualStudio.TemplateWizard;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace NINA.Plugin.Template.Wizard {

    public class WizardImplementation : IWizard {
        private WizardForm wizardForm;
        private string pluginName;
        private string pluginNamespace;
        private string pluginClassName;
        private string pluginShortDescription;
        private string pluginLongDescription;
        private string pluginWebsiteUrl;
        private string pluginRepoUrl;
        private string pluginAuthor;
        private string pluginTags;

        // This method is called before opening any item that
        // has the OpenInEditor attribute.
        public void BeforeOpeningFile(ProjectItem projectItem) {
        }

        public void ProjectFinishedGenerating(Project project) {
        }

        // This method is only called for item templates,
        // not for project templates.
        public void ProjectItemFinishedGenerating(ProjectItem projectItem) {
        }

        public void RunStarted(object automationObject,
            Dictionary<string, string> replacementsDictionary,
            WizardRunKind runKind, object[] customParams) {
            try {
                string projectName = string.Empty;
                replacementsDictionary.TryGetValue("$safeprojectname$", out projectName);

                if (string.IsNullOrEmpty(projectName)) {
                    projectName = "MyPlugin";
                }

                wizardForm = new WizardForm() {
                    ProjectName = projectName,
                };
                wizardForm.ShowDialog();

                if (wizardForm.DialogResult == DialogResult.Cancel) {
                    throw new WizardCancelledException();
                }

                pluginName = WizardForm.PluginName;
                pluginNamespace = WizardForm.PluginNamespace;
                pluginClassName = WizardForm.PluginClassName;
                pluginShortDescription = WizardForm.PluginShortDescription;
                pluginLongDescription = WizardForm.PluginLongDescription;
                pluginAuthor = WizardForm.PluginAuthor;
                pluginTags = WizardForm.PluginTags;
                pluginWebsiteUrl = WizardForm.PluginWebsiteUrl;
                pluginRepoUrl = WizardForm.PluginRepoUrl;

                if (string.IsNullOrEmpty(pluginWebsiteUrl)) {
                    pluginWebsiteUrl = "https://mypluginwebsite.com/";
                }

                if (string.IsNullOrEmpty(pluginRepoUrl)) {
                    pluginRepoUrl = "https://mypluginsourcerepo.com/project";
                }

                // Add custom parameters.
                replacementsDictionary.Add("$pluginname$", pluginName);
                replacementsDictionary.Add("$pluginnamespace$", pluginNamespace);
                replacementsDictionary.Add("$pluginclassname$", pluginClassName);
                replacementsDictionary.Add("$pluginshortdesc$", pluginShortDescription);
                replacementsDictionary.Add("$pluginlongdesc$", pluginLongDescription);
                replacementsDictionary.Add("$pluginauthor$", pluginAuthor);
                replacementsDictionary.Add("$plugintags$", pluginTags);
                replacementsDictionary.Add("$pluginwebsiteurl$", pluginWebsiteUrl);
                replacementsDictionary.Add("$pluginrepourl$", pluginRepoUrl);
            } catch (WizardCancelledException) {
                throw;
            } catch (Exception ex) {
                MessageBox.Show(ex.ToString());
            }
        }

        // This method is called after the project is created.
        public void RunFinished() {
        }

        // This method is only called for item templates,
        // not for project templates.
        public bool ShouldAddProjectItem(string filePath) {
            return true;
        }
    }
}