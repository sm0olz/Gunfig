namespace Gunfiguration;

// Internal class for managing individual menu items. You should never need to use any of the functions in here directly.
internal class GunfigOption : MonoBehaviour
{
    private static List<GunfigOption> _PendingUpdatesOnConfirm = new();

    private string _lookupKey = "";                        // key for looking up in our configuration file
    private string _defaultValue = "";                        // our default value if the key is not found in the configuration file
    private string _currentValue = "";                        // our current effective value, barring any pending changes
    private string _pendingValue = "";                        // our pending value after changes are applies
    private Gunfig.Update _updateType = Gunfig.Update.OnConfirm;   // when to apply any pending changes
    private bool _callbackImmediately = false;
    private List<string> _validValues = new();                     // valid values for the option (auto populated with "true" and "false" for toggles)
    private Action<string, string> _onApplyChanges = null;                      // event handler for execution
    private Action<string, string> _onPendingValueChanged = null; // event handler for pending UI changes
    private List<string> _selectableValues = null;
    private dfControl _control = null;                      // the dfControl to which we're attached
    private Gunfig _parent = null;                      // the Gunfig instance that's handling us
    private Color _labelColor = Color.white;
    private List<Color> _optionColors = new();
    private List<Color> _infoColors = new();

    [HarmonyPatch(typeof(FullOptionsMenuController), nameof(FullOptionsMenuController.CloseAndRevertChanges))]
    /// <summary>Resets Gunfig menu options when menus are closed without saving changes</summary>
    private class OnMenuCancelPatch
    {
        static void Prefix(FullOptionsMenuController __instance)
        {
            foreach (GunfigOption option in _PendingUpdatesOnConfirm)
                option.ResetMenuItemState();
            _PendingUpdatesOnConfirm.Clear();
        }
    }

    [HarmonyPatch(typeof(FullOptionsMenuController), nameof(FullOptionsMenuController.CloseAndApplyChanges))]
    /// <summary>Registers Gunfig changes after saving menu changes</summary>
    private class OnMenuConfirmPatch
    {
        static void Prefix(FullOptionsMenuController __instance)
        {
            foreach (GunfigOption option in _PendingUpdatesOnConfirm)
            {
                switch (option._updateType)
                {
                    case Gunfig.Update.Immediate:
                        // Immediate options should already have been committed.
                        break;

                    case Gunfig.Update.OnConfirm:
                        option.CommitPendingChanges();
                        break;

                    case Gunfig.Update.OnRestart:
                        // Store the value so it is written to disk, but do not
                        // update the effective in-memory value or invoke the callback.
                        option._parent.Set(option._lookupKey, option._pendingValue);
                        break;
                }
            }
            Gunfig.SaveActiveConfigsToDisk();  // save all committed changes
            _PendingUpdatesOnConfirm.Clear();
        }
    }

    internal bool Matches(Gunfig parent, string key)
    {
        return this._parent == parent &&
               this._lookupKey == key;
    }

    private static void OnGotFocus(Action<BraveOptionsMenuItem, dfControl, dfFocusEventArgs> orig, BraveOptionsMenuItem menuItem, dfControl control, dfFocusEventArgs args)
    {
        orig(menuItem, control, args);
        if (menuItem.GetComponent<GunfigOption>() is GunfigOption option)
            option.UpdateColors(menuItem, dim: false);
    }

    private static void OnSetUnselectedColors(Action<BraveOptionsMenuItem> orig, BraveOptionsMenuItem menuItem)
    {
        orig(menuItem);
        if (menuItem.GetComponent<GunfigOption>() is GunfigOption option)
            option.UpdateColors(menuItem, dim: true);
    }

