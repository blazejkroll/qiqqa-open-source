﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using icons;
using Qiqqa.Common.Configuration;
using Qiqqa.Documents.PDF.CitationManagerStuff;
using Qiqqa.UtilisationTracking;
using Utilities;
using Utilities.GUI;
using Utilities.Misc;
using Utilities.ProcessTools;
using UserControl = System.Windows.Controls.UserControl;

namespace Qiqqa.DocumentLibrary.BundleLibrary.LibraryBundleCreation
{
    /// <summary>
    /// Interaction logic for LibraryBundleCreationControl.xaml
    /// </summary>
    public partial class LibraryBundleCreationControl : UserControl
    {
        public static readonly string TITLE = "Bundle Library Builder";
        private Library library = null;
        private BundleLibraryManifest manifest = null;

        public LibraryBundleCreationControl()
        {
            InitializeComponent();

            CmdOCRAndIndex.Caption = "Force OCR and Indexing";
            CmdOCRAndIndex.Click += CmdOCRAndIndex_Click;

            CmdCrossReference.Caption = "Discover Cross-References";
            CmdCrossReference.Click += CmdCrossReference_Click;

            CmdAutoTags.Caption = "Generate AutoTags";
            CmdAutoTags.Click += CmdAutoTags_Click;

            CmdThemes.Caption = "Find Themes";
            CmdThemes.Click += CmdThemes_Click;

            CmdCreateBundle.Caption = "Create Bundle Library";
            CmdCreateBundle.Icon = Icons.GetAppIcon(Icons.BuildBundleLibrary);
            CmdCreateBundle.CaptionDock = Dock.Bottom;

            CmdCreateBundle.Click += CmdCreateBundle_Click;
        }

        private void CmdCreateBundle_Click(object sender, RoutedEventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Please select the folder into which the two Library Bundle files should be placed.";
                DialogResult result = dialog.ShowDialog();
                if (result == DialogResult.OK)
                {
                    CreateBundle(dialog.SelectedPath.ToString());
                }
                else
                {
                    MessageBoxes.Warn("Your Library Bundle creation has been cancelled.");
                }
            }
        }

        private void CreateBundle(string target_directory)
        {
            string target_filename_bundle_manifest = Path.GetFullPath(Path.Combine(target_directory, manifest.Id + Common.EXT_BUNDLE_MANIFEST));
            string target_filename_bundle = Path.GetFullPath(Path.Combine(target_directory, manifest.Id + Common.EXT_BUNDLE));

            // Check that the details of the manifest are reasonable
            try
            {
                new Uri(manifest.BaseUrl);
            }
            catch (Exception)
            {
                MessageBoxes.Warn("Your base URL of '{0}' is invalid.  Please correct it and try again.", manifest.BaseUrl);
                return;
            }

            // Smash out the manifest
            string json = manifest.ToJSON();
            File.WriteAllText(target_filename_bundle_manifest, json);

            // Smash out the bundle
            string source_directory = Path.GetFullPath(Path.Combine(library.LIBRARY_BASE_PATH, @"*"));
            string directory_exclusion_parameter = (manifest.IncludesPDFs ? "" : "-xr!documents");
            string parameters = String.Format("a -tzip -mm=Deflate -mmt=on -mx9 \"{0}\" \"{1}\" {2}", target_filename_bundle, source_directory, directory_exclusion_parameter);

            // Watch the zipper
            SafeThreadPool.QueueUserWorkItem(o => TailZIPProcess(manifest, parameters));
        }

        private static void TailZIPProcess(BundleLibraryManifest manifest, string parameters)
        {
            using (Process zip_process = Process.Start(ConfigurationManager.Instance.Program7ZIP, parameters))
            {
                using (ProcessOutputReader process_output_reader = new ProcessOutputReader(zip_process))
                {
                    string STATUS_TOKEN = "Bundle-" + manifest.Version;

                    StatusManager.Instance.ClearCancelled(STATUS_TOKEN);

                    int iteration = 0;
                    while (true)
                    {
                        ++iteration;

                        if (StatusManager.Instance.IsCancelled(STATUS_TOKEN))
                        {
                            zip_process.Kill();
                            zip_process.WaitForExit(500);

                            Logging.Error("Cancelled creation of Bundle Library:\n--- Parameters: {0}\n{1}", parameters, process_output_reader.GetOutputsDumpString());

                            StatusManager.Instance.UpdateStatus(STATUS_TOKEN, "Cancelled creation of Bundle Library.");
                            return;
                        }

                        if (zip_process.HasExited)
                        {
                            Logging.Info("Completed creation of Bundle Library:\n--- Parameters: {0}\n{1}", parameters, process_output_reader.GetOutputsDumpString());

                            StatusManager.Instance.UpdateStatus(STATUS_TOKEN, "Completed creation of Bundle Library.");
                            return;
                        }

                        StatusManager.Instance.UpdateStatusBusy(STATUS_TOKEN, "Creating Bundle Library...", iteration, iteration + 1, true);

                        Thread.Sleep(1000);
                    }
                }
            }
        }

        private void CmdThemes_Click(object sender, RoutedEventArgs e)
        {
            SafeThreadPool.QueueUserWorkItem(o => library.ExpeditionManager.RebuildExpedition(library.ExpeditionManager.RecommendedThemeCount, true, true, null));
        }

        private void CmdAutoTags_Click(object sender, RoutedEventArgs e)
        {
            SafeThreadPool.QueueUserWorkItem(o => library.AITagManager.Regenerate(null));
        }

        private void CmdCrossReference_Click(object sender, RoutedEventArgs e)
        {
            FeatureTrackingManager.Instance.UseFeature(Features.Library_GenerateReferences);
            SafeThreadPool.QueueUserWorkItem(o => CitationFinder.FindCitations(library));

        }

        private void CmdOCRAndIndex_Click(object sender, RoutedEventArgs e)
        {
            foreach (var pdf_document in library.PDFDocuments)
            {
                pdf_document.Library.LibraryIndex.ReIndexDocument(pdf_document);
            }
        }

        public void ReflectLibrary(Library library_)
        {
            library = library_;
            manifest = new BundleLibraryManifest();

            string bundle_title = library_.WebLibraryDetail.Title + " Bundle Library";
            bundle_title = bundle_title.Replace("Library Bundle Library", "Bundle Library");

            // Set the manifest
            manifest.Id = "BUNDLE_" + library_.WebLibraryDetail.Id;
            manifest.Version = DateTime.UtcNow.ToString("yyyyMMdd.HHmmss");

            manifest.Title = bundle_title;
            manifest.Description = library_.WebLibraryDetail.Description;

            // GUI updates
            DataContext = manifest;
            ObjRunLibraryName.Text = library_.WebLibraryDetail.Title;

            ResetProgress();
        }

        private void ResetProgress()
        {
        }
    }
}
