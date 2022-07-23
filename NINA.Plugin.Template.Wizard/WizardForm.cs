using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NINA.Plugin.Template.Wizard {

    public partial class WizardForm : Form {
        private IContainer components = null;
        private ErrorProvider errorProvider;

        private ToolTip wizardToolTip;
        private Label pluginNameLabel;
        private TextBox pluginNameTextBox;
        private Label authorLabel;
        private TextBox authorTextBox;
        private TextBox longDescriptionTextBox;
        private Label shortDescriptionLabel;
        private TextBox repoUrlTextBox;
        private Label repoUrlLabel;
        private TextBox websiteUrlTextBox;
        private Label websiteUrlLabel;
        private TextBox shortDescriptionTextBox;
        private Label longDescriptionLabel;
        private PictureBox ninaLogo;
        private Button cancelButton;
        private TextBox tagsTextBox;
        private Label tagsLabel;
        private Label namespaceLabel;
        private TextBox namespaceTextBox;
        private Button createButton;

        protected override void Dispose(bool disposing) {
            if (disposing && components != null) {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        public WizardForm() {
            InitializeComponent();
            this.namespaceTextBox.Text = "." + ProjectName;
        }

        private void InitializeComponent() {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(WizardForm));
            this.wizardToolTip = new System.Windows.Forms.ToolTip(this.components);
            this.pluginNameLabel = new System.Windows.Forms.Label();
            this.authorLabel = new System.Windows.Forms.Label();
            this.shortDescriptionLabel = new System.Windows.Forms.Label();
            this.websiteUrlLabel = new System.Windows.Forms.Label();
            this.repoUrlLabel = new System.Windows.Forms.Label();
            this.longDescriptionLabel = new System.Windows.Forms.Label();
            this.tagsLabel = new System.Windows.Forms.Label();
            this.namespaceLabel = new System.Windows.Forms.Label();
            this.pluginNameTextBox = new System.Windows.Forms.TextBox();
            this.authorTextBox = new System.Windows.Forms.TextBox();
            this.errorProvider = new System.Windows.Forms.ErrorProvider(this.components);
            this.createButton = new System.Windows.Forms.Button();
            this.shortDescriptionTextBox = new System.Windows.Forms.TextBox();
            this.websiteUrlTextBox = new System.Windows.Forms.TextBox();
            this.repoUrlTextBox = new System.Windows.Forms.TextBox();
            this.longDescriptionTextBox = new System.Windows.Forms.TextBox();
            this.ninaLogo = new System.Windows.Forms.PictureBox();
            this.cancelButton = new System.Windows.Forms.Button();
            this.tagsTextBox = new System.Windows.Forms.TextBox();
            this.namespaceTextBox = new System.Windows.Forms.TextBox();
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ninaLogo)).BeginInit();
            this.SuspendLayout();
            // 
            // pluginNameLabel
            // 
            this.pluginNameLabel.AutoSize = true;
            this.pluginNameLabel.Location = new System.Drawing.Point(35, 15);
            this.pluginNameLabel.Name = "pluginNameLabel";
            this.pluginNameLabel.Size = new System.Drawing.Size(65, 13);
            this.pluginNameLabel.TabIndex = 1;
            this.pluginNameLabel.Text = "Plugin name";
            this.wizardToolTip.SetToolTip(this.pluginNameLabel, "Required. A friendly name for your plugin.\r\nExample: \"Cloud Zapper 4000\"");
            // 
            // authorLabel
            // 
            this.authorLabel.AutoSize = true;
            this.authorLabel.Location = new System.Drawing.Point(62, 41);
            this.authorLabel.Name = "authorLabel";
            this.authorLabel.Size = new System.Drawing.Size(38, 13);
            this.authorLabel.TabIndex = 3;
            this.authorLabel.Text = "Author";
            this.wizardToolTip.SetToolTip(this.authorLabel, "Required. Your name or company\'s name");
            // 
            // shortDescriptionLabel
            // 
            this.shortDescriptionLabel.AutoSize = true;
            this.shortDescriptionLabel.Location = new System.Drawing.Point(14, 67);
            this.shortDescriptionLabel.Name = "shortDescriptionLabel";
            this.shortDescriptionLabel.Size = new System.Drawing.Size(86, 13);
            this.shortDescriptionLabel.TabIndex = 5;
            this.shortDescriptionLabel.Text = "Short description";
            this.wizardToolTip.SetToolTip(this.shortDescriptionLabel, "A short description of what the plugin does");
            // 
            // websiteUrlLabel
            // 
            this.websiteUrlLabel.AutoSize = true;
            this.websiteUrlLabel.Location = new System.Drawing.Point(29, 119);
            this.websiteUrlLabel.Name = "websiteUrlLabel";
            this.websiteUrlLabel.Size = new System.Drawing.Size(71, 13);
            this.websiteUrlLabel.TabIndex = 9;
            this.websiteUrlLabel.Text = "Website URL";
            this.wizardToolTip.SetToolTip(this.websiteUrlLabel, "URL of the plugin\'s website. Not mandatory.");
            // 
            // repoUrlLabel
            // 
            this.repoUrlLabel.AutoSize = true;
            this.repoUrlLabel.Location = new System.Drawing.Point(18, 145);
            this.repoUrlLabel.Name = "repoUrlLabel";
            this.repoUrlLabel.Size = new System.Drawing.Size(82, 13);
            this.repoUrlLabel.TabIndex = 11;
            this.repoUrlLabel.Text = "Repository URL";
            this.wizardToolTip.SetToolTip(this.repoUrlLabel, "URL of the GitHub, Bitbucket, or other repo where the plugin source code will res" +
        "ide. Not mandatory, but advisable if this is a Free/Open Source plugin.");
            // 
            // longDescriptionLabel
            // 
            this.longDescriptionLabel.AutoSize = true;
            this.longDescriptionLabel.Location = new System.Drawing.Point(15, 171);
            this.longDescriptionLabel.Name = "longDescriptionLabel";
            this.longDescriptionLabel.Size = new System.Drawing.Size(85, 13);
            this.longDescriptionLabel.TabIndex = 13;
            this.longDescriptionLabel.Text = "Long description";
            this.wizardToolTip.SetToolTip(this.longDescriptionLabel, "A long description of the plugin. Can be multiple lines and contain markdown form" +
        "atting.");
            // 
            // tagsLabel
            // 
            this.tagsLabel.AutoSize = true;
            this.tagsLabel.Location = new System.Drawing.Point(69, 93);
            this.tagsLabel.Name = "tagsLabel";
            this.tagsLabel.Size = new System.Drawing.Size(31, 13);
            this.tagsLabel.TabIndex = 7;
            this.tagsLabel.Text = "Tags";
            this.wizardToolTip.SetToolTip(this.tagsLabel, "A comma-delimited list of descriptive tags");
            // 
            // namespaceLabel
            // 
            this.namespaceLabel.AutoSize = true;
            this.namespaceLabel.Location = new System.Drawing.Point(36, 277);
            this.namespaceLabel.Name = "namespaceLabel";
            this.namespaceLabel.Size = new System.Drawing.Size(64, 13);
            this.namespaceLabel.TabIndex = 15;
            this.namespaceLabel.Text = "Namespace";
            this.wizardToolTip.SetToolTip(this.namespaceLabel, "Required. The C# namespace for your plugin. It should be unique and not have the " +
        "potential of colliding with another namespace.\r\nExample: YourName.NINA.CloudZapp" +
        "er");
            // 
            // pluginNameTextBox
            // 
            this.pluginNameTextBox.Location = new System.Drawing.Point(106, 12);
            this.pluginNameTextBox.Name = "pluginNameTextBox";
            this.pluginNameTextBox.Size = new System.Drawing.Size(261, 20);
            this.pluginNameTextBox.TabIndex = 2;
            this.pluginNameTextBox.LostFocus += new System.EventHandler(this.pluginNameTextBox_LostFocus);
            this.pluginNameTextBox.Validating += new System.ComponentModel.CancelEventHandler(this.pluginNameTextBox_Validating);
            this.pluginNameTextBox.Validated += new System.EventHandler(this.pluginNameTextBox_Validated);
            // 
            // authorTextBox
            // 
            this.authorTextBox.Location = new System.Drawing.Point(106, 38);
            this.authorTextBox.Name = "authorTextBox";
            this.authorTextBox.Size = new System.Drawing.Size(261, 20);
            this.authorTextBox.TabIndex = 4;
            this.authorTextBox.TextChanged += new System.EventHandler(this.authorTextBox_TextChanged);
            this.authorTextBox.Validating += new System.ComponentModel.CancelEventHandler(this.authorTextBox_Validating);
            this.authorTextBox.Validated += new System.EventHandler(this.authorTextBox_Validated);
            // 
            // errorProvider
            // 
            this.errorProvider.ContainerControl = this;
            // 
            // createButton
            // 
            this.createButton.Location = new System.Drawing.Point(251, 321);
            this.createButton.Name = "createButton";
            this.createButton.Size = new System.Drawing.Size(116, 25);
            this.createButton.TabIndex = 17;
            this.createButton.Text = "Create";
            this.createButton.Click += new System.EventHandler(this.createButton_Click);
            // 
            // shortDescriptionTextBox
            // 
            this.shortDescriptionTextBox.Location = new System.Drawing.Point(106, 64);
            this.shortDescriptionTextBox.Name = "shortDescriptionTextBox";
            this.shortDescriptionTextBox.Size = new System.Drawing.Size(261, 20);
            this.shortDescriptionTextBox.TabIndex = 6;
            // 
            // websiteUrlTextBox
            // 
            this.websiteUrlTextBox.Location = new System.Drawing.Point(106, 116);
            this.websiteUrlTextBox.Name = "websiteUrlTextBox";
            this.websiteUrlTextBox.Size = new System.Drawing.Size(261, 20);
            this.websiteUrlTextBox.TabIndex = 10;
            // 
            // repoUrlTextBox
            // 
            this.repoUrlTextBox.Location = new System.Drawing.Point(106, 142);
            this.repoUrlTextBox.Name = "repoUrlTextBox";
            this.repoUrlTextBox.Size = new System.Drawing.Size(261, 20);
            this.repoUrlTextBox.TabIndex = 12;
            // 
            // longDescriptionTextBox
            // 
            this.longDescriptionTextBox.AcceptsReturn = true;
            this.longDescriptionTextBox.AcceptsTab = true;
            this.longDescriptionTextBox.Location = new System.Drawing.Point(106, 168);
            this.longDescriptionTextBox.Multiline = true;
            this.longDescriptionTextBox.Name = "longDescriptionTextBox";
            this.longDescriptionTextBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.longDescriptionTextBox.Size = new System.Drawing.Size(261, 84);
            this.longDescriptionTextBox.TabIndex = 14;
            // 
            // ninaLogo
            // 
            this.ninaLogo.Image = ((System.Drawing.Image)(resources.GetObject("ninaLogo.Image")));
            this.ninaLogo.Location = new System.Drawing.Point(12, 321);
            this.ninaLogo.Name = "ninaLogo";
            this.ninaLogo.Size = new System.Drawing.Size(198, 61);
            this.ninaLogo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.ninaLogo.TabIndex = 18;
            this.ninaLogo.TabStop = false;
            // 
            // cancelButton
            // 
            this.cancelButton.Location = new System.Drawing.Point(251, 352);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(116, 23);
            this.cancelButton.TabIndex = 18;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            this.cancelButton.Click += new System.EventHandler(this.cancelButton_Click);
            // 
            // tagsTextBox
            // 
            this.tagsTextBox.Location = new System.Drawing.Point(106, 90);
            this.tagsTextBox.Name = "tagsTextBox";
            this.tagsTextBox.Size = new System.Drawing.Size(261, 20);
            this.tagsTextBox.TabIndex = 8;
            // 
            // namespaceTextBox
            // 
            this.namespaceTextBox.Location = new System.Drawing.Point(106, 274);
            this.namespaceTextBox.Name = "namespaceTextBox";
            this.namespaceTextBox.Size = new System.Drawing.Size(261, 20);
            this.namespaceTextBox.TabIndex = 16;
            this.namespaceTextBox.Validating += new System.ComponentModel.CancelEventHandler(this.namespaceTextBox_Validating);
            this.namespaceTextBox.Validated += new System.EventHandler(this.namespaceTextBox_Validated);
            // 
            // wizardToolTip
            //
            this.wizardToolTip.AutoPopDelay = 20000;
            this.wizardToolTip.InitialDelay = 500;
            this.wizardToolTip.ReshowDelay = 100;
            //
            // WizardForm
            // 
            this.AcceptButton = this.createButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(398, 392);
            this.Controls.Add(this.namespaceLabel);
            this.Controls.Add(this.namespaceTextBox);
            this.Controls.Add(this.tagsTextBox);
            this.Controls.Add(this.tagsLabel);
            this.Controls.Add(this.cancelButton);
            this.Controls.Add(this.ninaLogo);
            this.Controls.Add(this.longDescriptionTextBox);
            this.Controls.Add(this.longDescriptionLabel);
            this.Controls.Add(this.repoUrlTextBox);
            this.Controls.Add(this.repoUrlLabel);
            this.Controls.Add(this.websiteUrlTextBox);
            this.Controls.Add(this.websiteUrlLabel);
            this.Controls.Add(this.shortDescriptionTextBox);
            this.Controls.Add(this.shortDescriptionLabel);
            this.Controls.Add(this.authorTextBox);
            this.Controls.Add(this.authorLabel);
            this.Controls.Add(this.pluginNameLabel);
            this.Controls.Add(this.pluginNameTextBox);
            this.Controls.Add(this.createButton);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "WizardForm";
            this.Text = "NINA Plugin Template Wizard";
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ninaLogo)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        public static string PluginName { get; set; } = string.Empty;
        public static string PluginNamespace { get; set; } = string.Empty;
        public static string PluginShortDescription { get; set; } = string.Empty;
        public static string PluginLongDescription { get; set; } = string.Empty;
        public static string PluginAuthor { get; set; } = string.Empty;
        public static string PluginTags { get; set; } = string.Empty;
        public static string PluginWebsiteUrl { get; set; } = string.Empty;
        public static string PluginRepoUrl { get; set; } = string.Empty;
        public static string PluginClassName { get; set; } = string.Empty;
        public string ProjectName { get; set; }

        private void createButton_Click(object sender, EventArgs e) {
            if (string.IsNullOrEmpty(pluginNameTextBox.Text.Trim())) {
                errorProvider.SetError(this.pluginNameTextBox, "Please specify the plugin's name");
                return;
            }

            PluginName = pluginNameTextBox.Text;
            PluginNamespace = namespaceTextBox.Text;
            PluginShortDescription = shortDescriptionTextBox.Text;
            PluginLongDescription = longDescriptionTextBox.Text;
            PluginAuthor = authorTextBox.Text;
            PluginWebsiteUrl = websiteUrlTextBox.Text;
            PluginRepoUrl = repoUrlTextBox.Text;

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void cancelButton_Click(object sender, EventArgs e) {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void pluginNameTextBox_LostFocus(object sender, EventArgs e) {
            var autoNamespace = this.pluginNameTextBox.Text.Trim();
            autoNamespace = RemoveDiacritics(autoNamespace);
            autoNamespace = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(autoNamespace.ToLower());
            autoNamespace = autoNamespace.Replace(" ", string.Empty);
            autoNamespace = Regex.Replace(autoNamespace, @"[^a-zA-Z0-9 -]", string.Empty);
            PluginClassName = autoNamespace;
        }

        private void pluginNameTextBox_Validating(object sender, CancelEventArgs e) {
            if (string.IsNullOrEmpty(pluginNameTextBox.Text)) {
                e.Cancel = true;
                errorProvider.SetError(pluginNameTextBox, "Please specify the plugin's name");
            }
        }

        private void pluginNameTextBox_Validated(object sender, EventArgs e) {
            errorProvider.SetError(this.pluginNameTextBox, string.Empty);
        }

        private void namespaceTextBox_Validating(object sender, CancelEventArgs e) {
            Regex rxValidateNamespace = new Regex(@"^[a-zA-Z]([a-zA-Z0-9\.]*)$");
            if (!rxValidateNamespace.IsMatch(namespaceTextBox.Text)) {
                e.Cancel = true;
                errorProvider.SetError(namespaceTextBox, "Invalid namespace. It must begin with a letter and contain only alphanumeric words separated by periods");
            }

            if (string.IsNullOrEmpty(namespaceTextBox.Text)) {
                e.Cancel = true;
                errorProvider.SetError(namespaceTextBox, "Please specify the plugin's namespace");
            }
        }

        private void namespaceTextBox_Validated(object sender, EventArgs e) {
            errorProvider.SetError(this.namespaceTextBox, string.Empty);
        }

        private void authorTextBox_TextChanged(object sender, EventArgs e) {
            var autoNamespace = this.authorTextBox.Text.Trim();
            autoNamespace = RemoveDiacritics(autoNamespace);
            autoNamespace = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(autoNamespace.ToLower());
            autoNamespace = autoNamespace.Replace(" ", string.Empty);
            autoNamespace = Regex.Replace(autoNamespace, @"[^a-zA-Z0-9 -]", string.Empty);
            autoNamespace += ".NINA." + PluginClassName;
            this.namespaceTextBox.Text = autoNamespace;
        }

        private void authorTextBox_Validating(object sender, CancelEventArgs e) {
            if (string.IsNullOrEmpty(authorTextBox.Text)) {
                e.Cancel = true;
                errorProvider.SetError(authorTextBox, "Please specify an author's name");
            }
        }

        private void authorTextBox_Validated(object sender, EventArgs e) {
            errorProvider.SetError(this.authorTextBox, string.Empty);
        }

        private string RemoveDiacritics(string s) {
            string normalizedString = s.Normalize(NormalizationForm.FormD);
            StringBuilder stringBuilder = new StringBuilder();

            for (int i = 0; i < normalizedString.Length; i++) {
                char c = normalizedString[i];
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    stringBuilder.Append(c);
            }

            return stringBuilder.ToString();
        }
    }
}