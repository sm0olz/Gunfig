namespace Gunfiguration;

// Internal portion of the Gunfig API. You should never need to use any of the functions in here directly.
public partial class Gunfig
{
    /*
       Eventual planned QoL improvements to make, from most to least important:
        - prompts to save changes are a little unpredictable when navigating sub menus
        - remember cursor position when backing out of menus
        - can't dynamically enable / disable options at runtime (must restart the game)
        - buttons and labels get written out to gunfig files

       Unimportant stuff I probably won't do:
        - scrollbar sometimes disappears on main menu and entire menu glitches (spent hours trying to fix it, can just reopen menu to fix, only present on main menu)
        - double menu sounds are played when navigating to new pages due to onfocus events for buttons playing sounds (tricky to fix and barely noticeable)
        - haven't implemented progress / fill bars (not particularly useful outside vanilla volume controls, so not in a hurry to implement this)
        - haven't implemented sprites for options (e.g. like vanilla crosshair selection) (very hard, requires modifying sprite atlas, and it is minimally useful)
    */

    private enum ItemType
    {
        Label,
        Button,
        CheckBox,
        ArrowBox,
    }

    private class Item
    {
        internal ItemType _itemType = ItemType.Label;
        internal Gunfig.Update _updateType = Gunfig.Update.OnConfirm;
        internal string _key = null;
        internal string _label = null;
        internal List<string> _values = null;
        internal List<string> _info = null;
        internal Action<string, string> _callback = null;
        internal string _defaultValue = null;           // default value when no config value exists
        internal bool _callbackImmediately = false;     // invoke the callback when a pending value changes
        internal Action<string, string> _pendingValueChanged = null; // event handler for pending UI changes
        internal List<string> _selectableValues = null; // values available through normal menu navigation
    }

    [HarmonyPatch(typeof(FinalIntroSequenceManager), nameof(FinalIntroSequenceManager.Start))]
    private static class FinalIntroSequenceManagerStartPatch // doesn't actually hook the ienumerator itsef, only the implicit method that calls it
    {
        private static bool _AllModsLoaded = false;
        static void Prefix()
        {
            if (_AllModsLoaded)
                return;
            _AllModsLoaded = true;
            if (OnAllModsLoaded != null)
                OnAllModsLoaded();
        }
    }

    internal static readonly List<string> _UncheckedBoxValues = new() { "0", "1" }; // options for default-unchecked checkboxes
    internal static readonly List<string> _CheckedBoxValues = new() { "1", "0" }; // options for default-checked checkboxes
    internal static readonly List<string> _DefaultValues = new() { "1" };      // dummy option for things like buttons

    internal static List<Gunfig> _ActiveConfigs = new(); // list of all extant Gunfig instances
    internal static List<string> _ConfigAssemblies = new(); // list of assembly names associated with Gunfig instances, to avoid illegal accesses

    private Dictionary<string, string> _options = new(); // dictionary of mod options as key value pairs
    private List<Item> _registeredOptions = new(); // list of options from which we can dynamically regenerate the options panel
    private bool _dirty = false; // whether we've been changed since last saving to disk
    private string _configFile = null;  // the file on disk to which we're writing
    private Gunfig _parentGunfig = null;  // our parent Gunfig, if we're a sub-menu
    private string _cleanModName = null;  // the name of our mod, without formatting
    private dfScrollPanel _cachedConfigPage = null;  // our cached config page, for submenu navigation purposes

    internal string _modName = null;  // the name of our mod, including any formatting
    internal Gunfig _BaseGunfig => this._parentGunfig ?? this;  // the Gunfig instance actually responsible for managing our options

    protected Gunfig() { } // cannot construct Gunfig directly, must create / retrieve through Gunfig.Get()

