using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Resources.Dictionary.Chinese;

namespace WrathCombo.Attributes;

/// <summary> Attribute documenting additional information for each combo. </summary>
[AttributeUsage(AttributeTargets.Field)]
internal class CustomComboInfoAttribute : Attribute
{
    /// <summary> Initializes a new instance of the <see cref="CustomComboInfoAttribute"/> class. </summary>
    /// <param name="name"> Display name. </param>
    /// <param name="description"> Combo description. </param>
    /// <param name="jobID"> Associated job ID. </param>
    /// <param name="order"> Display order. </param>
    //// <param name="memeName"> Display meme name </param>
    //// <param name="memeDescription"> Meme description. </param>
    internal CustomComboInfoAttribute(string name, string description, byte jobID, [CallerLineNumber] int order = 0)
    {
        InternalName = name;
        Name = Translate(TranslatedNames, name, arrowTo: true);
        Description = Translate(TranslatedDescriptions, description, arrowTo: false);
        JobID = jobID;
        Order = order;
    }

    // 每次讀 preset 的任何屬性都會重跑這個建構子；名稱與說明是編譯期常數、翻譯結果固定，
    // 按原文快取，免得每次都對整份替換字典掃一遍。鍵的集合以 preset 數為上限；任何執行緒都可能建構，所以用並行字典。
    private static readonly ConcurrentDictionary<string, string> TranslatedNames = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> TranslatedDescriptions = new(StringComparer.Ordinal);

    private static string Translate(ConcurrentDictionary<string, string> cache, string text, bool arrowTo) =>
        text is null ? text.ReplaceWithChinese(arrowTo) : cache.GetOrAdd(text, static (t, a) => t.ReplaceWithChinese(a), arrowTo);

    /// <summary> Gets the untranslated original name, used by internal classification/IPC logic that matches on English substrings (e.g. "single target", "- AoE"). Not for display. </summary>
    public string InternalName { get; }

    /// <summary> Gets the display name. </summary>
    public string Name { get; }

    /// <summary> Gets the description. </summary>
    public string Description { get; }

    /// <summary> Gets the job ID. </summary>
    public uint JobID { get; }

    /// <summary> Gets the display order. </summary>
    public int Order { get; }

    /// <summary> Gets the job role. </summary>
    public int Role => CustomComboFunctions.JobIDs.JobIDToRole(JobID);

    /// <summary> Gets the job category. </summary>
    public uint ClassJobCategory => CustomComboFunctions.JobIDs.JobIDToClassJobCategory(JobID);

    /// <summary> Gets the job name. </summary>
    public string JobName => CustomComboFunctions.JobIDs.JobIDToName(JobID);

    /// <summary> Gets the job shorthand. </summary>
    public string JobShorthand => CustomComboFunctions.JobIDs.JobIDToShorthand(JobID);
}