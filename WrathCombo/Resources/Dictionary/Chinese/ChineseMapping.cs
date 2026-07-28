using System.Collections.Generic;
using System.Linq;
using System.Text;
using ECommons.Logging;
using System;
using WrathCombo.Resources.Dictionary.Chinese.Skill;
using WrathCombo.Resources.Dictionary.Chinese.Description;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace WrathCombo.Resources.Dictionary.Chinese
{
    // 统一替换文字的扩展方法
    public static class TextReplacer
    {
        public static string ReplaceWithChinese(this string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

#if DEBUG
            DictionaryDebugger.StartReplacementSession(text);
#endif

            var lines = text.Split(['\n'], StringSplitOptions.None);
            bool anyReplacement = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string originalLine = lines[i];
                string replacedLine = originalLine;

                foreach (var pair in ReplacementsDictionary.Replacements)
                {
                    if (replacedLine.Contains(pair.Key))
                    {
                        string beforeReplace = replacedLine;
                        replacedLine = ReplaceWholeWords(replacedLine, pair.Key, pair.Value);

                        if (beforeReplace != replacedLine)
                        {
#if DEBUG
                            DictionaryDebugger.RecordUsedKeyInSession(pair.Key, text);
#endif
                            anyReplacement = true;
                        }
                    }
                }

                lines[i] = Regex.Replace(replacedLine, @"\s*([\u4e00-\u9fa5])\s*", "$1");

#if DEBUG
                if (originalLine == replacedLine && !string.IsNullOrWhiteSpace(originalLine) &&
                    !Regex.IsMatch(originalLine, @"^\s*$"))
                {
                    DictionaryDebugger.RecordUnreplacedText(originalLine);
                }
#endif
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        ///     取代 <paramref name="key"/> 為 <paramref name="value"/>，但只在「沒有和其他英文字母黏在一起」的位置動手。
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     原本這裡是 <c>string.Replace</c>，完全沒有詞界概念，所以短鍵會咬進更長的英文單字裡：
        ///     'Heal' 咬進 "Health"、'Mana' 咬進 "Manafont"、'and' 咬進 "Standard"、
        ///     'Ruin II' 咬進 "Ruin III"（顯示成錯誤的技能）。
        ///     </para>
        ///     <para>
        ///     這裡刻意只把「ASCII 英文字母」當作詞界字元，不用 <c>\b</c>：
        ///     取代後的內容是中文，而中文字對 <c>\b</c> 而言也是文字字元，
        ///     用真正的詞界會導致「緊鄰已翻譯中文」的英文字反而不給翻。
        ///     </para>
        /// </remarks>
        private static string ReplaceWholeWords(string source, string key, string value)
        {
            int index = source.IndexOf(key, StringComparison.Ordinal);
            if (index < 0)
                return source;

            // 鍵本身若不是以字母開頭/結尾（例如 " to "、"On "），該側就不需要詞界檢查。
            bool guardStart = char.IsAsciiLetter(key[0]);
            bool guardEnd = char.IsAsciiLetter(key[^1]);

            StringBuilder builder = new(source.Length);
            int copiedUpTo = 0;

            while (index >= 0)
            {
                int end = index + key.Length;
                bool gluedToAWord =
                    (guardStart && index > 0 && char.IsAsciiLetter(source[index - 1])) ||
                    (guardEnd && end < source.Length && char.IsAsciiLetter(source[end]));

                builder.Append(source, copiedUpTo, index - copiedUpTo);
                builder.Append(gluedToAWord ? key : value);

                copiedUpTo = end;
                index = source.IndexOf(key, end, StringComparison.Ordinal);
            }

            builder.Append(source, copiedUpTo, source.Length - copiedUpTo);
            return builder.ToString();
        }
    }

    public static class ReplacementsDictionary
    {
        private static readonly Dictionary<string, string> _replacements = new();
        private static readonly StringBuilder _duplicateKeysLog = new();

        static ReplacementsDictionary()
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                InitializeDictionary();
                stopwatch.Stop();

                PluginLog.Information($"中文替换字典初始化完成，共加载 {_replacements.Count} 个键值对，耗时 {stopwatch.ElapsedMilliseconds} 毫秒");

                if (_duplicateKeysLog.Length > 0)
                {
                    PluginLog.Warning($"初始化过程中检测到重复键值对，{_duplicateKeysLog}");
                }
            }
            catch (System.Exception ex)
            {
                PluginLog.Error($"初始化中文替换字典时发生错误: {ex.Message}\n{ex.StackTrace}");
                _replacements.Clear();
            }

            #if DEBUG
            DictionaryDebugger.Initialize();
            #endif
        }

        private static void InitializeDictionary()
        {
            // 收集所有键值对
            var allKeyValuePairs = new List<KeyValuePair<string, string>>();

            allKeyValuePairs.AddRange(Genesis.GetDescriptions());
            allKeyValuePairs.AddRange(ASTDesc.GetDescriptions());
            allKeyValuePairs.AddRange(PCTDesc.GetDescriptions());
            allKeyValuePairs.AddRange(BLMDesc.GetDescriptions());
            allKeyValuePairs.AddRange(BLUDesc.GetDescriptions());
            allKeyValuePairs.AddRange(BRDDesc.GetDescriptions());
            allKeyValuePairs.AddRange(DNCDesc.GetDescriptions());
            allKeyValuePairs.AddRange(DRGDesc.GetDescriptions());
            allKeyValuePairs.AddRange(DRKDesc.GetDescriptions());
            allKeyValuePairs.AddRange(GNBDesc.GetDescriptions());
            allKeyValuePairs.AddRange(MCHDesc.GetDescriptions());
            allKeyValuePairs.AddRange(MNKDesc.GetDescriptions());
            allKeyValuePairs.AddRange(NINDesc.GetDescriptions());
            allKeyValuePairs.AddRange(PLDDesc.GetDescriptions());
            allKeyValuePairs.AddRange(RDMDesc.GetDescriptions());
            allKeyValuePairs.AddRange(RPRDesc.GetDescriptions());
            allKeyValuePairs.AddRange(SAMDesc.GetDescriptions());
            allKeyValuePairs.AddRange(SCHDesc.GetDescriptions());
            allKeyValuePairs.AddRange(SGEDesc.GetDescriptions());
            allKeyValuePairs.AddRange(SMNDesc.GetDescriptions());
            allKeyValuePairs.AddRange(VPRDesc.GetDescriptions());
            allKeyValuePairs.AddRange(WARDesc.GetDescriptions());
            allKeyValuePairs.AddRange(WHMDesc.GetDescriptions());
            allKeyValuePairs.AddRange(DOLDesc.GetDescriptions());
            allKeyValuePairs.AddRange(DOHDesc.GetDescriptions());
            allKeyValuePairs.AddRange(BozjaSkills.GetSkills());
            allKeyValuePairs.AddRange(CommonSkills.GetSkills());
            allKeyValuePairs.AddRange(ASTSkills.GetSkills());
            allKeyValuePairs.AddRange(BLMSkills.GetSkills());
            allKeyValuePairs.AddRange(BLUSkills.GetSkills());
            allKeyValuePairs.AddRange(BRDSkills.GetSkills());
            allKeyValuePairs.AddRange(DNCSkills.GetSkills());
            allKeyValuePairs.AddRange(DRGSkills.GetSkills());
            allKeyValuePairs.AddRange(DRKSkills.GetSkills());
            allKeyValuePairs.AddRange(GNBSkills.GetSkills());
            allKeyValuePairs.AddRange(MCHSkills.GetSkills());
            allKeyValuePairs.AddRange(MNKSkills.GetSkills());
            allKeyValuePairs.AddRange(NINSkills.GetSkills());
            allKeyValuePairs.AddRange(PCTSkills.GetSkills());
            allKeyValuePairs.AddRange(PLDSkills.GetSkills());
            allKeyValuePairs.AddRange(RDMSkills.GetSkills());
            allKeyValuePairs.AddRange(RPRSkills.GetSkills());
            allKeyValuePairs.AddRange(SAMSkills.GetSkills());
            allKeyValuePairs.AddRange(SCHSkills.GetSkills());
            allKeyValuePairs.AddRange(SGESkills.GetSkills());
            allKeyValuePairs.AddRange(SMNSkills.GetSkills());
            allKeyValuePairs.AddRange(VPRSkills.GetSkills());
            allKeyValuePairs.AddRange(WARSkills.GetSkills());
            allKeyValuePairs.AddRange(WHMSkills.GetSkills());
            allKeyValuePairs.AddRange(VariantSkills.GetSkills());
            allKeyValuePairs.AddRange(DOLSkills.GetSkills());
            allKeyValuePairs.AddRange(Apocalypse.GetDescriptions());

            // 检测并记录重复键
            var duplicates = allKeyValuePairs
                .GroupBy(kvp => kvp.Key)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicates.Count != 0)
            {
                _duplicateKeysLog.AppendLine($"共检测到 {duplicates.Count} 个重复键:");

                foreach (var group in duplicates)
                {
                    _duplicateKeysLog.AppendLine($"键: \"{group.Key}\"，出现 {group.Count()} 次，值:");
                    foreach (var kvp in group)
                    {
                        _duplicateKeysLog.AppendLine($"  - \"{kvp.Value}\"");
                    }
                }
            }

            // 过滤掉重复键并填充最终字典
            // 只保留每个键的第一个出现
            var uniqueKeyValues = allKeyValuePairs
                .GroupBy(kvp => kvp.Key)
                .Select(g => g.First())
                .Where(kvp => !string.IsNullOrEmpty(kvp.Key));

            foreach (var kvp in uniqueKeyValues)
            {
                _replacements.Add(kvp.Key, kvp.Value);
            }
        }

        public static IReadOnlyDictionary<string, string> Replacements => _replacements;

        public static string GetDuplicateKeysLog()
        {
            return _duplicateKeysLog.ToString();
        }
    }
}