    private void CommitPendingChanges()
    {
        if (this._pendingValue == this._currentValue)
            return;  // we didn't change, so we shouldn't do anything

        string previousValue = this._currentValue;
        this._currentValue = this._pendingValue;

        // Register the newly committed value.
        this._parent.Set(this._lookupKey, this._currentValue);

        if (this._updateType == Gunfig.Update.Immediate) // register and save immediate changes to disk
            Gunfig.SaveActiveConfigsToDisk();

        if (this._onApplyChanges != null)
        {
            try
            {
                this._onApplyChanges(this._lookupKey, this._currentValue);
            }
            catch (Exception)
            {
                // Revert the effective value if the callback fails.
                this._currentValue = previousValue;
                this._parent.Set(this._lookupKey, this._currentValue);

                if (this._updateType == Gunfig.Update.Immediate) // register and save immediate changes to disk
                    Gunfig.SaveActiveConfigsToDisk();

                throw;
            }
        }
    }

    // Values outside _selectableValues remain valid, but are skipped by menu navigation.
    internal bool IsValueSelectable(string value)
    {
        if (this._selectableValues == null)
            return true;

        return this._selectableValues.Contains(value);
    }

    // Gets the value currently displayed or pending in the menu.
    internal string GetPendingValue()
    {
        return this._pendingValue;
    }

    private void OnControlChanged(dfControl control, string stringValue)
    {
        this._pendingValue = stringValue;
        OnControlChanged();
    }

    private void OnControlChanged(dfControl control, bool toggleValue)
    {
        this._pendingValue = toggleValue ? "1" : "0";
        OnControlChanged();
    }

    private void OnButtonClicked(dfControl control)
    {
        if (this._onApplyChanges != null)
            this._onApplyChanges(this._lookupKey, this._pendingValue);
    }