    internal static void SaveActiveConfigsToDisk()
    {
        foreach (Gunfig config in _ActiveConfigs)
        {
            if (!config._dirty)
                continue;
            config.SaveToDisk();
            config._dirty = false;
        }
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(this._configFile))
            return;
        try
        {
            string[] lines = File.ReadAllLines(this._configFile);
            foreach (string line in lines)
            {
                string[] tokens = line.Split('=');
                if (tokens.Length != 2 || tokens[0] == null || tokens[1] == null)
                    continue;
                string key = tokens[0].Trim();
                string val = tokens[1].Trim();
                if (key.Length == 0 || val.Length == 0)
                    continue;
                this._options[key] = val;
            }
        }
        catch (Exception e)
        {
            ETGModConsole.Log($"    error loading mod config file {this._configFile}: {e}");
        }
    }

    private void SaveToDisk()
    {
        try
        {
            using (StreamWriter file = File.CreateText(this._configFile))
            {
                foreach (string key in this._options.Keys)
                {
                    if (string.IsNullOrEmpty(key))
                        continue;
                    file.WriteLine($"{key} = {this._options[key]}");
                }
            }
        }
        catch (Exception e)
        {
            ETGModConsole.Log($"    error saving mod config file {this._configFile}: {e}");
        }
    }

    // Initialize missing checkbox and scroll box values.
    // Invalid or missing defaults fall back to the first available value.
    private void RegisterOption(Item item)
    {
        this._registeredOptions.Add(item);
        if (item._itemType != ItemType.CheckBox && item._itemType != ItemType.ArrowBox)
            return;

        if (this._BaseGunfig.Value(item._key) == null) // make sure we have a default value for all loaded configuration options
        {
            string defaultValue = item._defaultValue;

            if (string.IsNullOrEmpty(defaultValue) || !item._values.Contains(defaultValue))
                defaultValue = item._values[0];

            this._BaseGunfig.Set(item._key, defaultValue);
        }
    }

    private Gunfig GetSubMenu(string menuName)
    {
        string cleanMenuName = $"{this._cleanModName}, {menuName.ProcessColors(out Color _)}";
        for (int i = 0; i < _ActiveConfigs.Count; ++i)
        {
            Gunfig config = _ActiveConfigs[i];
            if (config._cleanModName != cleanMenuName)
                continue;
            ETGModConsole.Log($"WARNING: tried to a create a duplicate submenu {menuName} under {this._cleanModName}");
            return null;
        }

        GunfigDebug.Log($"  Creating new Gunfig submenu {cleanMenuName} for {this._cleanModName}");
        Gunfig subGunfig = new Gunfig();
        subGunfig._modName = menuName;  // need to keep colors intact here
        subGunfig._cleanModName = cleanMenuName;  // need to remove colors here
        subGunfig._parentGunfig = this._parentGunfig ?? this;  // parent the gunfig to our parent if it exists, or ourself if we're a top-level gunfig
        _ActiveConfigs.Add(subGunfig);
        _ConfigAssemblies.Add(Assembly.GetCallingAssembly().FullName); // cache our assembly name to avoid illegal config accesses from other mods
        return subGunfig;
    }

    internal dfScrollPanel RegenConfigPage()
    {
        _cachedConfigPage = GunfigMenu.NewOptionsPanel($"{this._modName}");
        foreach (Item item in this._registeredOptions)
        {
            dfControl itemControl;
            switch (item._itemType)
            {
                default:
                case ItemType.Label:
                    itemControl = _cachedConfigPage.AddLabel(label: item._label);
                    break;
                case ItemType.Button:
                    itemControl = _cachedConfigPage.AddButton(label: item._label);
                    break;
                case ItemType.CheckBox:
                    itemControl = _cachedConfigPage.AddCheckBox(label: item._label);
                    break;
                case ItemType.ArrowBox:
                    itemControl = _cachedConfigPage.AddArrowBox(label: item._label, options: item._values, info: item._info, defaultValue: item._defaultValue);
                    break;
            }
            if (item._itemType != ItemType.Label) // pure labels don't need a GunfigOption and handle markup processing on site
                itemControl.gameObject.AddComponent<GunfigOption>().Setup(
                  parentConfig: this._BaseGunfig, key: item._key, values: item._values, update: item._callback, updateType: item._updateType, defaultValue: item._defaultValue,
                  callbackImmediately: item._callbackImmediately, pendingValueChanged: item._pendingValueChanged, selectableValues: item._selectableValues);
        }
        return _cachedConfigPage;
    }

    internal void OpenConfigPage(string key, string value)
    {
        GunfigMenu.OpenSubMenu(this._cachedConfigPage);
    }

    // Set a config key to a value and return the value
    internal string Set(string key, string value)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        this._options[key] = value;
        this._dirty = true;
        return value;
    }
}

// Private portion of GunfigHelpers
public static partial class GunfigHelpers
{
    private const string MARKUP_DELIM = "@"; // "#" is used for localization strings, so we need something else

    // Helpers for processing colors on various dfControls
    internal static Color Dim(this Color c, bool dim) => Color.Lerp(dim ? Color.black : Color.white, c, 0.5f);
    internal static string ProcessColors(this string markupText, out Color color)
    {
        string processedText = markupText;
        color = Color.white;
        if (processedText.StartsWith(MARKUP_DELIM))
        {
            // convert "@" back to "#" for the purposes of color conversion
            if (ColorUtility.TryParseHtmlString($"#{processedText.Substring(1, 6)}", out color))
                processedText = processedText.Substring(7);
            else
                color = Color.white;
        }
        return processedText;
    }
}
