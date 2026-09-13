namespace Gunfiguration;

// Internal class for actually constructing menus. You should never need to use any of the functions in here directly.
internal static class GunfigMenu
{
    internal const string _GUNFIG_EXTENSION = "gunfig";

    private const string _MOD_MENU_LABEL = "MOD CONFIG";
    private const string _MOD_MENU_TITLE = "Modded Options";
    private static List<dfControl> _RegisteredTabs = new();
    private static Stack<dfScrollPanel> _MenuStack = new();
    private static dfScrollPanel _GunfigMainPanel = null;
    private static dfScrollPanel _RefPanel = null;

    private static readonly MethodInfo _handleValueChangedMethod =
        AccessTools.Method(typeof(BraveOptionsMenuItem), "HandleValueChanged");

    internal class CustomCheckboxHandler : MonoBehaviour
    { public PropertyChangedEventHandler<bool> onChanged; }

    internal class CustomLeftRightArrowHandler : MonoBehaviour
    { public PropertyChangedEventHandler<string> onChanged; }

    internal class CustomButtonHandler : MonoBehaviour
    { public Action<dfControl> onClicked; }

    internal static void Init()
    {
        // for backing out of one menu at a time -> calls CloseAndMaybeApplyChangesWithPrompt() when escape is pressed
        // new Hook(
        //     typeof(PreOptionsMenuController).GetMethod("ReturnToPreOptionsMenu", BindingFlags.Instance | BindingFlags.Public),
        //     typeof(MenuMaster).GetMethod("ReturnToPreOptionsMenu", BindingFlags.Static | BindingFlags.NonPublic)
        //     );

        // Make sure our menus are loaded in the main menu
        new Hook(
            typeof(MainMenuFoyerController).GetMethod("InitializeMainMenu", BindingFlags.Instance | BindingFlags.Public),
            typeof(GunfigMenu).GetMethod("InitializeMainMenu", BindingFlags.Static | BindingFlags.NonPublic)
            );

        // Make sure our menus are loaded in game
        new Hook(
            typeof(GameManager).GetMethod("Pause", BindingFlags.Instance | BindingFlags.Public),
            typeof(GunfigMenu).GetMethod("Pause", BindingFlags.Static | BindingFlags.NonPublic)
            );

        // Make sure our menus appear and disappear properly
        new Hook(
            typeof(FullOptionsMenuController).GetMethod("ToggleToPanel", BindingFlags.Instance | BindingFlags.Public),
            typeof(GunfigMenu).GetMethod("ToggleToPanel", BindingFlags.Static | BindingFlags.NonPublic)
            );

        // Make sure we respect discarded changes
        new Hook(
            typeof(GameOptions).GetMethod("CompareSettings", BindingFlags.Static | BindingFlags.Public),
            typeof(GunfigMenu).GetMethod("CompareSettings", BindingFlags.Static | BindingFlags.NonPublic)
            );

        // Custom checkbox events
        new Hook(
            typeof(BraveOptionsMenuItem).GetMethod("HandleCheckboxValueChanged", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(GunfigMenu).GetMethod("HandleCheckboxValueChanged", BindingFlags.Static | BindingFlags.NonPublic)
            );

        // Custom arrowbox events
        new Hook(
            typeof(BraveOptionsMenuItem).GetMethod("HandleLeftRightArrowValueChanged", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(GunfigMenu).GetMethod("HandleLeftRightArrowValueChanged", BindingFlags.Static | BindingFlags.NonPublic)
            );

        // Skip display-only arrowbox values when navigating left/right
        new Hook(
            typeof(BraveOptionsMenuItem).GetMethod("IncrementArrow", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(GunfigMenu).GetMethod("IncrementArrow", BindingFlags.Static | BindingFlags.NonPublic)
            );

        new Hook(
            typeof(BraveOptionsMenuItem).GetMethod("DecrementArrow", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(GunfigMenu).GetMethod("DecrementArrow", BindingFlags.Static | BindingFlags.NonPublic)
            );

        // Custom button events
        new Hook(
            typeof(BraveOptionsMenuItem).GetMethod("DoSelectedAction", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(GunfigMenu).GetMethod("DoSelectedAction", BindingFlags.Static | BindingFlags.NonPublic)
            );

        // Update custom colors on focus gained
        new Hook(
            typeof(BraveOptionsMenuItem).GetMethod("DoFocus", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(GunfigOption).GetMethod("OnGotFocus", BindingFlags.Static | BindingFlags.NonPublic)
            );

        // Update custom colors on focus lost
        new Hook(
            typeof(BraveOptionsMenuItem).GetMethod("SetUnselectedColors", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(GunfigOption).GetMethod("OnSetUnselectedColors", BindingFlags.Static | BindingFlags.NonPublic)
            );
    }

    // private static void ReturnToPreOptionsMenu(Action<PreOptionsMenuController> orig, PreOptionsMenuController pm)
    // {
    //   orig(pm);
    // }

    private static void InitializeMainMenu(Action<MainMenuFoyerController> orig, MainMenuFoyerController mm)
    {
        if (GameUIRoot.Instance.PauseMenuPanel.GetComponent<PauseMenuController>().OptionsMenu.PreOptionsMenu is PreOptionsMenuController preOptions)
            if (!preOptions.m_panel.Find<dfButton>(_MOD_MENU_LABEL))
                preOptions.CreateGunfigButton();
        orig(mm);
    }

    private static void Pause(Action<GameManager> orig, GameManager gm)
    {
        if (GameUIRoot.Instance.PauseMenuPanel.GetComponent<PauseMenuController>().OptionsMenu.PreOptionsMenu is PreOptionsMenuController preOptions)
            if (!preOptions.m_panel.Find<dfButton>(_MOD_MENU_LABEL))
                preOptions.CreateGunfigButton();
        orig(gm);
    }

    private static void ToggleToPanel(Action<FullOptionsMenuController, dfScrollPanel, bool> orig, FullOptionsMenuController controller, dfScrollPanel targetPanel, bool doFocus)
    {
        bool isOurPanel = false;
        foreach (dfControl tab in _RegisteredTabs)
        {
            bool match = (tab == targetPanel);  // need to cache this because tab.IsVisible property doesn't return as expected
            tab.IsVisible = match;
            isOurPanel |= match;
        }
        orig(controller, targetPanel, doFocus);
        if (isOurPanel)
        {
            for (int i = 0; i < targetPanel.controls.Count; ++i)
            {
                dfControl c = targetPanel.controls[i];
                if (!c.gameObject.GetComponent<BraveOptionsMenuItem>())
                    continue;
                c.RecursiveFocus();  // fix bug where first item isn't highlighted
                break;
            }
            SetOptionsPageTitle(targetPanel.name);
            // PrintControlRecursive(targetPanel, dissect: false);
        }
        else
            SetOptionsPageTitle("Options");
        // if (isOurPanel)
        // {
        //   foreach (dfControl control in targetPanel.controls) // find first focusable element and select it
        //   {
        //     if (!control.GetComponent<BraveOptionsMenuItem>())
        //       continue;
        //     control.RecursiveFocus();  // fix bug where first item isn't highlighted
        //     break;
        //   }
        // }
    }

    private static bool CompareSettings(Func<GameOptions, GameOptions, bool> orig, GameOptions clone, GameOptions source)
    {
        if (GunfigOption.HasPendingChanges())
            return false; // we have pending updates, so prompt to discard
        return orig(clone, source);
    }

    private static void HandleCheckboxValueChanged(Action<BraveOptionsMenuItem> orig, BraveOptionsMenuItem item)
    {
        orig(item);
        if (item.GetComponent<CustomCheckboxHandler>() is CustomCheckboxHandler handler)
            handler.onChanged(item.m_self, item.m_selectedIndex == 1);
    }

    private static void HandleLeftRightArrowValueChanged(Action<BraveOptionsMenuItem> orig, BraveOptionsMenuItem item)
    {
        orig(item);
        if (item.GetComponent<CustomLeftRightArrowHandler>() is CustomLeftRightArrowHandler handler)
            handler.onChanged(item.m_self, item.labelOptions[item.m_selectedIndex]);
    }

    private static void IncrementArrow(Action<BraveOptionsMenuItem, dfControl, dfMouseEventArgs> orig, BraveOptionsMenuItem item, dfControl control, dfMouseEventArgs mouseEvent)
    {
        GunfigOption option = item.GetComponent<GunfigOption>();

        if (option == null || item.labelOptions == null || item.labelOptions.Length == 0)
        {
            orig(item, control, mouseEvent);
            return;
        }

        int currentIndex = item.m_selectedIndex;
        int nextIndex = FindNextSelectableIndex(
            item,
            option,
            currentIndex,
            direction: 1);

        if (nextIndex == currentIndex)
        {
            orig(item, control, mouseEvent);
            return;
        }

        if (nextIndex == (currentIndex + 1) % item.labelOptions.Length)
        {
            // The normal next value is selectable, so let the game handle it.
            orig(item, control, mouseEvent);
            return;
        }

        // Skip values that are valid but not selectable through normal navigation.
        AkSoundEngine.PostEvent(
            "Play_UI_menu_select_01",
            item.gameObject);

        item.m_selectedIndex = nextIndex;

        InvokeHandleValueChanged(item);
    }

    private static void DecrementArrow(Action<BraveOptionsMenuItem, dfControl, dfMouseEventArgs> orig, BraveOptionsMenuItem item, dfControl control, dfMouseEventArgs mouseEvent)
    {
        GunfigOption option = item.GetComponent<GunfigOption>();

        if (option == null || item.labelOptions == null || item.labelOptions.Length == 0)
        {
            orig(item, control, mouseEvent);
            return;
        }

        int currentIndex = item.m_selectedIndex;
        int nextIndex = FindNextSelectableIndex(
            item,
            option,
            currentIndex,
            direction: -1);

        if (nextIndex == currentIndex)
        {
            orig(item, control, mouseEvent);
            return;
        }

        int normalNextIndex =
            (currentIndex - 1 + item.labelOptions.Length) %
            item.labelOptions.Length;

        if (nextIndex == normalNextIndex)
        {
            // The normal previous value is selectable, so let the game handle it.
            orig(item, control, mouseEvent);
            return;
        }

        // Skip values that are valid but not selectable through normal navigation.
        AkSoundEngine.PostEvent(
            "Play_UI_menu_select_01",
            item.gameObject);

        item.m_selectedIndex = nextIndex;

        InvokeHandleValueChanged(item);
    }

    // Find the next selectable value, wrapping around the available options. Values that are valid but not selectable are skipped.
    private static int FindNextSelectableIndex(BraveOptionsMenuItem item, GunfigOption option, int currentIndex, int direction)
    {
        int count = item.labelOptions.Length;

        for (int i = 1; i <= count; ++i)
        {
            int index = (currentIndex + direction * i) % count;

            if (index < 0)
                index += count;

            if (option.IsValueSelectable(item.labelOptions[index]))
                return index;
        }

        return currentIndex;
    }

    // Notify the normal value-change pipeline after manually changing the selected index.
    private static void InvokeHandleValueChanged(BraveOptionsMenuItem item)
    {
        _handleValueChangedMethod.Invoke(item, null);
    }

    private static void DoSelectedAction(Action<BraveOptionsMenuItem> orig, BraveOptionsMenuItem item)
    {
        orig(item);
        if (item.GetComponent<CustomButtonHandler>() is CustomButtonHandler handler)
            handler.onClicked(item.m_self);
    }

    private static void FocusControl(dfControl control, dfMouseEventArgs args)
    {
        control.Focus();
    }

    private static void PlayMenuCursorSound(dfControl control)
    {
        AkSoundEngine.PostEvent("Play_UI_menu_select_01", control.gameObject);
    }

    private static void PlayMenuCursorSound(dfControl control, dfMouseEventArgs args)
    {
        PlayMenuCursorSound(control);
    }

    private static void PlayMenuCursorSound(dfControl control, dfFocusEventArgs args)
    {
        PlayMenuCursorSound(control);
    }

    private static dfPanel _CachedPrototypeCheckboxWrapperPanel = null;
    private static dfPanel _CachedPrototypeCheckboxInnerPanel = null;
    private static dfCheckbox _CachedPrototypeCheckbox = null;
    private static dfSprite _CachedPrototypeEmptyCheckboxSprite = null;
    private static dfSprite _CachedPrototypeCheckedCheckboxSprite = null;
    private static dfLabel _CachedPrototypeCheckboxLabel = null;
    private static dfPanel _CachedPrototypeLeftRightWrapperPanel = null;
    private static dfPanel _CachedPrototypeLeftRightInnerPanel = null;
    private static dfLabel _CachedPrototypeLeftRightPanelLabel = null;
    private static dfSprite _CachedPrototypeLeftRightPanelLeftSprite = null;
    private static dfSprite _CachedPrototypeLeftRightPanelRightSprite = null;
    private static dfLabel _CachedPrototypeLeftRightPanelSelection = null;
    private static dfPanel _CachedPrototypeInfoWrapperPanel = null;
    private static dfPanel _CachedPrototypeInfoInnerPanel = null;
    private static dfLabel _CachedPrototypeInfoPanelLabel = null;
    private static dfSprite _CachedPrototypeInfoPanelLeftSprite = null;
    private static dfSprite _CachedPrototypeInfoPanelRightSprite = null;
    private static dfLabel _CachedPrototypeInfoPanelSelection = null;
    private static dfLabel _CachedPrototypeInfoInfoPanel = null;
    private static dfPanel _CachedPrototypeButtonWrapperPanel = null;
    private static dfPanel _CachedPrototypeButtonInnerPanel = null;
    private static dfButton _CachedPrototypeButton = null;
    private static dfPanel _CachedPrototypeLabelWrapperPanel = null;
    private static dfPanel _CachedPrototypeLabelInnerPanel = null;
    private static dfLabel _CachedPrototypeLabel = null;

    // References to dfControls are invalidated after each run is started, so we need to clear any cached values whenever panels are rebuilt
    private static void ReCacheControlsAndTabs()
    {
        // Clear out all registered UI tabs, since we need to build everything fresh
        _RegisteredTabs.Clear();

        FullOptionsMenuController optionsMenu = GameUIRoot.Instance.PauseMenuPanel.GetComponent<PauseMenuController>().OptionsMenu;

        _CachedPrototypeCheckboxWrapperPanel = optionsMenu.TabVideo.Find<dfPanel>("V-SyncCheckBoxPanel");
        _CachedPrototypeCheckboxInnerPanel = _CachedPrototypeCheckboxWrapperPanel.Find<dfPanel>("Panel");
        _CachedPrototypeCheckbox = _CachedPrototypeCheckboxInnerPanel.Find<dfCheckbox>("Checkbox");
        _CachedPrototypeEmptyCheckboxSprite = _CachedPrototypeCheckbox.Find<dfSprite>("EmptyCheckbox");
        _CachedPrototypeCheckedCheckboxSprite = _CachedPrototypeCheckbox.Find<dfSprite>("CheckedCheckbox");
        _CachedPrototypeCheckboxLabel = _CachedPrototypeCheckboxInnerPanel.Find<dfLabel>("CheckboxLabel");

        _CachedPrototypeLeftRightWrapperPanel = optionsMenu.TabVideo.Find<dfPanel>("VisualPresetArrowSelectorPanel");
        _CachedPrototypeLeftRightInnerPanel = _CachedPrototypeLeftRightWrapperPanel.Find<dfPanel>("PanelEnsmallenerThatmakesDavesLifeHardandBrentsLifeEasy");
        _CachedPrototypeLeftRightPanelLabel = _CachedPrototypeLeftRightInnerPanel.Find<dfLabel>("OptionsArrowSelectorLabel");
        _CachedPrototypeLeftRightPanelLeftSprite = _CachedPrototypeLeftRightInnerPanel.Find<dfSprite>("OptionsArrowSelectorArrowLeft");
        _CachedPrototypeLeftRightPanelRightSprite = _CachedPrototypeLeftRightInnerPanel.Find<dfSprite>("OptionsArrowSelectorArrowRight");
        _CachedPrototypeLeftRightPanelSelection = _CachedPrototypeLeftRightInnerPanel.Find<dfLabel>("OptionsArrowSelectorSelection");

        _CachedPrototypeInfoWrapperPanel = optionsMenu.TabVideo.Find<dfPanel>("ResolutionArrowSelectorPanelWithInfoBox");
        _CachedPrototypeInfoInnerPanel = _CachedPrototypeInfoWrapperPanel.Find<dfPanel>("PanelEnsmallenerThatmakesDavesLifeHardandBrentsLifeEasy");
        _CachedPrototypeInfoPanelLabel = _CachedPrototypeInfoInnerPanel.Find<dfLabel>("OptionsArrowSelectorLabel");
        _CachedPrototypeInfoPanelLeftSprite = _CachedPrototypeInfoInnerPanel.Find<dfSprite>("OptionsArrowSelectorArrowLeft");
        _CachedPrototypeInfoPanelRightSprite = _CachedPrototypeInfoInnerPanel.Find<dfSprite>("OptionsArrowSelectorArrowRight");
        _CachedPrototypeInfoPanelSelection = _CachedPrototypeInfoInnerPanel.Find<dfLabel>("OptionsArrowSelectorSelection");
        _CachedPrototypeInfoInfoPanel = _CachedPrototypeInfoWrapperPanel.Find<dfLabel>("OptionsArrowSelectorInfoLabel");

        _CachedPrototypeButtonWrapperPanel = optionsMenu.TabControls.Find<dfPanel>("EditKeyboardBindingsButtonPanel");
        _CachedPrototypeButtonInnerPanel = _CachedPrototypeButtonWrapperPanel.Find<dfPanel>("PanelEnsmallenerThatmakesDavesLifeHardandBrentsLifeEasy");
        _CachedPrototypeButton = _CachedPrototypeButtonInnerPanel.Find<dfButton>("EditKeyboardBindingsButton");

        _CachedPrototypeLabelWrapperPanel = optionsMenu.TabControls.Find<dfPanel>("PlayerOneLabelPanel");
        _CachedPrototypeLabelInnerPanel = _CachedPrototypeLabelWrapperPanel.Find<dfPanel>("PanelEnsmallenerThatmakesDavesLifeHardandBrentsLifeEasy");
        _CachedPrototypeLabel = _CachedPrototypeLabelInnerPanel.Find<dfLabel>("Label");
    }

    private static void PrintControlRecursive(dfControl control, string indent = "->", bool dissect = false)
    {
        System.Console.WriteLine($"  {indent} control with name={control.name}, type={control.GetType()}, position={control.Position}, relposition={control.RelativePosition}, size={control.Size}, anchor={control.Anchor}, pivot={control.Pivot}");
        if (dissect)
            Dissect.DumpFieldsAndProperties(control);
        foreach (dfControl child in control.controls)
            PrintControlRecursive(child, "--" + indent);
    }

    private static T CopyAttributes<T>(this T self, T other) where T : dfControl
    {
        if (self is dfButton button && other is dfButton otherButton)
        {
            button.Atlas = otherButton.Atlas;
            button.ClickWhenSpacePressed = otherButton.ClickWhenSpacePressed;
            button.State = otherButton.State;
            button.PressedSprite = otherButton.PressedSprite;
            button.ButtonGroup = otherButton.ButtonGroup;
            button.AutoSize = otherButton.AutoSize;
            button.TextAlignment = otherButton.TextAlignment;
            button.VerticalAlignment = otherButton.VerticalAlignment;
            button.Padding = otherButton.Padding;
            button.Font = otherButton.Font;
            button.Text = otherButton.Text;
            button.TextColor = otherButton.TextColor;
            button.HoverTextColor = otherButton.HoverTextColor;
            button.NormalBackgroundColor = otherButton.NormalBackgroundColor;
            button.HoverBackgroundColor = otherButton.HoverBackgroundColor;
            button.PressedTextColor = otherButton.PressedTextColor;
            button.PressedBackgroundColor = otherButton.PressedBackgroundColor;
            button.FocusTextColor = otherButton.FocusTextColor;
            button.FocusBackgroundColor = otherButton.FocusBackgroundColor;
            button.DisabledTextColor = otherButton.DisabledTextColor;
            button.TextScale = otherButton.TextScale;
            button.TextScaleMode = otherButton.TextScaleMode;
            button.WordWrap = otherButton.WordWrap;
            button.Shadow = otherButton.Shadow;
            button.ShadowColor = otherButton.ShadowColor;
            button.ShadowOffset = otherButton.ShadowOffset;
        }
        if (self is dfPanel panel && other is dfPanel otherPanel)
        {
            panel.Atlas = otherPanel.Atlas;
            panel.BackgroundSprite = otherPanel.BackgroundSprite;
            panel.BackgroundColor = otherPanel.BackgroundColor;
            panel.Padding = otherPanel.Padding;
        }
        // TODO: this probably needs to be set up manually
        if (self is dfCheckbox checkbox && other is dfCheckbox otherCheckbox)
        {
            // IsChecked
            // CheckIcon
            // Label
            // GroupContainer
        }
        if (self is dfSprite sprite && other is dfSprite otherSprite)
        {
            sprite.Atlas = otherSprite.Atlas;
            sprite.SpriteName = otherSprite.SpriteName;
            sprite.Flip = otherSprite.Flip;
            sprite.FillDirection = otherSprite.FillDirection;
            sprite.FillAmount = otherSprite.FillAmount;
            sprite.InvertFill = otherSprite.InvertFill;
        }
        if (self is dfLabel label && other is dfLabel otherLabel)
        {
            label.Atlas = otherLabel.Atlas;
            label.Font = otherLabel.Font;
            label.BackgroundSprite = otherLabel.BackgroundSprite;
            label.BackgroundColor = otherLabel.BackgroundColor;
            label.TextScale = otherLabel.TextScale;
            label.TextScaleMode = otherLabel.TextScaleMode;
            label.CharacterSpacing = otherLabel.CharacterSpacing;
            label.ColorizeSymbols = otherLabel.ColorizeSymbols;
            label.ProcessMarkup = true; // always want this to be true
            label.ShowGradient = otherLabel.ShowGradient;
            label.BottomColor = otherLabel.BottomColor;
            label.Text = otherLabel.Text;
            label.AutoSize = otherLabel.AutoSize;
            label.AutoHeight = otherLabel.AutoHeight;
            label.WordWrap = otherLabel.WordWrap;
            label.TextAlignment = otherLabel.TextAlignment;
            label.VerticalAlignment = otherLabel.VerticalAlignment;
            label.Outline = otherLabel.Outline;
            label.OutlineSize = otherLabel.OutlineSize;
            label.OutlineColor = otherLabel.OutlineColor;
            label.Shadow = otherLabel.Shadow;
            label.ShadowColor = otherLabel.ShadowColor;
            label.ShadowOffset = otherLabel.ShadowOffset;
            label.Padding = otherLabel.Padding;
            label.TabSize = otherLabel.TabSize;
        }
        if (self is dfScrollbar scrollbar && other is dfScrollbar otherScrollbar)
        {
            scrollbar.ControlledByRightStick = otherScrollbar.ControlledByRightStick;
            scrollbar.atlas = otherScrollbar.atlas;
            scrollbar.orientation = otherScrollbar.orientation;
            scrollbar.rawValue = otherScrollbar.rawValue;
            scrollbar.minValue = otherScrollbar.minValue;
            scrollbar.maxValue = otherScrollbar.maxValue;
            scrollbar.stepSize = otherScrollbar.stepSize;
            scrollbar.scrollSize = otherScrollbar.scrollSize;
            scrollbar.increment = otherScrollbar.increment;
            scrollbar.thumb = scrollbar.AddControl<dfSprite>().CopyAttributes(otherScrollbar.thumb as dfSprite);
            scrollbar.track = scrollbar.AddControl<dfSprite>().CopyAttributes(otherScrollbar.track as dfSprite);
            scrollbar.incButton = otherScrollbar.incButton;
            scrollbar.decButton = otherScrollbar.decButton;
            scrollbar.thumbPadding = otherScrollbar.thumbPadding;
            scrollbar.autoHide = otherScrollbar.autoHide;
        }
        self.AllowSignalEvents = other.AllowSignalEvents;
        self.MinimumSize = other.MinimumSize;
        self.MaximumSize = other.MaximumSize;
        // self.ZOrder            = other.ZOrder;  // don't set this or children won't be processed in the order we add them
        self.TabIndex = other.TabIndex;
        self.IsInteractive = other.IsInteractive;
        self.Pivot = other.Pivot;
        // self.Position          = other.Position;  // not sure this actually matters, as long as we set RelativePosition
        self.RelativePosition = other.RelativePosition;
        self.HotZoneScale = other.HotZoneScale;
        self.useGUILayout = other.useGUILayout;
        self.Color = other.Color;
        self.DisabledColor = other.DisabledColor;
        self.Anchor = other.Anchor;
        self.CanFocus = other.CanFocus;
        self.AutoFocus = other.AutoFocus;
        self.Size = other.Size;
        self.Opacity = other.Opacity;
        self.enabled = other.enabled;

        self.renderOrder = other.renderOrder;
        self.isControlClipped = other.isControlClipped;

        return self;
    }

    private static void CreateGunfigButton(this PreOptionsMenuController preOptions)
    {
        _GunfigMainPanel = null; // nuke the old main panel and force rebuild it later when requested

        dfPanel panel = preOptions.m_panel;
        dfButton prevButton = panel.Find<dfButton>("AudioTab (1)");
        dfControl nextButton = prevButton.GetComponent<UIKeyControls>().down;

        // Get a list of all buttons in the menu
        List<dfButton> buttonsInMenu = new();
        foreach (dfControl control in panel.Controls)
            if (control is dfButton button)
                buttonsInMenu.Add(button);

        // Sort them from top to bottom and compute the new gap needed after adding a new button
        buttonsInMenu.Sort((a, b) => (a.Position.y < b.Position.y) ? 1 : -1);
        float minY = buttonsInMenu.First().Position.y;
        float maxY = buttonsInMenu.Last().Position.y;
        float gap = (maxY - minY) / buttonsInMenu.Count;

        // Shift the buttons up to make room for the new button
        for (int i = 0; i < buttonsInMenu.Count; ++i)
            buttonsInMenu[i].Position = buttonsInMenu[i].Position.WithY(minY + gap * i);

        // Add the new button to the list
        dfButton newButton = panel.AddControl<dfButton>().CopyAttributes(prevButton);
        newButton.Text = _MOD_MENU_LABEL;
        newButton.name = _MOD_MENU_LABEL;
        newButton.Position = newButton.Position.WithY(maxY);  // Add it to the original position of the final button
        newButton.Click += OpenGunfigMainMenu;
        newButton.MouseEnter += FocusControl;
        newButton.GotFocus += PlayMenuCursorSound;

        // Add it to the UI
        UIKeyControls uikeys = newButton.gameObject.AddComponent<UIKeyControls>();
        uikeys.button = newButton;
        uikeys.selectOnAction = true;
        uikeys.clearRepeatingOnSelect = true;
        uikeys.up = prevButton;
        uikeys.down = nextButton;
        prevButton.GetComponent<UIKeyControls>().down = newButton;
        nextButton.GetComponent<UIKeyControls>().up = newButton;

        // Adjust the layout
        newButton.PerformLayout();
        panel.PerformLayout();
    }

    private static void OpenGunfigMainMenu(dfControl control, dfMouseEventArgs args)
    {
        _MenuStack.Clear();
        RegenerateGunfigMainPanel();
        OpenSubMenu(_GunfigMainPanel);
    }

    private static void RegenerateGunfigMainPanel()
    {
        if (_GunfigMainPanel != null)
            return;
        // Cache all the controls we'll be copying for faster access (needs to be done every time panels are rebuilt each run)
        ReCacheControlsAndTabs();
        // Add submenus for each active mod
        System.Diagnostics.Stopwatch allmodsWatch = System.Diagnostics.Stopwatch.StartNew();
        _GunfigMainPanel = NewOptionsPanel(_MOD_MENU_TITLE);
        foreach (Gunfig gunfig in Gunfig._ActiveConfigs)
        {
            gunfig.RegenConfigPage().Finalize();
            if (gunfig._BaseGunfig == gunfig) // if we are a top level Gunfig, add directly to the MOD OPTIONS menu
                _GunfigMainPanel.AddButton(label: gunfig._modName).gameObject.AddComponent<GunfigOption>().Setup(
                  parentConfig: gunfig, key: null, values: Gunfig._DefaultValues,
                  updateType: Gunfig.Update.Immediate, update: gunfig.OpenConfigPage);
        }
        // Finalize the options panel
        _GunfigMainPanel.Finalize();
        allmodsWatch.Stop(); GunfigDebug.Log($"  Options panels built in {allmodsWatch.ElapsedMilliseconds} milliseconds");
    }

    internal static dfScrollPanel NewOptionsPanel(string name)
    {
        FullOptionsMenuController optionsMenu = GameUIRoot.Instance.PauseMenuPanel.GetComponent<PauseMenuController>().OptionsMenu;

        // Get a reference options panel
        _RefPanel ??= ResourceManager.LoadAssetBundle("shared_auto_001")
          .LoadAsset<GameObject>("UI Root")
          .GetComponentInChildren<FullOptionsMenuController>()
          .TabVideo;
        dfScrollPanel refPanel = _RefPanel;

        // Add our options panel to the PauseMenuController and copy some basic attributes from our reference
        dfScrollPanel newPanel = optionsMenu.m_panel.AddControl<dfScrollPanel>();
        newPanel.SuspendLayout();
        newPanel.UseScrollMomentum = refPanel.UseScrollMomentum;
        newPanel.ScrollWithArrowKeys = refPanel.ScrollWithArrowKeys;
        newPanel.Atlas = refPanel.Atlas;
        newPanel.BackgroundSprite = refPanel.BackgroundSprite;
        newPanel.BackgroundColor = refPanel.BackgroundColor;
        newPanel.ScrollPadding = refPanel.ScrollPadding;
        newPanel.WrapLayout = refPanel.WrapLayout;
        newPanel.FlowDirection = refPanel.FlowDirection;
        newPanel.FlowPadding = refPanel.FlowPadding;
        newPanel.ScrollPosition = refPanel.ScrollPosition;
        newPanel.ScrollWheelAmount = refPanel.ScrollWheelAmount;
        newPanel.HorzScrollbar = refPanel.HorzScrollbar;
        newPanel.VertScrollbar = optionsMenu.TabVideo.VertScrollbar; //HACK: creating our own scrollbar makes it invisible because...idk...painful to fight
        newPanel.WheelScrollDirection = refPanel.WheelScrollDirection;
        newPanel.UseVirtualScrolling = refPanel.UseVirtualScrolling;
        newPanel.VirtualScrollingTile = refPanel.VirtualScrollingTile;
        newPanel.CanFocus = refPanel.CanFocus;
        newPanel.AllowSignalEvents = refPanel.AllowSignalEvents;
        newPanel.IsEnabled = refPanel.IsEnabled;
        newPanel.IsVisible = refPanel.IsVisible;
        newPanel.IsInteractive = refPanel.IsInteractive;
        newPanel.Tooltip = refPanel.Tooltip;
        newPanel.Anchor = refPanel.Anchor;
        newPanel.Opacity = refPanel.Opacity;
        newPanel.Color = refPanel.Color;
        newPanel.DisabledColor = refPanel.DisabledColor;
        newPanel.Pivot = refPanel.Pivot;
        // newPanel.Size                 = refPanel.Size;  // don't set size manually since we want to clip
        newPanel.Width = refPanel.Width;
        newPanel.Height = refPanel.Height;
        newPanel.MinimumSize = refPanel.MinimumSize;
        newPanel.MaximumSize = refPanel.MaximumSize;
        // newPanel.ZOrder               = refPanel.ZOrder; // don't set this or children won't be processed in the order we add them
        newPanel.TabIndex = refPanel.TabIndex;
        newPanel.ClipChildren = refPanel.ClipChildren;
        newPanel.InverseClipChildren = refPanel.InverseClipChildren;
        // newPanel.Tag               = refPanel.Tag;
        newPanel.IsLocalized = refPanel.IsLocalized;
        newPanel.HotZoneScale = refPanel.HotZoneScale;
        newPanel.useGUILayout = refPanel.useGUILayout;
        newPanel.AutoFocus = refPanel.AutoFocus;
        newPanel.AutoLayout = refPanel.AutoLayout;
        newPanel.AutoReset = refPanel.AutoReset;
        newPanel.AutoScrollPadding = refPanel.AutoScrollPadding;
        newPanel.AutoFitVirtualTiles = refPanel.AutoFitVirtualTiles;

        // Set up a few additional variables to suit our needs
        newPanel.ClipChildren = true;
        newPanel.InverseClipChildren = true;
        newPanel.ScrollPadding = new RectOffset(0, 0, 0, 0);
        newPanel.AutoScrollPadding = new RectOffset(0, 0, 0, 0);

        newPanel.Size -= new Vector2(0, 50f);  //TODO: figure out why this offset is wrong in the first place
        newPanel.ResumeLayout();
        newPanel.Position = refPanel.Position.WithY(270f);  //TODO: figure out why this offset is wrong in the first place

        newPanel.name = name;
        newPanel.PerformLayout();
        newPanel.Enable();  // necessary to make sure our children are enabled when the panel is first loaded

        // Add it to our known panels so we can make visible / invisible as necessary
        _RegisteredTabs.Add(newPanel);

        return newPanel;
    }

    // based on V-SyncCheckBoxPanel
    internal static dfPanel AddCheckBox(this dfScrollPanel panel, string label, PropertyChangedEventHandler<bool> onchange = null)
    {
        dfPanel newCheckboxWrapperPanel = panel.AddControl<dfPanel>().CopyAttributes(_CachedPrototypeCheckboxWrapperPanel);
        dfPanel newCheckboxInnerPanel = newCheckboxWrapperPanel.AddControl<dfPanel>().CopyAttributes(_CachedPrototypeCheckboxInnerPanel);
        dfCheckbox newCheckbox = newCheckboxInnerPanel.AddControl<dfCheckbox>().CopyAttributes(_CachedPrototypeCheckbox);
        dfSprite newEmptyCheckboxSprite = newCheckbox.AddControl<dfSprite>().CopyAttributes(_CachedPrototypeEmptyCheckboxSprite);
        dfSprite newCheckedCheckboxSprite = newCheckbox.AddControl<dfSprite>().CopyAttributes(_CachedPrototypeCheckedCheckboxSprite);
        dfLabel newCheckboxLabel = newCheckboxInnerPanel.AddControl<dfLabel>().CopyAttributes(_CachedPrototypeCheckboxLabel);

        newCheckboxLabel.Text = label;

        BraveOptionsMenuItem menuItem = newCheckboxWrapperPanel.gameObject.AddComponent<BraveOptionsMenuItem>();
        menuItem.optionType = BraveOptionsMenuItem.BraveOptionsOptionType.NONE;
        menuItem.itemType = BraveOptionsMenuItem.BraveOptionsMenuItemType.Checkbox;
        menuItem.labelControl = newCheckboxLabel;
        menuItem.checkboxChecked = newCheckedCheckboxSprite;
        menuItem.checkboxUnchecked = newEmptyCheckboxSprite;
        menuItem.selectOnAction = true;

        menuItem.checkboxChecked.IsVisible = menuItem.m_selectedIndex == 1;

        newCheckboxWrapperPanel.MouseEnter += FocusControl;
        newCheckboxWrapperPanel.GotFocus += PlayMenuCursorSound;
        newCheckboxWrapperPanel.name = $"{label} panel";
        panel.RegisterBraveMenuItem(newCheckboxWrapperPanel);
        if (onchange != null)
            menuItem.gameObject.GetOrAddComponent<CustomCheckboxHandler>().onChanged += onchange;
        return newCheckboxWrapperPanel;
    }

    // based on VisualPresetArrowSelectorPanel (without info) and ResolutionArrowSelectorPanelWithInfoBox (with info)
    internal static dfPanel AddArrowBox(this dfScrollPanel panel, string label, List<string> options, List<string> info = null, PropertyChangedEventHandler<string> onchange = null, bool compact = true, string defaultValue = null)
    {
        bool hasInfo = (info != null && info.Count > 0 && info.Count == options.Count);

        dfPanel newArrowboxWrapperPanel = panel.AddControl<dfPanel>().CopyAttributes(hasInfo ? _CachedPrototypeInfoWrapperPanel : _CachedPrototypeLeftRightWrapperPanel);
        dfPanel newArrowboxInnerPanel = newArrowboxWrapperPanel.AddControl<dfPanel>().CopyAttributes(hasInfo ? _CachedPrototypeInfoInnerPanel : _CachedPrototypeLeftRightInnerPanel);
        dfLabel newArrowSelectorLabel = newArrowboxInnerPanel.AddControl<dfLabel>().CopyAttributes(hasInfo ? _CachedPrototypeInfoPanelLabel : _CachedPrototypeLeftRightPanelLabel);
        dfLabel newArrowSelectorSelection = newArrowboxInnerPanel.AddControl<dfLabel>().CopyAttributes(hasInfo ? _CachedPrototypeInfoPanelSelection : _CachedPrototypeLeftRightPanelSelection);
        dfSprite newArrowLeftSprite = newArrowboxInnerPanel.AddControl<dfSprite>().CopyAttributes(hasInfo ? _CachedPrototypeInfoPanelLeftSprite : _CachedPrototypeLeftRightPanelLeftSprite);
        dfSprite newArrowRightSprite = newArrowboxInnerPanel.AddControl<dfSprite>().CopyAttributes(hasInfo ? _CachedPrototypeInfoPanelRightSprite : _CachedPrototypeLeftRightPanelRightSprite);
        dfLabel newArrowInfoLabel = hasInfo ? newArrowboxWrapperPanel.AddControl<dfLabel>().CopyAttributes(_CachedPrototypeInfoInfoPanel) : null;

        // NOTE: fixes a weird bug where the position offset if visiting the video menu first
        if (newArrowInfoLabel != null)
            newArrowInfoLabel.RelativePosition = newArrowInfoLabel.RelativePosition.WithY(87f); // vanilla offset

        newArrowSelectorLabel.Text = label;
        newArrowSelectorSelection.Text = options[0];
        if (newArrowInfoLabel != null)
            newArrowInfoLabel.Text = info[0];

        if (compact)
        {
            if (hasInfo)
            {
                int maxLines = 1;
                foreach (string line in info)
                    maxLines = Mathf.Max(maxLines, line.Split('\n').Length);
                newArrowboxWrapperPanel.Size -= new Vector2(0, 66f - 22f * maxLines);  // NOTE: don't shrink it too small or scrolling gets very messed up
            }
            else
                newArrowboxWrapperPanel.Size -= new Vector2(0, 8f);  // NOTE: don't shrink it too small or scrolling gets very messed up
        }

        BraveOptionsMenuItem menuItem = newArrowboxWrapperPanel.gameObject.AddComponent<BraveOptionsMenuItem>();
        menuItem.optionType = BraveOptionsMenuItem.BraveOptionsOptionType.NONE;
        menuItem.itemType = hasInfo ? BraveOptionsMenuItem.BraveOptionsMenuItemType.LeftRightArrowInfo : BraveOptionsMenuItem.BraveOptionsMenuItemType.LeftRightArrow;
        menuItem.labelControl = newArrowSelectorLabel;
        menuItem.selectedLabelControl = newArrowSelectorSelection;
        menuItem.infoControl = newArrowInfoLabel;
        menuItem.labelOptions = options.ToArray();
        menuItem.infoOptions = hasInfo ? info.ToArray() : null;
        menuItem.left = newArrowLeftSprite;
        menuItem.right = newArrowRightSprite;
        menuItem.selectOnAction = true;

        newArrowboxWrapperPanel.MouseEnter += FocusControl;
        newArrowboxWrapperPanel.GotFocus += PlayMenuCursorSound;
        newArrowboxWrapperPanel.name = $"{label} panel";
        panel.RegisterBraveMenuItem(newArrowboxWrapperPanel);
        if (onchange != null)
            menuItem.gameObject.GetOrAddComponent<CustomLeftRightArrowHandler>().onChanged += onchange;
        return newArrowboxWrapperPanel;
    }

    // based on EditKeyboardBindingsButtonPanel
    internal static dfPanel AddButton(this dfScrollPanel panel, string label, Action<dfControl> onclick = null)
    {
        dfPanel newButtonWrapperPanel = panel.AddControl<dfPanel>().CopyAttributes(_CachedPrototypeButtonWrapperPanel);
        dfPanel newButtonInnerPanel = newButtonWrapperPanel.AddControl<dfPanel>().CopyAttributes(_CachedPrototypeButtonInnerPanel);
        dfButton newButton = newButtonInnerPanel.AddControl<dfButton>().CopyAttributes(_CachedPrototypeButton);

        newButton.Text = label;

        BraveOptionsMenuItem menuItem = newButtonWrapperPanel.gameObject.AddComponent<BraveOptionsMenuItem>();
        menuItem.optionType = BraveOptionsMenuItem.BraveOptionsOptionType.NONE;
        menuItem.itemType = BraveOptionsMenuItem.BraveOptionsMenuItemType.Button;
        menuItem.buttonControl = newButton;
        menuItem.selectOnAction = true;

        newButtonWrapperPanel.MouseEnter += FocusControl;
        newButtonWrapperPanel.GotFocus += PlayMenuCursorSound;
        newButtonWrapperPanel.name = $"{label} panel";
        panel.RegisterBraveMenuItem(newButtonWrapperPanel);
        if (onclick != null)
            menuItem.gameObject.GetOrAddComponent<CustomButtonHandler>().onClicked += onclick;
        return newButtonWrapperPanel;
    }

    // based on PlayerOneLabelPanel
    internal static dfPanel AddLabel(this dfScrollPanel panel, string label, bool compact = true)
    {
        dfPanel newLabelWrapperPanel = panel.AddControl<dfPanel>().CopyAttributes(_CachedPrototypeLabelWrapperPanel);
        dfPanel newLabelInnerPanel = newLabelWrapperPanel.AddControl<dfPanel>().CopyAttributes(_CachedPrototypeLabelInnerPanel);
        dfLabel newLabel = newLabelInnerPanel.AddControl<dfLabel>().CopyAttributes(_CachedPrototypeLabel);

        Color color;
        newLabel.Text = label.ProcessColors(out color);
        newLabel.Color = color;

        if (compact)
        {
            newLabelWrapperPanel.Size -= new Vector2(0, 56f);
            newLabelInnerPanel.Position += new Vector3(0, 56f, 0);
        }

        newLabelWrapperPanel.name = $"{label} panel";

        return newLabelWrapperPanel;
    }

    private static void RegisterBraveMenuItem(this dfScrollPanel panel, dfControl item)
    {
        for (int prevItemIndex = panel.Controls.Count - 2; prevItemIndex >= 0; prevItemIndex--)
        {
            dfControl prevItem = panel.controls[prevItemIndex];
            if (prevItem.GetComponent<BraveOptionsMenuItem>() is not BraveOptionsMenuItem prevMenuItem)
                continue;
            item.GetComponent<BraveOptionsMenuItem>().up = prevItem;
            prevMenuItem.down = item;
            break;
        }
    }

    private static void RecursiveFocus(this dfControl control, bool isRoot = true)
    {
        control.canFocus = isRoot;
        control.AutoFocus = true;
        foreach (dfControl child in control.controls)
            child.RecursiveFocus(isRoot: false);
    }

    private static void Finalize(this dfScrollPanel panel)
    {
        panel.controls.Last().Height += 16f; // fix a weird clipping issue for arrowboxes at the bottom
        foreach (dfControl child in panel.controls)
        {
            if (child.GetComponent<BraveOptionsMenuItem>() is not BraveOptionsMenuItem menuItem)
                continue;
            if (child.GetComponent<GunfigOption>() is not GunfigOption option)
                continue;
            option.UpdateColors(menuItem, true); // make sure our colors our properly set on first load
        }
    }

    internal static void OpenSubMenu(dfScrollPanel panel)
    {
        if (_MenuStack.Count > 0)
            _MenuStack.Peek().IsVisible = false;
        _MenuStack.Push(panel);
        GameUIRoot.Instance.PauseMenuPanel.GetComponent<PauseMenuController>(
          ).OptionsMenu.PreOptionsMenu.ToggleToPanel(panel, val: true, force: true); // force true so it works even if it's invisible
                                                                                     //NOTE: for whatever reason, if a vanilla menu is opened first, the panel is opened at 100x scale...why ._.
        panel.transform.localScale = Vector3.one;

        // System.Console.WriteLine($"opening menu {panel.name}");
        // DumpRecursive(panel.gameObject);
    }

    private static void SetOptionsPageTitle(string title)
    {
        if (GameUIRoot.Instance.PauseMenuPanel.GetComponent<PauseMenuController>().OptionsMenu is not FullOptionsMenuController optionsMenu)
            return;
        Color color;
        dfLabel titleControl = optionsMenu.m_panel.Find<dfLabel>("Title");
        titleControl.Text = title.ProcessColors(out color);
        titleControl.Color = color;
    }

    [HarmonyPatch(typeof(FullOptionsMenuController), nameof(FullOptionsMenuController.UpAllLevels))]
    /// <summary>Allow backing out of modded menus one level at a time</summary>
    private class BackOneLevelPatch
    {
        static bool Prefix(FullOptionsMenuController __instance)
        {
            if (_MenuStack.Count == 0)
                return true;     // call the original method if we don't have anything in our menu stack
            _MenuStack.Pop().IsVisible = false; // hide the latest menu on the stack
            if (_MenuStack.Count == 0)
                return true;     // call the original method (no need to pop the main options menu modal since it handles that itself)
            __instance.cloneOptions = GameOptions.CloneOptions(GameManager.Options); // reset vanilla options to prevent vanilla error messages (maybe slow -> suppress later if needed)
            __instance.PreOptionsMenu.ToggleToPanel(_MenuStack.Peek(), val: true, force: true); // force true so it works even if it's invisible
            if (Foyer.DoMainMenu)
                __instance.ShwoopOpen(); //HACK: fixes weird scrolling issue when backing out of submenus from the title screen, but causes flickering
            return false; // we just want to go back one level, so skip the original method
        }
    }

    [HarmonyPatch(typeof(FullOptionsMenuController), nameof(FullOptionsMenuController.ToggleToPanel))]
    /// <summary>Fixes an off-by-one error (preventing single-item menus from navigating up and down properly) by replacing > with >=</summary>
    private class ToggleToPanelPatch
    {
        [HarmonyILManipulator]
        private static void ToggleToPanelIL(ILContext il)
        {
            ILCursor cursor = new ILCursor(il);
            ILLabel loopBranch = null;
            if (!cursor.TryGotoNext(MoveType.Before, instr => instr.MatchBgt(out loopBranch)))
                return;
            cursor.Remove();
            cursor.Emit(OpCodes.Bge, loopBranch);
            return;
        }

        private static void Postfix(FullOptionsMenuController __instance)
        {
            if (Foyer.DoMainMenu)
                __instance.ShwoopOpen(); //HACK: fixes weird scrolling issue when entering submenus from the title screen, but causes flickering
        }
    }
}
