﻿using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;

namespace PRFCreator
{
    static class ExtraFiles
    {
        //key is displayable name
        //value is a Dictionary
        //key2 is destination file name in flashable zip
        //value is array of possible (regex) name in ftf
        private static Dictionary<string, Dictionary<string, string[]>> extrafilesDic = null;
        private static void InitDic()
        {
            if (extrafilesDic != null)
                return;

            extrafilesDic = new Dictionary<string, Dictionary<string, string[]>>();
            //kernel
            Dictionary<string, string[]> kdic = new Dictionary<string, string[]>();
            kdic.Add("boot", new string[] { "kernel", "boot" });
            kdic.Add("rpm", new string[] { "rpm" });
            extrafilesDic.Add("Kernel", kdic);
            //fotakernel
            Dictionary<string, string[]> fotadic = new Dictionary<string, string[]>();
            fotadic.Add("fotakernel", new string[] { "fotakernel" });
            extrafilesDic.Add("FOTAKernel", fotadic);
            //modem
            Dictionary<string, string[]> mdic = new Dictionary<string, string[]>();
            mdic.Add("amss_fsg", new string[] { "amss_fsg", "amss_fs_3", "modem" });
            mdic.Add("amss_fs_1", new string[] { "amss_fs_1" });
            mdic.Add("amss_fs_2", new string[] { "amss_fs_2" });
            extrafilesDic.Add("Modem", mdic);
            //elabel
            Dictionary<string, string[]> edic = new Dictionary<string, string[]>();
            edic.Add("ltalabel", new string[] { "elabel.*" });
            extrafilesDic.Add("LTALabel", edic);

            //sinflash doesn't support partition-image yet
            //partition-image
            //Dictionary<string, string[]> pdic = new Dictionary<string, string[]>();
            //pdic.Add("partition-image", new string[] { "partition", "partition-image" });
            //extrafilesDic.Add("Partition Image", pdic);
        }

        public static void FetchSinFromFTF(string ftf)
        {
            InitDic();
            Utility.InvokeIfNecessary(Form1.form.include_checklist, () => { Form1.form.include_checklist.Items.Clear(); });

            string[] filelist = Zipping.ListZipContent(ftf);
            foreach (string key in extrafilesDic.Keys)
            {
                foreach (string key2 in extrafilesDic[key].Keys)
                    foreach (string name in extrafilesDic[key][key2])
                        foreach (string f in filelist)
                            if (Regex.Match(f, name + "\\.sin").Success)
                            {
                                //Logger.WriteLog("Found sinfile: " + key);
                                Utility.InvokeIfNecessary(Form1.form.include_checklist, () =>
                                {
                                    Form1.form.include_checklist.Items.Add(key);
                                });
                                goto OuterLoop;
                            }
            OuterLoop:
                ;
            }
        }

        private static void AddSinToConfig(BackgroundWorker worker, string sinfile)
        {
            string val;
            if ((val = Utility.ReadConfig(worker, "sinfiles")) == null)
                Utility.EditConfig(worker, "sinfiles", sinfile);
            else
                Utility.EditConfig(worker, "sinfiles", val + "," + sinfile);
        }

        public static void AddExtraFiles(BackgroundWorker worker, string name, string ftffile)
        {
            switch (name)
            {
                case "kernel":
                    AddKernel(worker, ftffile);
                    break;
                case "fotakernel":
                    AddFOTAKernel(worker, ftffile);
                    break;
                case "modem":
                    AddModem(worker, ftffile);
                    break;
                case "ltalabel":
                    AddLTALabel(worker, ftffile);
                    break;
            }
        }

        public static void AddAPKFile(BackgroundWorker worker, string filename, string type)
        {
            Logger.WriteLog("Adding APK: " + Path.GetFileName(filename));
            if (!Zipping.UnzipFile(worker, filename, "AndroidManifest.xml", string.Empty, Utility.GetTempPath(), false))
            {
                Logger.WriteLog("Error adding APK: AndroidManifest.xml not found");
                return;
            }
            string appname = Utility.ManifestGetName(File.ReadAllBytes(Path.Combine(Utility.GetTempPath(), "AndroidManifest.xml")));
            File.Delete(Path.Combine(Utility.GetTempPath(), "AndroidManifest.xml"));
            if (appname == null)
            {
                Logger.WriteLog("Error adding APK: Could not read appname from AndroidManifest.xml");
                return;
            }

            if (type == "App (System)")
                Zipping.AddToZip(worker, Settings.destinationFile, filename, "system/app/" + appname + Path.GetExtension(filename), false);
            else
                Zipping.AddToZip(worker, Settings.destinationFile, filename, "data/app/" + appname + Path.GetExtension(filename), false);
        }