    private void OnControlChanged()
    {
        if (this._updateType == Gunfig.Update.Immediate)
        {
            CommitPendingChanges();
        }
        else
        {
            if (!_PendingUpdatesOnConfirm.Contains(this))
                _PendingUpdatesOnConfirm.Add(this);

            if (this._callbackImmediately && this._onApplyChanges != null)
            {
                try
                {
                    this._onApplyChanges(this._lookupKey, this._pendingValue);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        if (this._onPendingValueChanged != null)
        {
            try
            {
                this._onPendingValueChanged(
                    this._lookupKey,
                    this._pendingValue);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        UpdateColors(base.GetComponent<BraveOptionsMenuItem>(), dim: false); // we can probably safely assume we have focus
    }

    /// <summary>
    /// Changes the pending value of this option and updates its visible menu state. The value is committed according to the option's <see cref="Gunfig.Update"/> type.
    /// </summary>
    internal bool SetPendingValue(string value)
    {
        if (string.IsNullOrEmpty(this._lookupKey))
            return false;

        if (value == null || !this._validValues.Contains(value))
            return false;

        if (this._pendingValue == value)
            return true;

        this._pendingValue = value;

        if (this._updateType == Gunfig.Update.Immediate)
        {
            CommitPendingChanges();
        }
        else
        {
            if (!_PendingUpdatesOnConfirm.Contains(this))
                _PendingUpdatesOnConfirm.Add(this);
        }

        BraveOptionsMenuItem menuItem = base.GetComponent<BraveOptionsMenuItem>();
        if (menuItem)
        {
            // Update the visible menu control to match the new pending value.
            if (menuItem.selectedLabelControl != null && menuItem.labelOptions != null)
            {
                for (int i = 0; i < menuItem.labelOptions.Length; ++i)
                {
                    if (menuItem.labelOptions[i] != value)
                        continue;

                    GunfigMenu._menuItemSelectedIndexRef(menuItem) = i;
                    menuItem.selectedLabelControl.Text = menuItem.labelOptions[i];

                    if (menuItem.infoControl != null &&
                        menuItem.infoOptions != null &&
                        i < menuItem.infoOptions.Length)
                    {
                        menuItem.infoControl.Text = menuItem.infoOptions[i];
                    }

                    menuItem.selectedLabelControl.PerformLayout();

                    if (menuItem.infoControl != null)
                        menuItem.infoControl.PerformLayout();

                    break;
                }
            }

            if (menuItem.checkboxChecked != null)
            {
                bool isChecked = value == "1";

                menuItem.checkboxChecked.IsVisible = isChecked;

                if (menuItem.checkboxUnchecked != null)
                    menuItem.checkboxUnchecked.IsVisible = !isChecked;

                GunfigMenu._menuItemSelectedIndexRef(menuItem) = isChecked ? 1 : 0;
            }

            UpdateColors(menuItem, dim: false);
        }

        if (this._onPendingValueChanged != null)
        {
            try
            {
                this._onPendingValueChanged(this._lookupKey, this._pendingValue);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        return true;
    }

    private void ProcessColors()
    {
        // Make sure we have a menu item
        BraveOptionsMenuItem menuItem = base.GetComponent<BraveOptionsMenuItem>();
        if (!menuItem)
        {
            ETGModConsole.Log($"  NULL BRAVE MENU ITEM");
            return;
        }

        // Set up color info from individual label texts
        if (menuItem.labelControl is dfLabel label)
            label.Text = label.Text.ProcessColors(out this._labelColor);
        if (menuItem.buttonControl is dfButton button)
            button.Text = button.Text.ProcessColors(out this._labelColor);
        if ((menuItem.selectedLabelControl is dfLabel settingLabel) && menuItem.labelOptions != null)
        {
            this._optionColors.Clear();
            this._infoColors.Clear();

            Color c;
            bool hasInfo = ((menuItem.infoControl != null) && (menuItem.infoOptions != null) && (menuItem.labelOptions.Length == menuItem.infoOptions.Length));
            for (int i = 0; i < menuItem.labelOptions.Length; ++i)
            {
                menuItem.labelOptions[i] = menuItem.labelOptions[i].ProcessColors(out c);
                this._optionColors.Add(c);
                if (hasInfo)
                {
                    menuItem.infoOptions[i] = menuItem.infoOptions[i].ProcessColors(out c);
                    this._infoColors.Add(c);
                }
            }
        }

        this._control.IsVisibleChanged += (_, _) => UpdateColors(menuItem, true);
    }

    private void ResetMenuItemState(bool addHandlers = false)
    {
        // Make sure we have a menu item
        BraveOptionsMenuItem menuItem = base.GetComponent<BraveOptionsMenuItem>();
        if (!menuItem)
        {
            ETGModConsole.Log($"  NULL BRAVE MENU ITEM");
            return;
        }

        // Reset both the visible control and the pending value to the currently committed value when reverting menu changes.
        this._pendingValue = this._currentValue;

        // Set up the state of our menu item from our config
        if (menuItem.buttonControl is dfButton button)
        {
            if (addHandlers)
                this._control.gameObject.GetOrAddComponent<GunfigMenu.CustomButtonHandler>().onClicked += OnButtonClicked;
        }
        if (menuItem.checkboxChecked is dfControl checkBox)
        {
            bool isChecked = (this._currentValue.Trim() == "1");
            checkBox.IsVisible = isChecked;
            if (menuItem.checkboxUnchecked is dfControl checkBoxUnchecked)
                checkBoxUnchecked.IsVisible = !isChecked;
            menuItem.m_selectedIndex = isChecked ? 1 : 0;
            if (addHandlers)
                this._control.gameObject.GetOrAddComponent<GunfigMenu.CustomCheckboxHandler>().onChanged += OnControlChanged;
        }
        if ((menuItem.selectedLabelControl is dfLabel settingLabel) && menuItem.labelOptions != null)
        {
            bool hasInfo = ((menuItem.infoControl != null) && (menuItem.infoOptions != null) && (menuItem.labelOptions.Length == menuItem.infoOptions.Length));
            for (int i = 0; i < menuItem.labelOptions.Length; ++i)
            {
                if (menuItem.labelOptions[i] != this._currentValue)
                    continue;
                settingLabel.Text = menuItem.labelOptions[i];
                if (hasInfo)
                    menuItem.infoControl.Text = menuItem.infoOptions[i];

                settingLabel.PerformLayout();
                if (hasInfo)
                    menuItem.infoControl.PerformLayout();

                menuItem.m_selectedIndex = i;
                break;
            }
            if (addHandlers)
                this._control.gameObject.GetOrAddComponent<GunfigMenu.CustomLeftRightArrowHandler>().onChanged += OnControlChanged;
        }

        UpdateColors(menuItem, dim: true);
    }

    internal void UpdateColors(BraveOptionsMenuItem menuItem, bool dim)
    {
        if (menuItem.labelControl != null)
            menuItem.labelControl.Color = this._labelColor.Dim(dim);
        if (menuItem.buttonControl != null)
            menuItem.buttonControl.TextColor = this._labelColor.Dim(dim);
        if (menuItem.selectedLabelControl != null && this._optionColors.Count > 0)
            menuItem.selectedLabelControl.Color = this._optionColors[menuItem.m_selectedIndex % this._optionColors.Count].Dim(dim);
        if (menuItem.infoControl != null && this._infoColors.Count > 0)
        {
            menuItem.infoControl.Text = menuItem.infoOptions[menuItem.m_selectedIndex % this._infoColors.Count];
            menuItem.infoControl.Color = this._infoColors[menuItem.m_selectedIndex % this._infoColors.Count].Dim(dim);
        }
    }

    // Determines whether any Gunfig option has an uncommitted menu change.
    internal static bool HasPendingChanges()
    {
        foreach (GunfigOption option in _PendingUpdatesOnConfirm)
            if (option._pendingValue != option._currentValue)
                return true;
        return false;
    }

    /// <summary>
    /// Initializes this menu option and loads its current value from the parent configuration.
    /// </summary>
    /// <param name="parentConfig">The Gunfig configuration that owns this option.</param>
    /// <param name="key">The configuration key used to store the option.</param>
    /// <param name="values">The valid values for this option.</param>
    /// <param name="update">The callback invoked when the option's value is applied.</param>
    /// <param name="updateType">Determines when pending changes are committed.</param>
    /// <param name="defaultValue">The value to use when no existing configuration value is found.</param>
    /// <param name="callbackImmediately">Whether the apply callback should also be invoked when a pending value changes. This does not change when the value itself is committed.</param>
    /// <param name="pendingValueChanged">A callback invoked whenever the pending menu value changes.</param>
    /// <param name="selectableValues">Optional values that may be selected through normal menu navigation. Values omitted from this list remain valid but are skipped during navigation.</param>
    internal void Setup(Gunfig parentConfig, string key, List<string> values, Action<string, string> update, Gunfig.Update updateType = Gunfig.Update.OnConfirm, string defaultValue = null,
        bool callbackImmediately = false, Action<string, string> pendingValueChanged = null, List<string> selectableValues = null)
    {
        this._control = base.GetComponent<dfControl>();
        this._parent = parentConfig;
        this._lookupKey = key;
        this._validValues = values;
        this._onApplyChanges = update;
        this._updateType = updateType;
        this._callbackImmediately = callbackImmediately;
        this._onPendingValueChanged = pendingValueChanged;
        this._selectableValues = selectableValues;

        // Load our default and current values from our config, or from the options passed to us
        if (!string.IsNullOrEmpty(key))  // null or empty key == pseudo-option used for its callback only
        {
            this._defaultValue = defaultValue ?? values[0];
            this._defaultValue = this._defaultValue.ProcessColors(out Color _);
            this._currentValue = this._parent.Value(this._lookupKey)
                ?? this._parent.Set(this._lookupKey, this._defaultValue);

            this._pendingValue = this._currentValue;
        }

        ProcessColors();
        ResetMenuItemState(addHandlers: true);
    }
}
