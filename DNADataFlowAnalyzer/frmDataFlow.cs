using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using DNADataFlowAnalyzer.Properties;
using SharpCompress;
using SharpCompress.Archive;
using SharpCompress.Archive.SevenZip;
using SharpCompress.Reader;
using SharpCompress.Common;
using SharpCompress.Compressor;
using SharpCompress.Compressor.BZip2;

namespace DNADataFlowAnalyzer
{
    public partial class frmDataFlow : Form
    {    
        //This is all just initialization, buttons, controls, etc., it's really boring, and trust me, you can skip it all unless you are REALLY interested
        public frmDataFlow()
        {
            InitializeComponent();
            //This is necessary to declare at the start of the program because the startup path is not known at compile time
            Properties.Settings.Default.OutDir = Application.StartupPath;
            //Again, the current time is not known at compile time
            Properties.Settings.Default.OutFilename = String.Format("{0:yyyy-MM-dd}_DataFlowHeaders.csv", DateTime.Now);
        }        
        private void WriteToLogger(string log)
        {
            //This is a standardized way of writing to the textBox logger. I want it to display the current time before the passed string
            this.textBoxLogger.AppendText(DateTime.Now.ToString() + ": " + log + "\n");
        }
        private void frmDataFlow_HelpButtonClicked(object sender, CancelEventArgs e)
        {
            //Boring, boring popup
            MessageBox.Show("eDNA Data Flow Analyzer Version " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() +
            ". Please see Eric Strong for any suggestions, comments, or bugs.",
            "Help", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
        }
        private void toolStripButtonAdjustSettings_Click(object sender, EventArgs e)
        {
            //This will open the settings dialog. All options are bound to program properties.
            Form frmset = new frmSettings();
            frmset.ShowDialog();
        }
        private void toolStripButtonClearLog_Click(object sender, EventArgs e)
        {
            textBoxLogger.Clear();
        }
        private void toolStripButtonExportLog_Click(object sender, EventArgs e)
        {
            //Build the directory (create if necessary) and file path names
            string directory = textBoxOutDir.Text;
            Directory.CreateDirectory(textBoxOutDir.Text);
            string filenameFormat = String.Format("DNADataFlowAnalyzer_Log_{0:yyyy-MM-dd_hh-mm-ss-tt}.txt", DateTime.Now);
            string fullFilename = Path.Combine(directory, filenameFormat);
            File.WriteAllText(fullFilename, textBoxLogger.Text);
            this.WriteToLogger(String.Format("Log file exported to {0}",fullFilename));
        }
        private void buttonOutputDir_Click(object sender, EventArgs e)
        {
            if (folderBrowserOutput.ShowDialog() == DialogResult.OK){ textBoxOutDir.Text = folderBrowserOutput.SelectedPath;}
        }
        private void toolStripButtonSelectData_Click(object sender, EventArgs e)
        {
            //If we are selecting new data files, we want to make sure the old list (and listBox) is cleared to make way for the FUTURE!
            listBoxFiles.Items.Clear();
            //Allow the user to select new data files, and then add them to the listBox
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                foreach (string fn in openFileDialog1.FileNames) listBoxFiles.Items.Add(fn);
            }
        }    
        private void DisableWhileRunning()
        {
            //We want to disable a lot of buttons while the program is actually running, to avoid threading problems. Obviously, the
            //"RunProgram" button is the most important one to disable.
            this.toolStripProgress.Text = "Running...";
            this.toolStripButtonAdjustSettings.Enabled = false;
            this.toolStripButtonCancel.Enabled = true;
            this.toolStripButtonRunProgram.Enabled = false;
            this.toolStripButtonSelectData.Enabled = false;
        }
        private void EnableWhileNotRunning()
        {
            //Once we're finished with the program, the various buttons can be re-enabled.
            this.toolStripProgressBarFiles.Value = 0;
            this.toolStripProgress.Text = "Not Running";
            this.toolStripButtonAdjustSettings.Enabled = true;
            this.toolStripButtonCancel.Enabled = false;
            this.toolStripButtonRunProgram.Enabled = true;
            this.toolStripButtonSelectData.Enabled = true;
        }
        private void toolStripButtonRunProgram_Click(object sender, EventArgs e)
        {         
            //There are some buttons that should be disabled while running
            this.DisableWhileRunning();
            //I know this type of try-catch loop is not ideal, but it's mostly a fail-safe. I'll try to catch most of the errors within the actual run method.
            try { backgroundWorker1.RunWorkerAsync(); }
            catch (Exception ex)
            {
                this.WriteToLogger(String.Format("ERROR- unspecified error ({0}). Program failed. Please ask Eric Strong for help.",ex.Message));
                //This is easy to forget, but even if the program fails, we want to make sure to re-enable the buttons. Otherwise, the buttons will be re-enabled
                //at the end of program execution
                this.EnableWhileNotRunning();
            }
        }
        private void toolStripButtonCancel_Click(object sender, EventArgs e)
        {
            //Nice job sherlock, if we click the "Cancel" button then the background worker should cancel
            backgroundWorker1.CancelAsync();
        }
        private void backgroundWorker1_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            //Okay, so I use this function differently in my programs. There is probably a better way to 
            int progPerc = e.ProgressPercentage;
            string progressstring = e.UserState.ToString();
            //If the first character is a $, then the progress update is for a particular point
            if (!string.IsNullOrEmpty(progressstring)) this.WriteToLogger(progressstring);
            //Update the overall progress bar
            if (progPerc > 0 && progPerc <= 100) this.toolStripProgressBarFiles.Value = progPerc;
        }
        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            this.WriteToLogger("Data file analysis complete.");
            toolStripProgressBarFiles.Value = 0;
            //Dont forget to enable the buttons after the program is done running. This has to be done in the "RunWorkerCompleted"
            //and not in the main program loop, because the main program loop will execute to the end, only threading the backgroundWorker
            this.EnableWhileNotRunning();
        }   
        //This is the meat of the program. START READING HERE
        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            backgroundWorker1.ReportProgress(0, "*Starting data file analysis...");
            //I just prefer to use the name "dataFiles" instead of carrying around the listBox items
            var dataFiles = listBoxFiles.Items;
            //Prevent errors by making sure the directory actually exists
            Directory.CreateDirectory(Settings.Default.OutDir);
            string outputPath = Path.Combine(Settings.Default.OutDir, Settings.Default.OutFilename);
            using (StreamWriter outfile = new StreamWriter(outputPath))
            {
                //This is the header for the CSV output file
                outfile.WriteLine(String.Join(",","Filename","Site.Service","File DateTime","Compressed Size (bytes)","Uncompressed Size (bytes)", "Last Modified"));
                //Why do I use a for loop instead of foreach? I need the iterator for the progress bar.
                for (int currFileIter = 0; currFileIter < dataFiles.Count; currFileIter++)
                {
                    string filename = dataFiles[currFileIter].ToString();
                    //There's only a few opportunities to check for a pending cancellation, I take it where I can get it
                    if (backgroundWorker1.CancellationPending) break;
                    //This code looks nasty. The current value of the iterator needs to be divided by the total count of files to analyze to get the decimal percentage, but that
                    //needs to be a double or else it's truncated. Then, it's multiplied by 100 to get the percentage, which now needs to be supplied as an int to the ReportProgress
                    int progPerc = (int)(((double)currFileIter * 100.0 / (double) dataFiles.Count));
                    //Why do I supply two parameters to the ReportProgress? You can look at the method itself for more information, but basically I want to update both the 
                    //progress bar (parameter 1) and the logger (parameter 2) at the same time. If the second parameter is empty, the logger isn't updated, and if the first 
                    //parameter isn't between 0 and 100, the progress bar will not be updated.
                    backgroundWorker1.ReportProgress(progPerc,String.Format("Analyzing {0}...",filename));
                    //Here's the second "meaty" function. It will return a string to be written to the data file
                    outfile.WriteLine(AnalyzeArchive(filename));
                }              
            }
        }      
        //Executed for each iteration of the loop within DoWork
        //This entire program is not written from an object-oriented perspective. It would be better to have the following "Form" methods implemented as classes instead.
        //Next version of the program will fix this.
        private StringBuilder AnalyzeArchive(string fileName)
        {
            //I prefer to use a smaller variable name instead of carrying around the long settings path. The data prefix is how the program knows which files are eDNA
            //data files. For instance, the LCS project unsurprisingly uses "LCS" as the prefix. Chevron uses "CPV" or other possibilities.
            string dataPrefix = Settings.Default.DataPrefix;
            //I need a stringBuilder here because the archive file might have multiple files within it, so I won't necessarily return just one line
            var csv = new StringBuilder();
            //This is not very readable, but I wanted to fit it all on a single line
            using (var reader = ReaderFactory.Open(new BZip2Stream(File.OpenRead(fileName), SharpCompress.Compressor.CompressionMode.Decompress)))
            {
                //I don't like using the try-catch loop here
                try
                {
                    while (reader.MoveToNextEntry())
                    {
                        IEntry curEntry = reader.Entry;
                        var fileInfo = new FileInfo(fileName);
                        //This part is 
                        string detectType = curEntry.Key.Remove(3);
                        //There are three options: a data file, a system.log file, and PreMA log files. 
                        if (detectType == dataPrefix) csv.Append(AnalyzeDNAData(curEntry, fileInfo));
                        else if (detectType == "Aud" || detectType == "Sys") csv.AppendLine(AnalyzeLogFile(curEntry,fileInfo,reader,false));
                        else if (detectType == "log") csv.AppendLine(AnalyzeLogFile(curEntry,fileInfo,reader,true));
                        else { csv.AppendLine(curEntry.Key); }
                    }
                }
                catch (EndOfStreamException)
                {
                    //This keep happening, not sure why
                }
                
            }
            return csv;
        }
        //The following methods are used within the "AnalyzeArchive" method
        private string AnalyzeDNAData(IEntry entry, FileInfo fileInfo)
        {
            //The date is in the filename, so we need to get it. This is a convoluted way. I find where "20" starts (since it will very likely be the start of the DateTime),
            //then I find the rest of the file string starting at that index and remove the extension. It will be formated below in the "return"
            int dateIndex = entry.Key.IndexOf("20");
            string dateString = Path.GetFileNameWithoutExtension(entry.Key.Substring(dateIndex));
            //This may hurt readability a little, but I didn't give the variables "local" names. The comments give more information about the output of this method
            return String.Join(",",
                fileInfo.Name, //This is the short name of the file, instead of the full path
                entry.Key.Remove(dateIndex), //The eDNA site and service (everything before the dateIndex)
                String.Format("{0:####/##/## ##:##:##}", Convert.ToInt64(dateString)), //This is the date of the data file, formatted from dateString
                fileInfo.Length.ToString(), //The compressed file size
                entry.Size.ToString(), //The uncompressed file size
                entry.LastModifiedTime.ToString() //The last modification date of the file
                );
        }
        private string AnalyzeLogFile(IEntry entry, FileInfo fileInfo, IReader reader, bool premaLog)
        {
            //If selected in user settings, extract the file to the output directory
            if (Settings.Default.ExtractLogs)
            {
                //The default directory that the logs will be exported to is a folder called "Logs" in the specified output directory
                string formatDate = String.Format("{0:yyyy-MM-dd}", entry.LastModifiedTime.Value);
                string logDir = Path.Combine(Settings.Default.OutDir, "Logs", formatDate);
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, entry.Key);
                //If the log file already exists, make sure it's deleted first. This is also because the "latest" log file for a day is the most updated one
                File.Delete(logPath);
                reader.WriteEntryToFile(logPath);
                //Special case if it's a PreMA log file, because it needs to be extracted again
                if (premaLog) WritePreMALog(logPath, logDir);
            }
            //This may hurt readability a little, but I didn't give the variables "local" names. The comments give more information about the output of this method
            return String.Join(",",
                fileInfo.Name, //This is the short name of the file, instead of the full path
                entry.Key, //Instead of site.service, this will be the name of the log file (e.g. type)
                String.Empty, //The eDNA service time doesn't apply here, so use an empty string
                fileInfo.Length.ToString(), //The compressed file size
                entry.Size.ToString(), //The uncompressed file size
                entry.LastModifiedTime.ToString() //The last modification date of the file
                );
        }
        //This method is optionally used within the AnalyzeLogFile method
        private void WritePreMALog(string logPath, string logDir)
        {
            //Since the PreMA logs are in an archive of their own, we need to extract again
            using (var szipreader = ArchiveFactory.Open(File.OpenRead(logPath)))
            {
                foreach (var innerentry in szipreader.Entries)
                {
                    string logPath2 = logDir + @"\" + innerentry.Key;
                    //If the log file already exists, make sure it's deleted first. This is also because the "latest" log file for a day is the most updated one
                    File.Delete(logPath2);
                    innerentry.WriteToFile(logPath2);
                }
            }
        }
    }
}
