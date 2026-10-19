using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ECommons.Logging;

namespace WrathCombo.Resources.Dictionary.Chinese
{
    public static class DictionaryDebugger
    {
        private static readonly HashSet<string> _unreplacedTexts = new();
        private static readonly HashSet<string> _unusedKeys = new();
        private static readonly HashSet<string> _usedKeys = new();
        private static readonly Dictionary<string, HashSet<string>> _sessionUsedKeys = new();

        public static void Initialize()
        {
            foreach (var pair in ReplacementsDictionary.Replacements)
            {
                _unusedKeys.Add(pair.Key);
            }

            PluginLog.Information($"鍵值對調試器初始化完成，加載了 {_unusedKeys.Count} 個鍵值對");
        }

        public static void RecordUsedKey(string key)
        {
            if (_unusedKeys.Contains(key))
            {
                _unusedKeys.Remove(key);
                _usedKeys.Add(key);
            }
        }

        public static void StartReplacementSession(string originalText)
        {
            if (!_sessionUsedKeys.ContainsKey(originalText))
            {
                _sessionUsedKeys[originalText] = new HashSet<string>();
            }
        }

        // 避免同一文本多次替換時重複計數鍵值對使用
        public static void RecordUsedKeyInSession(string key, string originalText)
        {
            if (_sessionUsedKeys.ContainsKey(originalText) && !_sessionUsedKeys[originalText].Contains(key))
            {
                _sessionUsedKeys[originalText].Add(key);
                
                if (_unusedKeys.Contains(key))
                {
                    _unusedKeys.Remove(key);
                    _usedKeys.Add(key);
                }
            }
        }

        public static void RecordUnreplacedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            string cleanText = text.Trim();
            
            if (!System.Text.RegularExpressions.Regex.IsMatch(cleanText, @"[a-zA-Z]"))
                return;

            if (ShouldSkipUnreplacedText(cleanText))
                return;

            _unreplacedTexts.Add(cleanText);
        }

        private static bool ShouldSkipUnreplacedText(string text)
        {
            if (text.Length < 3)
                return true;

            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^[\s\d\W]*[a-zA-Z]{1,2}[\s\d\W]*$"))
                return true;

            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^[A-Z_][A-Z0-9_]*$"))
                return true;

            if (text.Contains("/") || text.Contains("\\") || text.Contains("://") || 
                text.Contains("()") || text.Contains("{}") || text.Contains("[]"))
                return true;

            // 跳過主要是中文但包含少量英文字母的文本（如"- 等級90或以上"）
            var chineseCharCount = System.Text.RegularExpressions.Regex.Matches(text, @"[\u4e00-\u9fa5]").Count;
            var englishCharCount = System.Text.RegularExpressions.Regex.Matches(text, @"[a-zA-Z]").Count;
            
            if (chineseCharCount > englishCharCount && chineseCharCount > 2)
                return true;

            return false;
        }

        public static void ExportDebugFile()
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string filePath = Path.Combine(desktopPath, "WrathDictionaryDebug.txt");

                using (StreamWriter writer = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    writer.WriteLine("WrathCombo中文翻譯鍵值對調試文件");
                    writer.WriteLine($"生成時間: {DateTime.Now}");
                    writer.WriteLine($"詞典總數: {ReplacementsDictionary.Replacements.Count}");
                    writer.WriteLine($"已使用鍵值對: {_usedKeys.Count}");
                    writer.WriteLine($"未使用鍵值對: {_unusedKeys.Count}");
                    writer.WriteLine($"未替換文本總數: {_unreplacedTexts.Count}");
                    writer.WriteLine($"替換會話總數: {_sessionUsedKeys.Count}");
                    writer.WriteLine();

                    writer.WriteLine("=== 未被替換的英文文本 ===");
                    foreach (var text in _unreplacedTexts.OrderBy(t => t))
                    {
                        writer.WriteLine($"\"{text}\"");
                    }
                    writer.WriteLine();

                    writer.WriteLine("=== 未被使用的鍵值對 ===");
                    
                    var dynamicSkillKeys = new List<string>();
                    var normalUnusedKeys = new List<string>();
                    
                    foreach (var key in _unusedKeys.OrderBy(k => k))
                    {
                        string value = ReplacementsDictionary.Replacements[key];
                        // 檢測動態技能鍵值對：值為中文但鍵為英文技能名
                        bool isDynamicSkill = System.Text.RegularExpressions.Regex.IsMatch(value, @"[\u4e00-\u9fa5]") && 
                                            System.Text.RegularExpressions.Regex.IsMatch(key, @"^[A-Z][a-zA-Z\s]*$") &&
                                            !value.Contains(" ");
                        
                        if (isDynamicSkill)
                        {
                            dynamicSkillKeys.Add(key);
                        }
                        else
                        {
                            normalUnusedKeys.Add(key);
                        }
                    }

                    writer.WriteLine($"普通未使用鍵值對: {normalUnusedKeys.Count}個");
                    writer.WriteLine($"疑似動態技能鍵值對: {dynamicSkillKeys.Count}個");
                    writer.WriteLine();

                    if (normalUnusedKeys.Any())
                    {
                        writer.WriteLine("--- 普通未使用鍵值對 ---");
                        foreach (var key in normalUnusedKeys)
                        {
                            writer.WriteLine($"\"{key}\" => \"{ReplacementsDictionary.Replacements[key]}\"");
                        }
                        writer.WriteLine();
                    }

                    if (dynamicSkillKeys.Any())
                    {
                        writer.WriteLine("--- 疑似動態技能鍵值對（可能因為遊戲中已顯示為中文而未被使用） ---");
                        foreach (var key in dynamicSkillKeys)
                        {
                            writer.WriteLine($"\"{key}\" => \"{ReplacementsDictionary.Replacements[key]}\"");
                        }
                        writer.WriteLine();
                        writer.WriteLine("注意：上述鍵值對使用了ActionName()方法獲取技能的本地化名稱。");
                        writer.WriteLine("如果遊戲設置為中文，這些鍵值對可能永遠不會被使用，");
                        writer.WriteLine("因為界面中直接顯示的就是中文技能名。");
                        writer.WriteLine();
                    }

                    writer.WriteLine();
                    writer.WriteLine("=== 重複鍵日誌 ===");
                    writer.Write(ReplacementsDictionary.GetDuplicateKeysLog());
                }

                PluginLog.Information($"鍵值對調試文件已生成到桌面: WrathDictionaryDebug.txt");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"生成鍵值對調試文件時發生錯誤: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}