        public static void AddExtraFlashable(BackgroundWorker worker, string filename)
        {
            Logger.WriteLog("Adding flashable zip: " + Path.GetFileName(filename));
            string fixedname = Path.GetFileName(filename).Replace(' ', '_');

            string cmd = "\n# " + fixedname + "\n" +
                "if\n" +
                "\tpackage_extract_file(\"" + fixedname + "\", \"/tmp/" + fixedname + "\") == \"t\"\n" +
                "then\n" +
                "\trun_program(\"/tmp/busybox\", \"mkdir\", \"/tmp/" + Path.GetFileNameWithoutExtension(fixedname) + "_extracted" + "\");\n" +
                "\trun_program(\"/tmp/busybox\", \"unzip\", \"-d\", \"/tmp/" + Path.GetFileNameWithoutExtension(fixedname) + "_extracted" + "\", \"/tmp/" + fixedname + "\");\n" +
                "\tset_perm(0, 0, 0755, \"/tmp/" + Path.GetFileNameWithoutExtension(fixedname) + "_extracted" + "/META-INF/com/google/android/update-binary\");\n" +
                "\trun_program(\"/tmp/" + Path.GetFileNameWithoutExtension(fixedname) + "_extracted" + "/META-INF/com/google/android/update-binary\", file_getprop(\"/tmp/prfargs\", \"version\"), file_getprop(\"/tmp/prfargs\", \"outfile\"), \"/tmp/" + fixedname + "\");\n" +
                "\tdelete_recursive(\"/tmp/" + Path.GetFileNameWithoutExtension(fixedname) + "_extracted" + "\");\n" +
                "\tdelete(\"/tmp/" + fixedname + "\");\n" +
                "endif;\n" +
                "#InsertExtra\n";
            Utility.EditScript(worker, "#InsertExtra", cmd);
            Zipping.AddToZip(worker, Settings.destinationFile, filename, fixedname, false);
        }

        private static string GetKernelFilename(string ftffile)
        {
            string[] names = { "kernel", "boot" };
            foreach (string name in names)
            {
                if (Zipping.ExistsInZip(ftffile, name + ".sin"))
                    return name;
            }

            //if nothing exists, return kernel anyway so the error message makes sense
            return "kernel";
        }

        private static void AddKernel(BackgroundWorker worker, string ftffile)
        {
            if (PartitionInfo.ScriptMode == PartitionInfo.Mode.Sinflash)
            {
                if (ExtractAndAddSin(worker, GetKernelFilename(ftffile), ftffile, "boot"))
                    AddSinToConfig(worker, "boot");
                if (ExtractAndAddSin(worker, "rpm", ftffile))
                    AddSinToConfig(worker, "rpm");
            }
            else
            {
                ExtractAndAdd(worker, GetKernelFilename(ftffile), ".elf", ftffile, "boot");
                ExtractAndAdd(worker, "rpm", ".elf", ftffile);
            }
        }

        private static void AddFOTAKernel(BackgroundWorker worker, string ftffile)
        {
            if (PartitionInfo.ScriptMode == PartitionInfo.Mode.Sinflash)
            {
                if (ExtractAndAddSin(worker, "fotakernel", ftffile))
                    AddSinToConfig(worker, "fotakernel");
            }
            else
                ExtractAndAdd(worker, "fotakernel", ".elf", ftffile);
        }

        private static void AddLTALabel(BackgroundWorker worker, string ftffile)
        {
            string ltalname = Zipping.ZipGetFullname(ftffile, "elabel*.sin");
            if (string.IsNullOrEmpty(ltalname))
            {
                Logger.WriteLog("   Error: Could not find LTALabel in FTF");
                return;
            }

            if (PartitionInfo.ScriptMode == PartitionInfo.Mode.Sinflash)
            {
                if (ExtractAndAddSin(worker, Path.GetFileNameWithoutExtension(ltalname), ftffile, "ltalabel"))
                    AddSinToConfig(worker, "ltalabel");
            }
            else
                ExtractAndAdd(worker, Path.GetFileNameWithoutExtension(ltalname), ".ext4", ftffile, "ltalabel");
        }

