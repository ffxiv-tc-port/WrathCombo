using FFXIVClientStructs;
using System;
using System.Collections.Generic;
using System.Linq;
using WrathCombo.Core;
using WrathCombo.Services;

namespace WrathCombo.CustomComboNS.Functions
{
    internal abstract class UserData(string v)
    {
        public string pName = v;

        public static implicit operator string(UserData o) => (o.pName);

        public static Dictionary<string, UserData> MasterList = new();

        public abstract void ResetToDefault();
    }

    internal class UserFloat : UserData
    {
        // Constructor with only the string parameter
        public UserFloat(string v) : this(v, 0.0f) { }

        public float Default;

        // Constructor with both string and float parameters
        public UserFloat(string v, float defaults) : base(v) // Overload constructor to preload data
        {
            if (!PluginConfiguration.CustomFloatValues.ContainsKey(this.pName)) // if it isn't there, set
            {
                PluginConfiguration.SetCustomFloatValue(this.pName, defaults);
                Service.Configuration.Save();
            }

            Default = defaults;
            MasterList.Add(this.pName, this);
        }

        // Implicit conversion to float
        public static implicit operator float(UserFloat o) => PluginConfiguration.GetCustomFloatValue(o.pName);

        public override void ResetToDefault()
        {
            PluginConfiguration.SetCustomFloatValue(this.pName, Default);
            Service.Configuration.Save();
        }
    }

    internal class UserInt : UserData
    {
        // Constructor with only the string parameter
        public UserInt(string v) : this(v, 0) { } // Chaining to the other constructor with a default value

        public int Default;
        // Constructor with both string and int parameters
        public UserInt(string v, int defaults) : base(v) // Overload constructor to preload data
        {
            if (!PluginConfiguration.CustomIntValues.ContainsKey(this.pName)) // if it isn't there, set
            {
                PluginConfiguration.SetCustomIntValue(this.pName, defaults);
                Service.Configuration.Save();
            }

            Default = defaults;
            MasterList.Add(this.pName, this);
        }

        // Implicit conversion to int
        public static implicit operator int(UserInt o) => PluginConfiguration.GetCustomIntValue(o.pName);

        public int Value { get { return this; } set { PluginConfiguration.SetCustomIntValue(this.pName, value); Service.Configuration.Save(); } }

        public override void ResetToDefault()
        {
            PluginConfiguration.SetCustomIntValue(this.pName, Default);
            Service.Configuration.Save();
        }
    }

    internal class UserBool : UserData
    {
        // Constructor with only the string parameter
        public UserBool(string v) : this(v, false) { }

        public bool Default;

        // Constructor with both string and bool parameters
        public UserBool(string v, bool defaults) : base(v) // Overload constructor to preload data
        {
            if (!PluginConfiguration.CustomBoolValues.ContainsKey(this.pName)) // if it isn't there, set
            {
                PluginConfiguration.SetCustomBoolValue(this.pName, defaults);
                Service.Configuration.Save();
            }

            Default = defaults;
            MasterList.Add(this.pName, this);
        }

        // Implicit conversion to bool
        public static implicit operator bool(UserBool o) => PluginConfiguration.GetCustomBoolValue(o.pName);

        public override void ResetToDefault()
        {
            PluginConfiguration.SetCustomBoolValue(this.pName, Default);
            Service.Configuration.Save();
        }
    }

    internal class UserIntArray : UserData
    {
        public string Name => pName;

        public int[] Default;
        public int Count => PluginConfiguration.GetCustomIntArrayValue(this.pName).Length;
        public bool Any(Func<int, bool> func) => PluginConfiguration.GetCustomIntArrayValue(this.pName).Any(func);
        public int[] Items => PluginConfiguration.GetCustomIntArrayValue(this.pName);
        public int IndexOf(int item)
        {
            for (int i = 0; i < Count; i++)
            {
                if (Items[i] == item)
                    return i;
            }
            return -1;
        }

