using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ECommons.Logging;

namespace WrathCombo.Resources.Dictionary.Chinese
{
    public static class SkillTranslationTableGenerator
    {
        private static readonly Dictionary<string, string> JobNameMapping = new()
        {
            { "AST", "占星術師" },
            { "BLM", "黑魔道士" },
            { "BRD", "吟遊詩人" },
            { "DNC", "舞者" },
            { "DRG", "龍騎士" },
            { "DRK", "暗黑騎士" },
            { "GNB", "絕槍戰士" },
            { "MCH", "機工士" },
            { "MNK", "武僧" },
            { "NIN", "忍者" },
            { "PAL", "騎士" },
            { "PLD", "騎士" },
            { "RDM", "赤魔道士" },
            { "RPR", "奪魂者" },
            { "SAM", "武士" },
            { "SCH", "學者" },
            { "SGE", "賢者" },
            { "SMN", "召喚士" },
            { "VPR", "毒蛇劍士" },
            { "WAR", "戰士" },
            { "WHM", "白魔道士" },
            { "BLU", "青魔道士" },
            { "DOL", "採集職業" },
            { "DOH", "製作職業" },
            { "PCT", "繪靈法師" },
            { "Common", "通用技能" },
            { "Bozja", "博茲雅技能" },
            { "Variant", "變體技能" }
        };
        public static void GenerateSkillTranslationTable()
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string filePath = Path.Combine(desktopPath, "WrathCombo技能對照表.md");

                using (StreamWriter writer = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    writer.WriteLine("# WrathCombo 技能名稱中英文對照表");
                    writer.WriteLine();
                    writer.WriteLine($"生成時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine();
                    writer.WriteLine("## 說明");
                    writer.WriteLine();
                    writer.WriteLine("此對照表包含遊戲中所有職業的技能名稱中英文對照。");
                    writer.WriteLine("技能名稱通過遊戲客戶端獲取，確保準確性。");
                    writer.WriteLine();
                    writer.WriteLine("---");
                    writer.WriteLine();

                    // 獲取所有技能文件
                    var skillFiles = GetSkillFiles();
                    
                    foreach (var skillFile in skillFiles.OrderBy(f => f.Key))
                    {
                        var jobName = skillFile.Key;
                        var skills = skillFile.Value;
                        
                        if (skills.Any())
                        {
                            writer.WriteLine($"## {GetJobDisplayName(jobName)} ({jobName})");
                            writer.WriteLine();
                            
                            writer.WriteLine("| 英文名稱 | 中文名稱 |");
                            writer.WriteLine("|---------|---------|");
                            
                            foreach (var skill in skills.OrderBy(s => s.Key))
                            {
                                writer.WriteLine($"| {skill.Key} | {skill.Value} |");
                            }
                            
                            writer.WriteLine();
                            writer.WriteLine("---");
                            writer.WriteLine();
                        }
                    }
                }

                PluginLog.Information($"技能對照表已生成到桌面: WrathCombo技能對照表.md");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"生成技能對照表時發生錯誤: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static Dictionary<string, IEnumerable<KeyValuePair<string, string>>> GetSkillFiles()
        {
            var skillFiles = new Dictionary<string, IEnumerable<KeyValuePair<string, string>>>();
            var skillNamespace = "WrathCombo.Resources.Dictionary.Chinese.Description";
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var skillTypes = assembly.GetTypes()
                .Where(t => t.Namespace == skillNamespace && t.Name.EndsWith("Skills"))
                .ToList();

            foreach (var skillType in skillTypes)
            {
                try
                {
                    var jobName = skillType.Name.Replace("Skills", "");
                    var getSkillsMethod = skillType.GetMethod("GetSkills");
                    if (getSkillsMethod != null)
                    {
                        // 靜態方法調用，無需實例化
                        var skills = (IEnumerable<KeyValuePair<string, string>>)getSkillsMethod.Invoke(null, null);
                        skillFiles[jobName] = skills;
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Warning($"獲取技能文件 {skillType.Name} 時發生錯誤: {ex.Message}");
                }
            }
            return skillFiles;
        }

        private static string GetJobDisplayName(string jobName)
        {
            return JobNameMapping.TryGetValue(jobName, out var displayName) ? displayName : jobName;
        }
    }
} 