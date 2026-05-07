using Microsoft.WindowsAPICodePack.Dialogs;
using psu_archive_explorer.FileViewers;
using psu_archive_explorer.Forms;
using psu_archive_explorer.Forms.FileViewers;
using psu_archive_explorer.Forms.FileViewers.Enemies;
using psu_archive_explorer;
using PSULib;
using PSULib.FileClasses.Archives;
using PSULib.FileClasses.Bosses;
using PSULib.FileClasses.Characters;
using PSULib.FileClasses.Enemies;
using PSULib.FileClasses.General;
using PSULib.FileClasses.General.Scripts;
using PSULib.FileClasses.Items;
using PSULib.FileClasses.Maps;
using PSULib.FileClasses.Missions;
using PSULib.FileClasses.Models;
using PSULib.FileClasses.Textures;
using PSULib.Support;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace psu_archive_explorer
{
    public partial class MainForm : Form
    {
        private class TextureEntry
        {
            public RawFile fileContents;
            public List<string> containingFiles = new List<string>();
        }

        private void textureCatalogueToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                Dictionary<string, Dictionary<string, TextureEntry>> textureEntries = new Dictionary<string, Dictionary<string, TextureEntry>>();
                using (MD5 md5 = MD5.Create())
                {
                    foreach (string file in Directory.EnumerateFiles(folderBrowserDialog1.SelectedPath))
                    {
                        using (Stream s = new FileStream(file, FileMode.Open))
                        {
                            byte[] identifier = new byte[4];
                            s.Read(identifier, 0, 4);
                            s.Seek(0, SeekOrigin.Begin);
                            if (identifier.SequenceEqual(new byte[] { 0x4E, 0x4D, 0x4C, 0x4C }))
                            {
                                NblLoader nbl = new NblLoader(s);
                                if (nbl.chunks.Count > 1)
                                {
                                    //This means there's a TMLL...
                                    foreach (RawFile raw in nbl.chunks[1].fileContents)
                                    {
                                        byte[] fileMd5 = md5.ComputeHash(raw.fileContents);
                                        string md5String = BitConverter.ToString(fileMd5).Replace("-", "");
                                        if (!textureEntries.ContainsKey(raw.filename))
                                        {
                                            textureEntries[raw.filename] = new Dictionary<string, TextureEntry>();
                                        }
                                        if (!textureEntries[raw.filename].ContainsKey(md5String))
                                        {
                                            TextureEntry entry = new TextureEntry();
                                            entry.fileContents = raw;
                                            textureEntries[raw.filename][md5String] = entry;
                                        }
                                        if (!textureEntries[raw.filename][md5String].containingFiles.Contains(Path.GetFileName(file)))
                                        {
                                            textureEntries[raw.filename][md5String].containingFiles.Add(Path.GetFileName(file));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                foreach (var ent in textureEntries)
                {
                    if (ent.Value.Values.Count > 1)
                    {
                        Console.Out.WriteLine("Texture: " + ent.Key);
                        foreach (var val in ent.Value)
                        {
                            Directory.CreateDirectory(folderBrowserDialog1.SelectedPath + "\\categorized\\conflicted\\" + ent.Key + "\\" + val.Key);
                            using (Stream outStream = new FileStream(folderBrowserDialog1.SelectedPath + "\\categorized\\conflicted\\" + ent.Key + "\\" + val.Key + "\\" + val.Value.fileContents.filename, FileMode.Create))
                            {
                                val.Value.fileContents.WriteToStream(outStream);
                            }
                            XvrTextureFile xvr = new XvrTextureFile(val.Value.fileContents.subHeader, val.Value.fileContents.fileContents, val.Value.fileContents.filename);
                            xvr.mipMaps[0].Save(folderBrowserDialog1.SelectedPath + "\\categorized\\conflicted\\" + ent.Key + "\\" + val.Key + "\\" + val.Value.fileContents.filename.Replace(".xvr", ".png"));
                            Console.Out.WriteLine("\t" + val.Key + ": " + string.Join(", ", val.Value.containingFiles));
                        }
                        Console.Out.WriteLine();
                    }
                    else
                    {

                        string hash = ent.Value.Keys.First();
                        RawFile raw = ent.Value[hash].fileContents;
                        Directory.CreateDirectory(folderBrowserDialog1.SelectedPath + "\\categorized\\" + ent.Key);
                        using (Stream outStream = new FileStream(folderBrowserDialog1.SelectedPath + "\\categorized\\" + ent.Key + "\\" + raw.filename, FileMode.Create))
                        {
                            raw.WriteToStream(outStream);
                        }
                        XvrTextureFile xvr = new XvrTextureFile(raw.subHeader, raw.fileContents, raw.filename);
                        xvr.mipMaps[0].Save(folderBrowserDialog1.SelectedPath + "\\categorized\\" + ent.Key + "\\" + raw.filename.Replace(".xvr", ".png"));
                    }
                }
            }
        }

        private void listAllObjparamsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CommonOpenFileDialog goodOpenFileDialog = new CommonOpenFileDialog();
            goodOpenFileDialog.IsFolderPicker = true;

            if (goodOpenFileDialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                string[] fileNames = Directory.GetFiles(goodOpenFileDialog.FileName);
                actionProgressBar.Maximum = fileNames.Length;
                progressStatusLabel.Text = $"Progress: {actionProgressBar.Value}/{actionProgressBar.Maximum} Files. Please wait, this can take time.";
                progressStatusLabel.Refresh();
                Dictionary<int, Tuple<string, ObjectParamFile.ObjectEntry>> objects = new Dictionary<int, Tuple<string, ObjectParamFile.ObjectEntry>>();

                foreach (string fileName in fileNames)
                {
                    Console.WriteLine(fileName);
                    string newFolder = Path.GetDirectoryName(fileName);
                    byte[] formatName = new byte[4];
                    try
                    {
                        using (Stream stream = File.Open(fileName, FileMode.Open))
                        {
                            stream.Read(formatName, 0, 4);

                            string identifier = Encoding.ASCII.GetString(formatName, 0, 4);
                            if (identifier == "NMLL")
                            {
                                NblLoader nbl = new NblLoader(stream);
                                if (((NblChunk)nbl.getFileParsed(0)).doesFileExist("obj_param.xnr"))
                                {
                                    ObjectParamFile paramFile = (ObjectParamFile)((NblChunk)nbl.getFileParsed(0)).getFileParsed("obj_param.xnr");
                                    foreach (int objectId in paramFile.ObjectDefinitions.Keys)
                                    {
                                        if (objects.ContainsKey(objectId) && !objects[objectId].Item2.group2Entry.Equals(paramFile.ObjectDefinitions[objectId].group2Entry))
                                        {
                                            Console.WriteLine("Mismatched object, ID = " + objectId + " compared to " + objects[objectId].Item1);
                                        }
                                        else
                                        {
                                            objects[objectId] = new Tuple<string, ObjectParamFile.ObjectEntry>(fileName, paramFile.ObjectDefinitions[objectId]);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine("Error reading file");
                    }

                    actionProgressBar.Value++;
                    progressStatusLabel.Text = $"Progress: {actionProgressBar.Value}/{actionProgressBar.Maximum} Files. Please wait, this can take time.";
                    progressStatusLabel.Refresh();
                }

                foreach (int i in objects.Keys.OrderBy(a => a))
                {
                    var hitbox = objects[i].Item2.group2Entry;
                    Console.WriteLine("Object " + i + ", first found in " + objects[i].Item1 + ": group 0 = " + hitbox.hitboxShape + "; {" + hitbox.unknownFloat2 + ", " + hitbox.unknownFloat3 + ", " + hitbox.unknownFloat3 + "}; id 1 = " + hitbox.unknownInt5 + "; isolated float = " + hitbox.unknownFloat6 + "; last value = " + hitbox.unknownInt9);
                }
            }
        }

        private void listAllMonsterLayoutsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CommonOpenFileDialog goodOpenFileDialog = new CommonOpenFileDialog();
            goodOpenFileDialog.IsFolderPicker = true;

            if (goodOpenFileDialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                string outputFileName = Path.Combine(goodOpenFileDialog.FileName, "report2.txt");
                StreamWriter writer = new StreamWriter(outputFileName);
                string[] fileNames = Directory.GetFiles(goodOpenFileDialog.FileName);
                actionProgressBar.Maximum = fileNames.Length;
                progressStatusLabel.Text = $"Progress: {actionProgressBar.Value}/{actionProgressBar.Maximum} Files. Please wait, this can take time.";
                progressStatusLabel.Refresh();
                Dictionary<int, Tuple<string, ObjectParamFile.ObjectEntry>> objects = new Dictionary<int, Tuple<string, ObjectParamFile.ObjectEntry>>();

                foreach (string fileName in fileNames)
                {
                    string newFolder = Path.GetDirectoryName(fileName);
                    byte[] formatName = new byte[4];
                    try
                    {
                        using (Stream stream = File.Open(fileName, FileMode.Open))
                        {
                            stream.Read(formatName, 0, 4);

                            string identifier = Encoding.ASCII.GetString(formatName, 0, 3);
                            if (identifier == "AFS")
                            {
                                writer.WriteLine(fileName);
                                AfsLoader afs = new AfsLoader(stream);
                                foreach (var file in afs.afsList)
                                {
                                    if (file.fileName.StartsWith("zone") && file.fileName.EndsWith("_ae.nbl"))
                                    {
                                        NblLoader nbl = (NblLoader)file.fileContents;
                                        foreach (var nblFile in ((ContainerFile)nbl.getFileParsed(0)).getFilenames())
                                        {
                                            if (nblFile.StartsWith("enemy") && nblFile.EndsWith(".xnr"))
                                            {
                                                EnemyLayoutFile layoutFile = (EnemyLayoutFile)((ContainerFile)nbl.getFileParsed(0)).getFileParsed(nblFile);
                                                writer.WriteLine("\t" + nblFile + ":");
                                                for (int i = 0; i < layoutFile.spawns.Length; i++)
                                                {
                                                    writer.WriteLine($"\t\tSpawn {i}:");
                                                    writer.WriteLine($"\t\tMonsters:");
                                                    for (int j = 0; j < layoutFile.spawns[i].monsters.Length; j++)
                                                    {
                                                        writer.WriteLine($"\t\t\tGroup {j}:");
                                                        for (int k = 0; k < layoutFile.spawns[i].monsters[j].Length; k++)
                                                        {
                                                            writer.WriteLine("\t\t\t\t" + layoutFile.spawns[i].monsters[j][k].ToString());
                                                        }
                                                    }
                                                    writer.WriteLine($"\t\tArrangements:");
                                                    for (int j = 0; j < layoutFile.spawns[i].arrangements.Length; j++)
                                                    {
                                                        writer.WriteLine("\t\t\t" + layoutFile.spawns[i].arrangements[j].ToString());
                                                    }
                                                    writer.WriteLine($"\t\tSpawn Data:");
                                                    for (int j = 0; j < layoutFile.spawns[i].spawnData.Length; j++)
                                                    {
                                                        writer.WriteLine("\t\t\t" + layoutFile.spawns[i].spawnData[j].ToString());
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine("Error reading file");
                    }

                    actionProgressBar.Value++;
                    progressStatusLabel.Text = $"Progress: {actionProgressBar.Value}/{actionProgressBar.Maximum} Files. Please wait, this can take time.";
                    progressStatusLabel.Refresh();
                }
                /*
                foreach (int i in objects.Keys.OrderBy(a => a))
                {
                    var hitbox = objects[i].Item2.group2Entry;
                    Console.WriteLine("Object " + i + ", first found in " + objects[i].Item1 + ": group 0 = " + hitbox.hitboxShape + "; {" + hitbox.unknownFloat2 + ", " + hitbox.unknownFloat3 + ", " + hitbox.unknownFloat3 + "}; id 1 = " + hitbox.unknownInt5 + "; isolated float = " + hitbox.unknownFloat6 + "; last value = " + hitbox.unknownInt9);
                }*/
            }
        }

        //TODO: This should be in a different program.
        private string convertDamageResists(int rawResists)
        {
            StringBuilder sb = new StringBuilder(3);

            switch (rawResists & 0x3)
            {
                default: break;
                case 1: sb.Append("s"); break;
                case 2: case 3: sb.Append("S"); break;
            }
            switch (rawResists & 0xC)
            {
                default: break;
                case 4: sb.Append("r"); break;
                case 8: case 0xC: sb.Append("R"); ; break;
            }
            switch (rawResists & 0x30)
            {
                default: break;
                case 4: sb.Append("t"); break;
                case 8: case 0xC: sb.Append("T"); break;
            }
            return sb.ToString();
        }

        private void catalogueEnemyparamToolStripMenuItem_Click(object sender, EventArgs e)
        {

            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {

                Dictionary<string, EnemyParamFile> paramFileMap = new Dictionary<string, EnemyParamFile>();
                Dictionary<string, ActDataFile> actDataFileMap = new Dictionary<string, ActDataFile>();
                Dictionary<string, DamageDataFile> damageDataFileMap = new Dictionary<string, DamageDataFile>();
                foreach (string file in Directory.EnumerateFiles(folderBrowserDialog1.SelectedPath))
                {
                    using (Stream s = new FileStream(file, FileMode.Open))
                    {
                        byte[] identifier = new byte[4];
                        s.Read(identifier, 0, 4);
                        s.Seek(0, SeekOrigin.Begin);
                        if (identifier.SequenceEqual(new byte[] { 0x4E, 0x4D, 0x4C, 0x4C }))
                        {
                            NblLoader nbl = new NblLoader(s);
                            if (nbl.chunks.Count > 0)
                            {
                                foreach (RawFile raw in nbl.chunks[0].fileContents)
                                {
                                    if (raw.filename.StartsWith("Param") && !raw.filename.Contains("ColtobaShare"))
                                    {
                                        paramFileMap[raw.filename] = (EnemyParamFile)nbl.chunks[0].getFileParsed(raw.filename);
                                    }
                                    else if (raw.filename.StartsWith("ActData") && !raw.filename.Contains("Quadruped_a"))
                                    {
                                        actDataFileMap[raw.filename] = (ActDataFile)nbl.chunks[0].getFileParsed(raw.filename);
                                    }
                                    else if (raw.filename.StartsWith("DamageData") && !raw.filename.Contains("Quadruped_a"))
                                    {
                                        damageDataFileMap[raw.filename] = (DamageDataFile)nbl.chunks[0].getFileParsed(raw.filename);
                                    }
                                }
                            }
                        }
                    }
                }

                /*
                foreach (var entry in paramFileMap.OrderBy(x => x.Key))
                {
                    EnemyParamFile file = entry.Value;
                    Console.Out.WriteLine(entry.Key);
                    
                    Console.Out.WriteLine("Base Stats:");
                    Console.Out.WriteLine("\tHpModifier: " + file.baseParams.HpModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tAtpModifier: " + file.baseParams.AtpModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tDfpModifier: " + file.baseParams.DfpModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tAtaModifier: " + file.baseParams.AtaModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tEvpModifier: " + file.baseParams.EvpModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tStaModifier: " + file.baseParams.StaModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tLckModifier: " + file.baseParams.LckModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tTpModifier: " + file.baseParams.TpModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tMstModifier: " + file.baseParams.MstModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tElementModifier: " + file.baseParams.ElementModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tExpModifier: " + file.baseParams.ExpModifier.ToString("0.00##"));
                    Console.Out.WriteLine("\tUnknownValue1: " + file.baseParams.UnknownValue1);
                    Console.Out.WriteLine("\tUnknownValue2: " + file.baseParams.UnknownValue2);
                    Console.Out.WriteLine("\tUnknownValue3: " + file.baseParams.UnknownValue3);
                    Console.Out.WriteLine("\tStatusResists: " + file.baseParams.StatusResists.ToString("X"));
                    Console.Out.WriteLine("\tDamageResists: " + convertDamageResists(file.baseParams.DamageResists));
                    Console.Out.WriteLine("\tUnknownModifier3: " + file.baseParams.UnknownModifier3.ToString("0.00##"));
                    Console.Out.WriteLine("\tUnknownModifier4: " + file.baseParams.UnknownModifier4.ToString("0.00##"));
                    Console.Out.WriteLine("\tUnknownValue4: " + file.baseParams.UnknownValue4);
                    Console.Out.WriteLine("\tUnknownValue5: " + file.baseParams.UnknownValue5);
                    Console.Out.WriteLine("\tUnknownModifier5: " + file.baseParams.UnknownModifier5.ToString("0.00##"));
                    Console.Out.WriteLine("\tUnknownModifier6: " + file.baseParams.UnknownModifier6.ToString("0.00##"));
                    Console.Out.WriteLine("\tUnknownModifier7: " + file.baseParams.UnknownModifier7.ToString("0.00##"));
                    string element = "UNKNOWN";
                    switch(file.baseParams.MonsterElement)
                    {
                        case 0: element = "Neutral"; break;
                        case 1: element = "Fire"; break;
                        case 2:
                            element = "Lightning"; break;
                        case 4:
                            element = "Light"; break;
                        case 9:
                            element = "Ice"; break;
                        case 10:
                            element = "Ground"; break;
                        case 12:
                            element = "Dark"; break;
                        default: break;
                    }
                    Console.Out.WriteLine("\tMonsterElement: " + element);
                    Console.Out.WriteLine();
                    Console.Out.WriteLine("Buffs:");
                    Console.Out.WriteLine("\t?\t??\t???\t????\t?????\tATP\tDFP\tATA\tEVP\tSTA\tLCK\tTP\tMST\tEXP\tSERes\tDmgRes");
                    //Console.Out.WriteLine("\t?\t??\t???\t????\t?????\tATP\tDFP\tATA\tEVP\tSTA\tLCK\tTP\tMST\tEXP\tUnused\tUnused\tUnused\tUnused\tSERes\tDmgRes");
                    foreach (var buff in file.buffParams)
                    {
                        Console.Out.Write("\t"); 
                        Console.Out.Write(buff.UnknownValue1 + "\t");
                        Console.Out.Write(buff.UnknownValue2 + "\t");
                        Console.Out.Write(buff.UnknownValue3 + "\t");
                        Console.Out.Write(buff.UnknownValue4 + "\t");
                        Console.Out.Write(buff.UnusedIntValue1 + "\t");
                        Console.Out.Write(buff.AtpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(buff.DfpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(buff.AtaModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(buff.EvpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(buff.StaModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(buff.LckModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(buff.TpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(buff.MstModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(buff.ExpModifier.ToString("0.00##") + "\t");
                        
                        //Console.Out.Write(buff.UnusedIntValue2 + "\t");
                        //Console.Out.Write(buff.UnusedModifier1 + "\t");
                        //Console.Out.Write(buff.UnusedModifier2 + "\t");
                        //Console.Out.Write(buff.UnusedModifier3 + "\t");
                        Console.Out.Write(buff.StatusResists.ToString("X") + "\t");
                        Console.Out.Write(convertDamageResists(buff.DamageResists));
                        //Console.Out.Write(buff.DamageResists.ToString("X"));
                        Console.Out.WriteLine();
                    }
                    Console.Out.WriteLine();
                    Console.Out.WriteLine("Attacks:");
                    //Console.Out.WriteLine("\tBone          \t?\t??\t???\t????\t?????\tOnhit\tSE(s)\tLevel\t??\t???\tHP\tATP\tDFP\tATA\tEVP\tSTA\tLCK\tTP\tMST\tELE%\tEXP\tUnused\tUnused\tUnused");
                    Console.Out.WriteLine("\tBone          \tX\tY\tZ\tWidth\tHeight\tOnhit\tSE(s)\tLevel\t??\t???\tHP\tATP\tDFP\tATA\tEVP\tSTA\tLCK\tTP\tMST\tELE%\tEXP");
                    foreach (var attack in file.attackParams)
                    {
                        Console.Out.Write("\t");
                        Console.Out.Write(attack.BoneName.PadRight(14) + "\t");
                        Console.Out.Write(attack.OffsetX.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.OffsetY.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.OffsetZ.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.BoundCylinderWidth.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.BoundCylinderHeight.ToString("0.00##") + "\t");

                        Console.Out.Write(attack.OnHitEffect.ToString("X4") + "\t");
                        Console.Out.Write(attack.StatusEffect.ToString("X4") + "\t");
                        Console.Out.Write(attack.UnknownSubgroup2Int3 + "\t");
                        Console.Out.Write(attack.UnknownSubgroup2Int4 + "\t");
                        Console.Out.Write(attack.UnknownSubgroup2Int5.ToString("X4") + "\t");

                        Console.Out.Write(attack.HpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.AtpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.DfpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.AtaModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.EvpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.StaModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.LckModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.TpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.MstModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.ElementModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(attack.ExpModifier.ToString("0.00##"));
                        
                        //Console.Out.Write(attack.ExpModifier + "\t");
                        //Console.Out.Write(attack.UnusedModifier1 + "\t");
                        //Console.Out.Write(attack.UnusedModifier2 + "\t");
                        //Console.Out.Write(attack.UnusedModifier3);
                        Console.Out.WriteLine();
                    }
                    Console.Out.WriteLine();
                    Console.Out.WriteLine("Hitboxes:");
                    Console.Out.WriteLine("\tCanHit\tBone          \tX\tY\tZ\tWidth\tHeight\tHP\tATP\tDFP\tATA\tEVP\tSTA\tLCK\tTP\tMST\tELE%\tEXP\tUnused\tUnused\tUnused");
                    foreach (var hitbox in file.hitboxParams)
                    {
                        Console.Out.Write("\t");
                        Console.Out.Write(hitbox.Targetable + "\t");
                        Console.Out.Write((hitbox.BoneName != null ? hitbox.BoneName : "").PadRight(14) + "\t");
                        Console.Out.Write(hitbox.OffsetX.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.OffsetY.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.OffsetZ.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.BoundCylinderWidth.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.BoundCylinderHeight.ToString("0.00##") + "\t");

                        Console.Out.Write(hitbox.HpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.AtpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.DfpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.AtaModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.EvpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.StaModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.LckModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.TpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.MstModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.ElementModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.ExpModifier.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.UnusedModifier1.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.UnusedModifier2.ToString("0.00##") + "\t");
                        Console.Out.Write(hitbox.UnusedModifier3.ToString("0.00##"));
                        Console.Out.WriteLine();
                    }
                    
                    Console.Out.WriteLine("\tGroup 2:");
                    foreach (var subentry1 in file.unknownSubEntry1List)
                    {
                        Console.Out.Write("\t");
                        Console.Out.Write("\t" + subentry1.UnknownInt1);
                        Console.Out.Write("\t" + subentry1.OffsetX.ToString("0.00##"));
                        Console.Out.Write("\t" + subentry1.OffsetY.ToString("0.00##"));
                        Console.Out.Write("\t" + subentry1.OffsetZ.ToString("0.00##"));
                        Console.Out.Write("\t" + subentry1.Scale1.ToString("0.00##"));
                        Console.Out.WriteLine("\t" + subentry1.Scale2.ToString("0.00##"));
                    }
                    Console.Out.WriteLine();
                    Console.Out.WriteLine("\tGroup 2:");
                    foreach (var subentry2 in file.unknownSubEntry2List)
                    {
                        Console.Out.Write("\t");
                        Console.Out.Write("\t" + subentry2.UnknownInt1);
                        Console.Out.Write("\t" + subentry2.UnknownInt2);
                        Console.Out.Write("\t" + subentry2.UnknownFloat1.ToString("0.00##"));
                        Console.Out.Write("\t" + subentry2.UnknownInt3);
                        Console.Out.Write("\t" + subentry2.UnknownInt4);
                        Console.Out.Write("\t" + subentry2.UnknownInt5);
                        Console.Out.Write("\t" + subentry2.UnknownInt6);
                        Console.Out.Write("\t" + subentry2.UnknownInt7);
                        Console.Out.WriteLine("\t" + subentry2.UnknownInt8);
                    }
                    Console.Out.WriteLine();
                    Console.Out.WriteLine();
                }
                */
                /*
                foreach(var entry in actDataFileMap)
                {
                    ActDataFile actDataFile = entry.Value;
                    Console.Out.WriteLine(entry.Key);
                    Console.Out.WriteLine("whatever");
                    for(int i = 0; i < actDataFile.Actions.Count; i++)
                    {
                        Console.Out.WriteLine("Action " + i);
                        
                        foreach (var action in actDataFile.Actions[i].ActionEntries)
                        {
                            Console.Out.Write("\t" + action.UnknownInt1);
                            Console.Out.Write("\t" + action.MotTblID);
                            Console.Out.Write("\t" + action.UnknownFloatAt3);
                            Console.Out.Write("\t" + action.VerticalExaggeration);
                            Console.Out.Write("\t" + action.MotionFloat1);
                            Console.Out.Write("\t" + action.MotionFloat2);
                            Console.Out.Write("\t" + action.HorizontalUnknown);
                            Console.Out.Write("\t" + action.UnknownFloatAt8);
                            Console.Out.Write("\t" + action.UnknownFloatAt9);
                            Console.Out.Write("\t" + action.UnknownAngleDegrees1);
                            Console.Out.Write("\t" + action.UnknownIntAt11);
                            Console.Out.Write("\t" + action.UnknownAngleDegrees2);
                            Console.Out.Write("\t" + action.UnknownAngleDegrees3);
                            Console.Out.Write("\t" + action.UnknownStateValue);
                            Console.Out.Write("\t" + action.UnknownStateModifier1);
                            Console.Out.Write("\t" + action.UnknownStateModifier2);
                            Console.Out.Write("\t" + action.AttackID);
                            Console.Out.Write("\t" + action.UnknownInt15);
                            Console.Out.Write("\t" + action.UnknownFloat6);
                            Console.Out.Write("\t" + action.UnknownInt16);
                            Console.Out.Write("\t" + action.UnknownFloat7);
                            Console.Out.Write("\t" + action.DamageDataList);
                            Console.Out.Write("\t" + action.UnknownInt18);
                            Console.Out.Write("\t" + action.UnknownInt19);
                            Console.Out.Write("\t" + action.UnknownInt20);
                            Console.Out.Write("\t" + action.UnknownFloatAt21);
                            Console.Out.Write("\t" + action.UnusedInt22);
                            Console.Out.Write("\t" + action.UnusedInt23);
                            Console.Out.Write("\t" + action.UnusedInt24);
                            Console.Out.Write("\t" + action.UnusedInt25);
                            Console.Out.WriteLine();
                        }
                        */
                /*
                for(int j = 0; j < actDataFile.Actions[i].ActionEntries.Count; j++)
                {
                    if (actDataFile.Actions[i].ActionEntries[j].SubEntryList1.Count > 0 || actDataFile.Actions[i].ActionEntries[j].SubEntryList2.Count > 0)
                    {
                        Console.Out.WriteLine("\tSubaction " + j);
                        if (actDataFile.Actions[i].ActionEntries[j].SubEntryList1.Count > 0)
                        {
                            Console.Out.WriteLine("\tSublist 1:");
                            for (int k = 0; k < actDataFile.Actions[i].ActionEntries[j].SubEntryList1.Count; k++)
                            {
                                Console.Out.Write("\t\t" + actDataFile.Actions[i].ActionEntries[j].SubEntryList1[k].UnknownInt1);
                                Console.Out.Write("\t\t" + actDataFile.Actions[i].ActionEntries[j].SubEntryList1[k].UnknownFloat.ToString("0.00##"));
                                Console.Out.Write("\t\t" + actDataFile.Actions[i].ActionEntries[j].SubEntryList1[k].UnknownInt2);
                                Console.Out.WriteLine();
                            }
                        }
                        if (actDataFile.Actions[i].ActionEntries[j].SubEntryList2.Count > 0)
                        {
                            Console.Out.WriteLine("\tSublist 2:");
                            for (int k = 0; k < actDataFile.Actions[i].ActionEntries[j].SubEntryList2.Count; k++)
                            {
                                Console.Out.Write("\t\t" + actDataFile.Actions[i].ActionEntries[j].SubEntryList2[k].UnknownInt1);
                                Console.Out.Write("\t\t" + actDataFile.Actions[i].ActionEntries[j].SubEntryList2[k].UnknownFloat.ToString("0.00##"));
                                Console.Out.Write("\t\t" + actDataFile.Actions[i].ActionEntries[j].SubEntryList2[k].UnknownInt2);
                                Console.Out.WriteLine();
                            }
                        }
                        Console.Out.WriteLine();
                    }
                }*/
                /*
                Console.Out.WriteLine();
            }
            Console.Out.WriteLine();
        }
        */
                foreach (var entry in damageDataFileMap)
                {
                    DamageDataFile damageDataFile = entry.Value;
                    Console.Out.WriteLine(entry.Key);
                    for (int i = 0; i < damageDataFile.DamageTypeEntries.Count; i++)
                    {
                        Console.Out.WriteLine("Damage lookup " + i);
                        for (int j = 0; j < damageDataFile.DamageTypeEntries[i].Count; j++)
                        {
                            Console.Out.WriteLine("\tDamage index " + j + ", Type: " + damageDataFile.DamageTypeEntries[i][j].DamageType + ", Angle count: " + damageDataFile.DamageTypeEntries[i][j].Angles.Count);
                            foreach (var angleEntry in damageDataFile.DamageTypeEntries[i][j].Angles)
                            {
                                Console.Out.Write("\t\t" + angleEntry.UnknownInt1);
                                Console.Out.Write("\t" + angleEntry.UnknownInt2);
                                Console.Out.Write("\tActions: " + string.Join(", ", angleEntry.ActionList));
                                Console.Out.WriteLine();
                            }
                            Console.Out.WriteLine();
                        }
                        Console.Out.WriteLine();
                    }
                    Console.Out.WriteLine();
                }
            }
        }
    }
}