        public void Clear(int maxValues)
        {
            var array = PluginConfiguration.GetCustomIntArrayValue(this.pName);
            Array.Resize(ref array, maxValues);
            PluginConfiguration.SetCustomIntArrayValue(this.pName, array);
            Service.Configuration.Save();
        }

        public UserIntArray(string v, int[] defaults) : base(v)
        {
            if (!PluginConfiguration.CustomIntArrayValues.ContainsKey(this.pName))
            {
                PluginConfiguration.SetCustomIntArrayValue(this.pName, defaults);
                Service.Configuration.Save();
            }

            Default = defaults;
            MasterList.Add(this.pName, this);
        }

        public UserIntArray(string v) : base(v)
        {
            if (!PluginConfiguration.CustomIntArrayValues.ContainsKey(this.pName))
            {
                PluginConfiguration.SetCustomIntArrayValue(this.pName, []);
                Service.Configuration.Save();
            }

            Default = [];
            MasterList.Add(this.pName, this);
        }

        public static implicit operator int[](UserIntArray o) => PluginConfiguration.GetCustomIntArrayValue(o.pName);

        public int this[int index]
        {
            get
            {
                // 讀取路徑不得寫入設定:原本越界時會 Array.Resize + Save(),
                // 等於在戰鬥迴圈裡「讀一次就寫一次磁碟」。
                // 改成越界直接回預設值(0),不 resize、不 Save;陣列長度的維護
                // 交給寫入路徑(UserConfig.DrawPriorityInput 會先呼叫 Clear(maxValues))。
                var array = PluginConfiguration.GetCustomIntArrayValue(this.pName);
                return index >= array.Length ? 0 : array[index];
            }
            set
            {
                if (index < this.Count)
                {
                    var array = PluginConfiguration.GetCustomIntArrayValue(this.pName);
                    array[index] = value;
                    Service.Configuration.Save();
                }
            }
        }

        public override void ResetToDefault()
        {
            PluginConfiguration.SetCustomIntArrayValue(this.pName, Default);
            Service.Configuration.Save();
        }
    }

    internal class UserBoolArray : UserData
    {
        // Constructor with only the string parameter
        public UserBoolArray(string v) : this(v, []) { }

        public bool[] Default;

        // Constructor with both string and bool array parameters
        public UserBoolArray(string v, bool[] defaults) : base(v)
        {
            if (!PluginConfiguration.CustomBoolArrayValues.ContainsKey(this.pName))
            {
                PluginConfiguration.SetCustomBoolArrayValue(this.pName, defaults);
                Service.Configuration.Save();
            }

            Default = defaults;
            MasterList.Add(this.pName, this);
        }

        public int Count => PluginConfiguration.GetCustomBoolArrayValue(this.pName).Length;
        public static implicit operator bool[](UserBoolArray o) => PluginConfiguration.GetCustomBoolArrayValue(o.pName);
        public bool this[int index]
        {
            get
            {
                // 讀取路徑不得寫入設定:原本越界時會 Array.Resize + Save(),
                // 等於在戰鬥迴圈裡「讀一次就寫一次磁碟」
                // (ContentCheck.IsInConfiguredContent 每幀都會讀 [0]/[1]/[2])。
                // 改成越界直接回預設值(false),不 resize、不 Save;陣列長度的維護交給
                // 寫入路徑(UserConfig.DrawHorizontalMultiChoice / DrawPvPStatusMultiChoice
                // 本來就會自己 resize + Save)。
                var array = PluginConfiguration.GetCustomBoolArrayValue(this.pName);
                return index >= array.Length ? false : array[index];
            }
        }

        public bool All(Func<bool, bool> predicate)
        {
            var array = PluginConfiguration.GetCustomBoolArrayValue(this.pName);
            return array.All(predicate);
        }

        public override void ResetToDefault()
        {
            PluginConfiguration.SetCustomBoolArrayValue(this.pName, Default);
            Service.Configuration.Save();
        }
    }

}