        private static string GetModemFilename(string ftffile)
        {
            string[] mdms = { "amss_fsg", "amss_fs_3", "modem" };
            foreach (string mdm in mdms)
            {
                if (Zipping.ExistsInZip(ftffile, mdm + ".sin"))
                    return mdm;
            }

            return "modem";
        }

        private static void AddModem(BackgroundWorker worker, string ftffile)
        {
            if (PartitionInfo.ScriptMode == PartitionInfo.Mode.Sinflash)
            {
                if (ExtractAndAddSin(worker, GetModemFilename(ftffile), ftffile, "amss_fsg"))
                    AddSinToConfig(worker, "amss_fsg");
                if (ExtractAndAddSin(worker, "amss_fs_1", ftffile))
                    AddSinToConfig(worker, "amss_fs_1");
                if (ExtractAndAddSin(worker, "amss_fs_2", ftffile))
                    AddSinToConfig(worker, "amss_fs_2");
            }
            else
            {
                ExtractAndAdd(worker, GetModemFilename(ftffile), string.Empty, ftffile, "amss_fsg");
                ExtractAndAdd(worker, "amss_fs_1", string.Empty, ftffile);
                ExtractAndAdd(worker, "amss_fs_2", string.Empty, ftffile);
            }
        }

        private static void ExtractAndAdd(BackgroundWorker worker, string name, string extension, string ftffile, string AsFilename = "")
        {
            if (Zipping.ExistsInZip(ftffile, name + ".sin") == false)
            {
                OnError(name, AsFilename);
                return;
            }

            Zipping.UnzipFile(worker, ftffile, name + ".sin", string.Empty, Utility.GetTempPath(), false);
            if (File.Exists(Path.Combine(Utility.GetTempPath(), name + ".sin")))
            {
                Logger.WriteLog("   " + name);
                SinExtract.ExtractSin(worker, Path.Combine(Utility.GetTempPath(), name + ".sin"), Path.Combine(Utility.GetTempPath(), name + extension), false);

                if (PartitionInfo.ScriptMode == PartitionInfo.Mode.LegacyUUID)
                {
                    byte[] UUID = PartitionInfo.ReadSinUUID(Path.Combine(Utility.GetTempPath(), name + ".sin"));
                    Utility.ScriptSetUUID(worker, (AsFilename == "" ? name : AsFilename), UUID);
                }

                File.Delete(Path.Combine(Utility.GetTempPath(), name + ".sin"));
                Zipping.AddToZip(worker, Settings.destinationFile, Path.Combine(Utility.GetTempPath(), name + extension), (AsFilename == "" ? name : AsFilename) + extension, false);
                File.Delete(Path.Combine(Utility.GetTempPath(), name + extension));
            }
        }

        private static bool ExtractAndAddSin(BackgroundWorker worker, string name, string ftffile, string AsFilename = "")
        {
            if (Zipping.ExistsInZip(ftffile, name + ".sin") == false)
            {
                OnError(name, AsFilename);
                return false;
            }

            Zipping.UnzipFile(worker, ftffile, name + ".sin", string.Empty, Utility.GetTempPath(), false);
            if (File.Exists(Path.Combine(Utility.GetTempPath(), name + ".sin")))
            {
                Logger.WriteLog("   " + name);
                Zipping.AddToZip(worker, Settings.destinationFile, Path.Combine(Utility.GetTempPath(), name + ".sin"), (AsFilename == "" ? name : AsFilename) + ".sin", false, Ionic.Zlib.CompressionLevel.None);
                File.Delete(Path.Combine(Utility.GetTempPath(), name + ".sin"));
            }

            return true;
        }

        private static void OnError(string name, string AsFilename = "")
        {
            switch (name)
            {
                case "rpm":
                    //rpm seems to be missing in older firmwares, so we don't display it as error
                    //in newer firmwares, rpm is moved to boot files
                    //Logger.WriteLog("   Info: Could not find " + ((AsFilename == "") ? name : AsFilename));
                    break;
                default:
                    Logger.WriteLog("   Error: Could not find " + ((AsFilename == "") ? name : AsFilename));
                    break;
            }
        }
    }
}
