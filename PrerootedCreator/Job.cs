﻿using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;

namespace PRFCreator
{
    class Job
    {
        private static int JobNum = 0;
        private static void SetJobNum(int num)
        {
            if (Form1.form.jobnum_label.InvokeRequired)
                Form1.form.jobnum_label.Invoke(new MethodInvoker(delegate { Form1.form.jobnum_label.Text = num + "/" + GetJobCount(); }));
            else
                Form1.form.jobnum_label.Text = num + "/" + GetJobCount();
        }

        private static int GetJobCount()
        {
            int count = jobs.Length - 1; //don't count 'Complete'
            if (Form1.form.include_checklist.CheckedItems.Count < 1) //if there are no extra files
                count--;
            if (!Form1.form.options_checklist.CheckedItems.Contains("Sign zip"))
                count--;
            if (!File.Exists(Form1.form.rec_textbox.Text)) //if recovery is not included
                count--;
            if (Form1.form.extra_dataGridView.Rows.Count < 1) //no additional zip files
                count--;

            return count;
        }

        private static Action<BackgroundWorker>[] legacyjobs = { UnpackSystem, UnpackSystemEXT4, EditScript, AddExtras, AddSuperSU, AddRecovery, AddExtraFlashable, AddSystemEXT4, SignZip, Complete };
        private static Action<BackgroundWorker>[] newjobs = { UnpackSystem, EditScript, AddExtras, AddSuperSU, AddRecovery, AddExtraFlashable, AddSystem, SignZip, Complete };
        private static Action<BackgroundWorker>[] jobs = newjobs;
        public static void Worker()
        {
            JobNum = 0;
            int free = Utility.freeSpaceMB(Utility.GetTempPath());
            if (free < 4096)
            {
                Logger.WriteLog("Error: Not enough disk space. Please make sure that you have atleast 4GB free space on drive " + Path.GetPathRoot(Utility.GetTempPath())
                    + ". Currently you only have " + free + "MB available");
                return;
            }
            if (!Zipping.ExistsInZip(Form1.form.ftf_textbox.Text, "system.sin"))
            {
                Logger.WriteLog("Error: system.sin does not exist in file " + Form1.form.ftf_textbox.Text);
                return;
            }
            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.WorkerSupportsCancellation = true;
            worker.DoWork += (o, _e) =>
                {
                    try
                    {
                        Form1.form.ControlsEnabled(false);
                        if (Form1.form.options_checklist.CheckedItems.Contains("Legacy mode"))
                            jobs = legacyjobs;
                        else
                            jobs = newjobs;
                        foreach (Action<BackgroundWorker> action in jobs)
                        {
                            if (worker.CancellationPending)
                            {
                                Cancel(worker);
                                _e.Cancel = true;
                                break;
                            }
                            action(worker);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.WriteLog(e.Message);
                        //Logger.WriteLog(e.StackTrace);
                    }
                };
            worker.ProgressChanged += (o, _e) =>
                {
                    Form1.form.progressBar.Value = _e.ProgressPercentage;
                };
            worker.RunWorkerCompleted += (o, _e) =>
                {
                    Form1.form.ControlsEnabled(true);
                };
            worker.RunWorkerAsync();
        }

        private static void UnpackSystem(BackgroundWorker worker)
        {
            SetJobNum(++JobNum);
            Logger.WriteLog("Extracting system.sin from " + System.IO.Path.GetFileName(Form1.form.ftf_textbox.Text));
            if (!Zipping.UnzipFile(worker, Form1.form.ftf_textbox.Text, "system.sin", string.Empty, Utility.GetTempPath()))
            {
                worker.CancelAsync();
                return;
            }

            byte[] UUID = PartitionInfo.ReadSinUUID(Path.Combine(Utility.GetTempPath(), "system.sin"));
            //PartitionInfo.ScriptMode = (UUID != null) ? PartitionInfo.Mode.LegacyUUID : PartitionInfo.Mode.Legacy;
            if (!Form1.form.options_checklist.CheckedItems.Contains("Legacy mode"))
                PartitionInfo.ScriptMode = PartitionInfo.Mode.Sinflash;
            else
                PartitionInfo.ScriptMode = (UUID != null) ? PartitionInfo.Mode.LegacyUUID : PartitionInfo.Mode.Legacy;
            Utility.ScriptSetUUID(worker, "system", UUID);
        }

        private static void EditScript(BackgroundWorker worker)
        {
            SetJobNum(++JobNum);
            Logger.WriteLog("Adding info to flashable script");
            string fw = Utility.PadStr(Path.GetFileNameWithoutExtension(Form1.form.ftf_textbox.Text), " ", 41);
            Utility.EditScript(worker, "INSERT FIRMWARE HERE", fw);
        }

        private static void UnpackSystemEXT4(BackgroundWorker worker)
        {
            SetJobNum(++JobNum);
            SinExtract.ExtractSin(worker, Path.Combine(Utility.GetTempPath(), "system.sin"), Path.Combine(Utility.GetTempPath(), "system.ext4"));
            File.Delete(Path.Combine(Utility.GetTempPath(), "system.sin"));
        }

        private static void AddSystemEXT4(BackgroundWorker worker)
        {
            SetJobNum(++JobNum);
            Logger.WriteLog("Adding system to zip");
            Zipping.AddToZip(worker, Settings.destinationFile, Path.Combine(Utility.GetTempPath(), "system.ext4"), "system.ext4");
            File.Delete(Path.Combine(Utility.GetTempPath(), "system.ext4"));
        }

        private static void AddSystem(BackgroundWorker worker)
        {
            SetJobNum(++JobNum);
            Logger.WriteLog("Adding system to zip");
            Zipping.AddToZip(worker, Settings.destinationFile, Path.Combine(Utility.GetTempPath(), "system.sin"), "system.sin", true);
            File.Delete(Path.Combine(Utility.GetTempPath(), "system.sin"));
        }

        private static void AddExtras(BackgroundWorker worker)
        {
            if (Form1.form.include_checklist.CheckedItems.Count < 1)
                return;

            Logger.WriteLog("Adding extra files");
            SetJobNum(++JobNum);
            foreach (string item in Form1.form.include_checklist.CheckedItems)
                ExtraFiles.AddExtraFiles(worker, item.ToLower(), Form1.form.ftf_textbox.Text);
        }

        private static void AddExtraFlashable(BackgroundWorker worker)
        {
            if(Form1.form.extra_dataGridView.Rows.Count < 1)
                return;

            SetJobNum(++JobNum);
            for (int i = 0; i < Form1.form.extra_dataGridView.Rows.Count; i++)
            {
                string type = Form1.form.extra_dataGridView["GridViewType", i].Value.ToString();
                string name = Form1.form.extra_dataGridView["GridViewName", i].Value.ToString();
                if (!File.Exists(name))
                {
                    Logger.WriteLog("Error adding Extra File '" + name + "': File does not exist");
                    continue;
                }

                if (type == "Flashable zip")
                    ExtraFiles.AddExtraFlashable(worker, name);
                else
                    ExtraFiles.AddAPKFile(worker, name, type);
            }
        }

        private static void AddSuperSU(BackgroundWorker worker)
        {
            SetJobNum(++JobNum);
            Logger.WriteLog("Adding " + Path.GetFileName(Form1.form.su_textbox.Text));
            string superSUFile = Form1.form.su_textbox.Text;
            Zipping.AddToZip(worker, Settings.destinationFile, superSUFile, "SuperSU.zip", false);
        }

        private static void AddRecovery(BackgroundWorker worker)
        {
            if (!File.Exists(Form1.form.rec_textbox.Text))
                return;

            SetJobNum(++JobNum);
            string recoveryFile = Form1.form.rec_textbox.Text;
            Logger.WriteLog("Adding " + Path.GetFileName(recoveryFile));
            Zipping.AddToZip(worker, Settings.destinationFile, recoveryFile, "dualrecovery.zip");
        }

        //~ doubles the process time
        private static void SignZip(BackgroundWorker worker)
        {
            if (!Form1.form.options_checklist.CheckedItems.Contains("Sign zip"))
                return;

            SetJobNum(++JobNum);
            if (!Utility.JavaInstalled())
            {
                Logger.WriteLog("Error: Could not execute Java. Is it installed?");
                return;
            }

            string signapkAbs = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "signapk.jar");
            if (!File.Exists(signapkAbs))
            {
                Logger.WriteLog("Error: signapk.jar file not found");
                return;
            }

            Utility.WriteResourceToFile("PRFCreator.Resources.testkey.pk8", "testkey.pk8");
            Utility.WriteResourceToFile("PRFCreator.Resources.testkey.x509.pem", "testkey.x509.pem");

            string newdest = Path.GetFileNameWithoutExtension(Settings.destinationFile) + "-signed.zip";
            Logger.WriteLog("Signing zip file");
            if (Utility.RunProcess("java", "-Xmx1024m -jar \"" + signapkAbs + "\" -w testkey.x509.pem testkey.pk8 " + Settings.destinationFile + " " + newdest) == 0)
            {
                File.Delete(Settings.destinationFile);
                Settings.destinationFile = newdest;
            }
            else
                Logger.WriteLog("Error: Could not sign zip");

            File.Delete("testkey.pk8");
            File.Delete("testkey.x509.pem");
        }

        private static void Complete(BackgroundWorker worker)
        {
            FileInfo fi = new FileInfo(Settings.destinationFile);
            if (fi.Length > int.MaxValue)
            {
                Logger.WriteLog("Warning: Flashable zip size is bigger than 2GB! It may be possible that flashing fails. Please make sure you are using TWRP 3.0 or higher.");
                MessageBox.Show("Warning: Flashable zip size is bigger than 2GB! It may be possible that flashing fails. Please make sure you are using TWRP 3.0 or higher.", "PRFCreator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            Logger.WriteLog("Finished\n");
        }

        private static void Cancel(BackgroundWorker worker)
        {
            Logger.WriteLog("Cancelled\n");
        }
    }
}
