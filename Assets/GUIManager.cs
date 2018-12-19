using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanyModule;
using DG.Tweening;
using LiveEventsModule;
using NG;
using Officer;
using PlayerUnitsModule;
using UniRx;
using UniRx.Triggers;
using UnityEngine;
using Object = UnityEngine.Object;
using ObjectPoolModule;
using TMPro;

public class GUIManager : BaseWindow<GUIManager>
{
    private static bool _sceneLoaded = false;

    public delegate void SetLoadingProgress(float percentage = 0, bool forceUpdate = false);

    public static event SetLoadingProgress OnSetLoadingScreenProgress;

    public static float UIScale { get; set; }
    public static float ReferenceWidth { get; set; }
    public static float ReferenceHeight { get; set; }
    public Camera RenderCamera;

    [Header("New Menu System")] public Transform MenusRoot;
    private readonly ReactiveCollection<BaseWindow> menuStack = new ReactiveCollection<BaseWindow>();
    private readonly ReactiveCollection<BaseWindow> dialogStack = new ReactiveCollection<BaseWindow>();
    private readonly HashSet<BaseWindow> nonStackedMenus = new HashSet<BaseWindow>();
    internal static ReactiveProperty<BaseWindow> TopmostDialog = new ReactiveProperty<BaseWindow>();
    internal static ReactiveProperty<BaseWindow> TopmostPanel = new ReactiveProperty<BaseWindow>();

    [Header("Panels")] public GameObject currentPanel;
    public GameObject lobbyPanel;
    public GameObject serverPanel;
    public GameObject indicatorPanel;

    public static ReactiveProperty<Panel> CurrentPanel = new ReactiveProperty<Panel>(Panel.None);
    public static ReactiveProperty<Panel> PreviousPanel = new ReactiveProperty<Panel>(Panel.None);

    public bool AllowedToGoToDifferentMenu = true;

    [Header("Indicators")] public string HealthBarPrefabName;
    public string HealthBarBuildingPrefabName;
    public string RangeIndicatorUnitPrefabName;
    public string RangeIndicatorCommandPrefabName;

    [Header("Players")] public HealthBar[] playerHealth;

    private readonly Dictionary<int, GameObject> tutorialHighlights = new Dictionary<int, GameObject>();
    private const string UiLoadingPath = "_1.6UI/";
    private readonly CompositeDisposable _compositeDisposable = new CompositeDisposable();

    [Header("Panel Easing")] public int AnimationDirection;
    public Ease PanelEaseTypeIn = Ease.Linear;
    public Ease PanelEaseTypeOut = Ease.Linear;

    [Header("Check Swipe Direction")] private float _swipeDistance = 300;
    private readonly float _swipeTresholdY = 50;
    public bool isSwiping = false;
    public bool canSwipe = false;
    public bool isAnimatingMenu = false;
    private Vector2 firstTouch;
    private Vector2 lastTouch;
    private int touchId = -1;

    [Header("Font For Arabic")] public TMP_FontAsset ArabicFont;

    private IDisposable menuTransition = null;

    public enum Panel
    {
        None = -1,
        MainMenu = 0,
        CampaignMenu = 1,
        Lobby = 2,
        InGame = 3,
        LeaderBoards = 7,
        ArmyPanel = 10,
        BattleResultsPanel = 21,
        ServerPanel = 24,
        ShopPanel = 33,
        SocialPanel = 49,
        TasksPanel = 51
    }

    private static readonly Camera _lobbyCamera = null;

    public static void SetLobbyCamera(bool enabled)
    {
        if (_lobbyCamera)
        {
            print("Lobby Camera set");
            _lobbyCamera.gameObject.SetActive(enabled);
        }
    }

    public static bool IsEighteenByNine
    {
        get
        {
            bool returnValue = false;

            float screenWidth = Screen.width;
            float screenHeight = Screen.height;

            float aspectRatio = screenWidth / screenHeight;

            if (aspectRatio < 0.56f)
            {
                returnValue = true;
            }

            return returnValue;
        }
    }

    private void Awake()
    {
    #if UNITY_EDITOR
        _swipeDistance = 100;
    #endif

        RegisterSingleton();
        this.Canvas = GetComponent<Canvas>();
        LoadingScreenOnDisable.Show();
        if (GameServer.IsServer)
        {
            ChangeToPanel(Panel.ServerPanel);
            ConnectingDialog.Hide();
            CollectDialogMenuPage.Hide();
            LoadingScreenOnDisable.Hide();
        }
        else
        {
            SetProfileLoadedSubscriptions();
        }

        this.UpdateAsObservable().Where(_ => Input.GetKeyDown(KeyCode.Escape)).Subscribe(_ => TopmostPageBack())
            .AddTo(this);

    #if UNITY_EDITOR
        Observable.EveryUpdate().Where(x => Input.GetMouseButtonDown(0)).Subscribe(_ =>
        {
            if (MatchManager.Instance != null || this.isSwiping)
            {
                return;
            }

            this.isSwiping = true;
            this.firstTouch = Input.mousePosition;
        }).AddTo(this);

        Observable.EveryUpdate().Subscribe(_ => { this.isSwiping = Input.GetMouseButton(0); }).AddTo(this);

        Observable.EveryUpdate().Where(x => Input.GetMouseButtonUp(0)).Subscribe(_ =>
        {
            this.lastTouch = Input.mousePosition;
            if (MatchManager.Instance == null && this.isSwiping)
            {
                HandleMainMenuSwipes();
            }
        }).AddTo(this);
    #else
        Observable.EveryUpdate().Where(x => Input.touchCount == 1).Subscribe(_ => { HandleTouches(); }).AddTo(this);
        
        #endif

        this.dialogStack.ObserveCountChanged().Subscribe(_ => { UpdateTopmostDialog(); }).AddTo(this);
        this.menuStack.ObserveCountChanged().Subscribe(_ => { UpdateTopmostPanel(); }).AddTo(this);
        CurrentPanel.Subscribe(_ =>
        {
            HandleTopMenu();
            HandleBottomMenu();
        }).AddTo(this);
    }

    public void SetProfileLoadedSubscriptions()
    {
        ProfileManager.Loaded.Where(x => x).Subscribe(_ => { InitialChangeToPanel(Panel.MainMenu); })
                      .AddTo(this._compositeDisposable);
        PlayerUnitsManager.playerUnitsLoaded.Where(x => x).Subscribe(_ => { InitialChangeToPanel(Panel.MainMenu); })
                          .AddTo(this._compositeDisposable);
        PlayerUnitsManager.playerBattleDeckLoaded.Where(x => x)
                          .Subscribe(_ => { InitialChangeToPanel(Panel.MainMenu); })
                          .AddTo(this._compositeDisposable);
    }

    public void HandleTouches()
    {
        Touch touch = Input.GetTouch(0); // get the touch
        if (this.touchId != touch.fingerId)
        {
            this.firstTouch = touch.position;
        }

        if (!CanSwipe())
        {           
            this.isSwiping = false;
            this.touchId = -1;
            return;
        }

        switch (touch.phase)
        {
            case TouchPhase.Began:
                this.firstTouch = touch.position;
                this.touchId = touch.fingerId;
                this.isSwiping = true;
                break;
            case TouchPhase.Moved:
                this.isSwiping = this.touchId == touch.fingerId;
                break;
            case TouchPhase.Ended:
            {
                this.lastTouch = touch.position;
                float deltaX = this.lastTouch.x - this.firstTouch.x;
                float deltaY = this.lastTouch.y - this.firstTouch.y;

                if (deltaY > this._swipeTresholdY || deltaY < -this._swipeTresholdY)
                {
                    return;
                }

                if (deltaX > this._swipeDistance || deltaX < -this._swipeDistance && this.isSwiping)
                {
                    BottomMenuPanel.Instance.PanelSwiped(deltaX > 0);
                }

                this.touchId = -1;
                break;
            }
            case TouchPhase.Stationary:
                this.isSwiping = false;
                break;
            case TouchPhase.Canceled:
                this.isSwiping = false;
                break;
        }
    }

    private void HandleMainMenuSwipes()
    {
        if (!CanSwipe())
        {
            return;
        }

        float deltaX = this.lastTouch.x - this.firstTouch.x;
        float deltaY = this.lastTouch.y - this.firstTouch.y;

        if (deltaY > this._swipeTresholdY || deltaY < -this._swipeTresholdY)
        {
            return;
        }

        if (deltaX > this._swipeDistance || deltaX < -this._swipeDistance)
        {
            BottomMenuPanel.Instance.PanelSwiped(deltaX > 0);
        }
    }

    public bool CanSwipe()
    {
        if (!this.canSwipe)
        {
            return false;
        }

        if (TutorialManager.Instance.IsInTutorial())
        {
            return false;
        }

        if (TasksMenu.Instance != null && TasksMenu.Instance.AdvertSwipeHandler.isSwipeDetected)
        {
            return false;
        }

        // We don't swipe if its not a accepted menu or there is a dialog active
        return this.dialogStack.Count <= 0 && CanSwipeMenu() && BottomMenuPanel.Instance != null;
    }

    private static bool CanSwipeMenu()
    {
        return CurrentPanel.Value == Panel.ArmyPanel || CurrentPanel.Value == Panel.MainMenu || CurrentPanel.Value == Panel.ShopPanel ||
               CurrentPanel.Value == Panel.TasksPanel || CurrentPanel.Value == Panel.SocialPanel;
    }

    private void UpdateTopmostDialog()
    {
        TopmostDialog.Value = this.dialogStack.LastOrDefault();
    }

    private void UpdateTopmostPanel()
    {
        TopmostPanel.Value = this.menuStack.LastOrDefault();
    }

    private void InitialChangeToPanel(Panel panel)
    {
        if (!PlayerUnitsManager.playerBattleDeckLoaded.Value || !PlayerUnitsManager.playerUnitsLoaded.Value ||
            !ProfileManager.Loaded.Value)
        {
            return;
        }

        ChangeToPanel(panel);
        ConnectingDialog.Hide();
        CollectDialogMenuPage.Hide();
        LoadingScreenOnDisable.Hide();
        this._compositeDisposable.Clear();

        TutorialManager.Instance.Setup(ProfileManager.m_tutorialCurrentState); // setup tutorial state
    }

    private void OnDestroy()
    {
        UnregisterSingleton();
    }

    private IEnumerator DisableLoadingScreenWithDelay()
    {
        // just some failsafe to make sure we don't hang here indefinitely no matter what
        float failSafeTimer = 0f;
        while (LobbyMainMenu.Instance == null || !LobbyMainMenu.Instance.Loaded)
        {
            failSafeTimer += Time.deltaTime;
            if (failSafeTimer > 3f)
            {
                break;
            }

            yield return null;
        }

        HideLoadingScreen();
    }

#region NPC Dialogue Methods

    internal void HighlightNPCDialogueCurrency(InGameCurrency highlightCurrency)
    {
        if (NPCDialogueDialog.Instance != null)
        {
            NPCDialogueDialog.Instance.HighlightCurrency(highlightCurrency);
        }
    }

    internal void ShowNPCDialogue(bool visible, string prompt = "")
    {
        ShowNPCDialoguePanel(visible);
        ShowNPCPrompt(prompt);
    }

    internal void ShowNPCDialogueMemo(bool visible)
    {
        if (NPCDialogueDialog.Instance != null)
        {
            NPCDialogueDialog.Instance.ShowMemo(visible);
        }
    }

    internal void ShowNPCDialogueThoughBubble(bool visible)
    {
        if (NPCDialogueDialog.Instance != null)
        {
            NPCDialogueDialog.Instance.ShowThoughBubble(visible);
        }
    }

    private void ShowNPCDialoguePanel(bool visible)
    {
        if (visible)
        {
            NPCDialogueDialog.Show();
        }
        else
        {
            NPCDialogueDialog.Hide();
        }
    }

    private void ShowNPCPrompt(string prompt)
    {
        if (NPCDialogueDialog.Instance != null)
        {
            NPCDialogueDialog.Instance.ShowPrompt(prompt);
        }
    }

#endregion

#region Sub Menu Methods

    /// <summary>
    /// Sets the state of the sub menu buttons in the bottom menu panel and top menu panel.
    /// Allows setting exceptions for bottom menu panels buttons.
    /// </summary>
    internal void SetSubMenuButtonsState(bool state, params Panel[] bottomMenuExceptions)
    {
        if (BottomMenuPanel.Instance != null)
        {
            BottomMenuPanel.Instance.SetButtonInteractibilites(state, bottomMenuExceptions);
        }

        if (TopMenu.Instance != null)
        {
            TopMenu.Instance.SetButtonInteractibilites(state);
        }
    }

#endregion

    public UniRx.IObservable<Unit> DisplayLoadingScreen(bool disableWhenMainMenuReady = false)
    {
        return Observable.Create<Unit>(observer =>
        {
            LoadingScreenOnDisable.Show();

            if (AudioManager.instance)
            {
                StartCoroutine(AudioFadeOut.FadeOutMusic(AudioManager.instance.musicSource, 0.5f,
                    AudioManager.instance.loadingSound));
            }

            if (disableWhenMainMenuReady)
            {
                var disableScreen = Observable.FromMicroCoroutine(DisableLoadingScreenWithDelay)
                                              .Subscribe();

                disableScreen.Dispose();
            }

            observer.OnCompleted();
            return null;
        });
    }

    public void HideLoadingScreen()
    {
        StartCoroutine(AudioFadeOut.FadeOutMusic(AudioManager.instance.musicSource, 0.5f,
            AudioManager.instance.menuMusic));

        HandlePvPInterruption();
    }

    public void ShowBannedScreen(string banDateEnd, string banReason)
    {
        BannedDialog.Show();

        BannedDialog.Instance.InitialiseLabels(banDateEnd, banReason);

        if (AudioManager.instance)
        {
            StartCoroutine(AudioFadeOut.FadeOutMusic(AudioManager.instance.musicSource, 0.5f,
                AudioManager.instance.loadingSound));
        }
    }

    public void HandlePvPInterruption()
    {
        var prevMatch = BattleInterruptionInfo.Restore(() => new BattleInterruptionInfo());

        // UI-related functions after killing app during match
        if (prevMatch.ScreenToShow() == Panel.MainMenu)
        {
            return;
        }

        //user's event progress not loading with event's information
        // so there is delay
        if (EventManager.IsChallengesActive && prevMatch.matchType == MatchCategory.LIVE_EVENTS)
        {
            EventManager.CurrentChallengesEvent.MatchResult(false);
        }

        //Will show defeat screen if player killed the app during the match - calculate the lost medals at this point
        ProfileManager.m_LastResultMedals = ProfileManager.PlayerMedals.Value - prevMatch.previousMedalAmount;
        ShowBattleOver(false, 1, "", prevMatch.opponentName, prevMatch.officerType, false, false, true);
        prevMatch.SerializeState(BattleInterruptionInfo.CompletionType.Banned);
    }

    public void ShowBattleOver(bool isRanked, int winner, string opponentId, string opponentName = "Opponent",
                               OfficerType officerType = OfficerType.KRUGER, bool isRematch = false,
                               bool enableEmotes = false, bool showBackground = false)
    {
        BattleResultsPanel.Show();
        BattleResultsPanel.Instance.Setup(isRanked, winner, opponentId, opponentName, officerType, isRematch,
            enableEmotes, showBackground);
    }

    public static void ShowPurchaseResultsDialog(bool success, InGameCurrency inGameCurrency)
    {
        if (Instance == null)
        {
            return;
        }

        PurchaseResultsDialog.Show(success);

        // Show VFX.
        if (!success)
        {
            return;
        }

        switch (inGameCurrency)
        {
            case InGameCurrency.GEMS:
                GemCollectionSequence.Show();
                break;
        }
    }

    public static void ShowVipBoughtDialog(bool success)
    {
        if (success)
        {
            VipBoughtCongratulationsPanelActivator.Show();
        }
        else
        {
            PurchaseResultsDialog.Show(success);
        }
    }

    public void ShowUpdateDialog(string storeUrl)
    {
        GenericDialog.Show(string.Empty, storeUrl);
    }

    public static void ShowNetworkErrorDialog(string message)
    {
        GenericDialog.Show(string.Empty, message, string.Empty, true);
    }

    public void ShowNotEnoughtGoldDialog(string message)
    {
        GenericDialog.Show(string.Empty, message, string.Empty, false);
    }

    public void ShowNoDiscountCardsDialog(string message)
    {
        GenericDialog.Show(string.Empty, message, string.Empty, false);
    }

    public static void ShowServerMessage(string message, string url)
    {
        GenericDialog.Show(string.Empty, message, url, false);
    }


    internal void ShowBottomMenu(bool visible)
    {
        if (visible)
        {
            BottomMenuPanel.Show();
            BottomMenuPanel.Instance.SetButtonInteractibilites(true);
        }
        else
        {
            BottomMenuPanel.Hide();
        }
    }

    internal void ShowTopMenu(bool visible)
    {
        if (visible)
        {
            TopMenu.Show();
            TopMenu.Instance.SetButtonInteractibilites(true);
        }
        else
        {
            TopMenu.Hide();
        }
    }

    internal void ShowCancelMatchMakingContent(bool visible)
    {
        if (ConnectingDialog.Instance == null)
        {
            return;
        }

        ConnectingDialog.Instance.ShowCancelMatchMakingButton(visible);
    }

    public bool IsBottomMenuPanel(Panel panel)
    {
        switch (panel)
        {
            case Panel.LeaderBoards: return true;
            case Panel.ArmyPanel: return true;
            case Panel.ShopPanel: return true;
            case Panel.MainMenu: return true;
            case Panel.SocialPanel: return true;
        }

        return false;
    }

    public void ChangeToPanel(Panel _panel)
    {
        if (GameServer.IsServer)
        {
            return;
        }

        // if going to top lists or class screen for the first time after seasons of war update, show the info dialog
        if (!ProfileManager.m_SeasonsOfWarInfoDialogShown &&
            _panel == Panel.LeaderBoards)
        {
            SeasonsOfWarInfoDialog.Show();
            return;
        }

        if (TutorialManager.Instance != null && TutorialManager.Instance.IsInTutorial())
        {
            if (TutorialManager.Instance.IsWaitingForCampaignMenu && _panel == Panel.CampaignMenu &&
                LobbyMainMenu.Instance != null)
            {
                LobbyMainMenu.Instance.TutorialSetCampaignMenuButtonInteractableOnly(false);
            }
        }

        //Change the bottom menu
        if (IsBottomMenuPanel(_panel) && BottomMenuPanel.Instance != null)
        {
            BottomMenuPanel.Instance.SetItemActive(_panel);
        }

        PreviousPanel.Value = CurrentPanel.Value;
        CurrentPanel.Value = _panel;

        this.menuTransition = HidePreviousPanel().DoOnCompleted(ActivateChosenPanel).Subscribe().AddTo(this);
    }

    private UniRx.IObservable<Unit> HidePreviousPanel()
    {
        return Observable.Create<Unit>(observer =>
        {
            if (PreviousPanel.Value == CurrentPanel.Value)
            {
                observer.OnCompleted();
                return null;
            }

            switch (PreviousPanel.Value)
            {
                case Panel.MainMenu:
                    LobbyMainMenu.Hide();
                    break;
                case Panel.CampaignMenu:
                    CampaignMenu.Hide();
                    break;
                case Panel.Lobby:
                    this.lobbyPanel.SetActive(false);
                    break;
                case Panel.InGame:
                    BattleHUD.Hide();
                    BattleResultsPanel.Hide();
                    break;
                case Panel.LeaderBoards:
                    Leaderboard.Hide();
                    break;
                case Panel.ArmyPanel:
                    CardCollectionMenu.Hide();
                    break;
                case Panel.ShopPanel:
                    IAPPanel.Hide();
                    break;
                case Panel.TasksPanel:
                    TasksMenu.Hide();
                    break;
                case Panel.ServerPanel:
                    this.serverPanel.SetActive(false);
                    break;
                case Panel.SocialPanel:
                    SocialPanel.Hide();
                    break;
                case Panel.BattleResultsPanel:
                    BattleResultsPanel.Hide();
                    break;
                default:
                    Debug.LogError("Unsupported panel: " + PreviousPanel);
                    break;
            }

            observer.OnCompleted();
            return null;
        });
    }

    public void HandlePanels(Panel currentPanel)
    {
        switch (currentPanel)
        {
            case Panel.MainMenu:
                LobbyMainMenu.Show();
                break;
            case Panel.CampaignMenu:
                CampaignMenu.Show();
                break;
            case Panel.InGame:
                ShowTopMenu(false);
                ShowBottomMenu(false);
                BattleHUD.Show();
                return;
            case Panel.LeaderBoards:
                Leaderboard.Show();
                break;
            case Panel.ArmyPanel:
                CardCollectionMenu.Show();
                break;
            case Panel.TasksPanel:
                TasksMenu.Show();
                break;
            case Panel.ShopPanel:
                IAPPanel.Show();
                break;
            case Panel.SocialPanel:
                SocialPanel.Show();
                break;
            default:
                LobbyMainMenu.Show();
                break;
        }
    }

    public void ActivateChosenPanel()
    {
        if (!GameServer.IsServer)
        {
            ToolKit.SetActive(this.lobbyPanel, false);
            ToolKit.SetActive(this.serverPanel, false);
        }

        HandlePanels(CurrentPanel.Value);
    }

#region UpdatedMenu

    private void HandleTopMenu()
    {
        if (CurrentPanel.Value == Panel.MainMenu
            || CurrentPanel.Value == Panel.CampaignMenu
            || CurrentPanel.Value == Panel.LeaderBoards
            || CurrentPanel.Value == Panel.ArmyPanel
            || CurrentPanel.Value == Panel.ShopPanel
            || CurrentPanel.Value == Panel.SocialPanel
            || CurrentPanel.Value == Panel.TasksPanel)
        {
            ShowTopMenu(true);
        }
        else
        {
            ShowTopMenu(false);
        }
    }

    private void HandleBottomMenu()
    {
        if (CurrentPanel.Value == Panel.MainMenu
            || CurrentPanel.Value == Panel.LeaderBoards
            || CurrentPanel.Value == Panel.ArmyPanel
            || CurrentPanel.Value == Panel.ShopPanel
            || CurrentPanel.Value == Panel.SocialPanel
            || CurrentPanel.Value == Panel.TasksPanel)
        {
            ShowBottomMenu(true);
        }
        else
        {
            ShowBottomMenu(false);
        }
    }

    public GameObject GetPrefab<T>() where T : BaseWindow
    {
        Type type = typeof(T);

        // Create instance from prefab
        GameObject prefab = Resources.Load<GameObject>(UiLoadingPath + type.Name);

        if (prefab == null)
        {
            print("Error loading: " + type.Name + ", please check prefab name and location in " + UiLoadingPath);
        }

        return prefab;
    }

    public T Instantiate<T>() where T : BaseWindow
    {
        var type = typeof(T);
        var instantiated = Instantiate(GetPrefab<T>(), this.MenusRoot);
        var returnedObject = instantiated.GetComponent<T>();
        if (!returnedObject.Overlay)
        {
            this.currentPanel = instantiated;
        }

        instantiated.gameObject.name = type.Name;
        return returnedObject;
    }

    public void AddBaseWindow(BaseWindow baseWindow)
    {
        if (baseWindow.PushToStack)
        {
            PushToStack(baseWindow);
        }
        else
        {
            this.nonStackedMenus.Add(baseWindow);
        }
    }

    public void RemoveBaseWindow(BaseWindow baseWindow)
    {
        if (baseWindow.PushToStack)
        {
            PopFromStack(baseWindow);
        }
        else
        {
            this.nonStackedMenus.Remove(baseWindow);
        }
    }

    private void PushToStack(BaseWindow baseWindow)
    {
        if (baseWindow.Overlay)
        {
            if (this.dialogStack.Contains(baseWindow))
            {
                PopFromStack(baseWindow);
            }

            int sortingOrder = baseWindow.SortingOrder;

            if (TopmostDialog.Value != null)
            {
                sortingOrder = TopmostDialog.Value.SortingOrder;
            }

            baseWindow.SortingOrder = GetNextOverlaySortingOrder(sortingOrder);

            this.dialogStack.Add(baseWindow);
        }
        else
        {
            if (this.menuStack.Contains(baseWindow))
            {
                PopFromStack(baseWindow);
            }

            baseWindow.SortingOrder = GetNextFrontSortingOrder();

            this.menuStack.Add(baseWindow);
        }
    }

    private int GetNextOverlaySortingOrder(int defaultOrder)
    {
        return defaultOrder + this.dialogStack.Count + 1;
    }

    private int GetNextFrontSortingOrder()
    {
        return this.menuStack.Count + 1;
    }

    private void PopFromStack(BaseWindow baseWindow)
    {
        if (baseWindow.Overlay)
        {
            this.dialogStack.Remove(baseWindow);
        }
        else
        {
            this.menuStack.Remove(baseWindow);
        }
    }

    public bool IsInStack(BaseWindow baseWindow)
    {
        return this.menuStack.Contains(baseWindow);
    }

    public bool TopmostPageTypeMatches(BaseWindow baseWindow)
    {
        if (baseWindow == null)
        {
            return false;
        }

        if (this.dialogStack.Count > 0)
        {
            return this.dialogStack.Last().GetType() == baseWindow.GetType();
        }

        if (this.menuStack.Count > 0)
        {
            return this.menuStack.Last().GetType() == baseWindow.GetType();
        }

        if (this.nonStackedMenus.Count > 0)
        {
            return this.nonStackedMenus.Last().GetType() == baseWindow.GetType();
        }

        return false;
    }

    public void TopmostPageBack()
    {
        if (TopmostPageTypeMatches(ConnectingDialog.Instance))
        {
            if (GameServer.Instance == null || GameServer.Instance.m_MatchMaker == null)
            {
                return;
            }

            if (GameServer.Instance.m_MatchMaker.DedicatedStatus == MatchMaker.MatchMakerStatus.ConnectingToMaster)
            {
                ConnectingDialog.Instance.CancelMatchMaking();
            }

            return;
        }

        if (this.dialogStack.Count > 0 && !TopmostPageTypeMatches(ConnectingDialog.Instance))
        {
            this.dialogStack.Last().SendBackButtonClick();
            return;
        }

        if (this.menuStack.Count > 0)
        {
            this.menuStack.Last().SendBackButtonClick();
            return;
        }

        if (this.nonStackedMenus.Count > 0)
        {
            this.nonStackedMenus.Last().SendBackButtonClick();
        }
    }

#endregion

    public Indicator AddIndicator(Transform target, Vector3 offset, GameObject prefab = null)
    {
        if (!prefab)
        {
            return null;
        }

        GameObject obj = Instantiate(prefab);
        Indicator indicator = obj.GetComponent<Indicator>();
        if (!indicator)
        {
            Destroy(obj);
            return null;
        }

        Transform t = indicator.cachedTransform;
        t.SetParent(this.indicatorPanel.transform, false);

        indicator.target = target;

        return indicator;
    }

    public PooledObject AddHealthBar(Character character, Vector3 offset, string prefabName = null)
    {
        if (NGObjectPool.Instance == null)
        {
            return null;
        }

        string pName = string.IsNullOrEmpty(prefabName) ? this.HealthBarPrefabName : prefabName;

        PooledObject hBar = NGObjectPool.Instance.GetFromPool(PooledObjectType.INGAMEUI, pName);
        HealthBar bar = hBar.pHealthBar;

        bar.target = character.cachedTransform;
        bar.targetHealth = character;
        bar.targetOffset = offset;

        bar.SetColor(MatchManager.GetTeamColor(character.team));
        bar.SetCharacterType(character.characterType);
        bar.SetStars(character.characterRank);

        Transform t = bar.cachedTransform;

        t.SetParent(this.indicatorPanel.transform, false);
        t.ResetLocal();

        bar.HideBar();

        return hBar;
    }

    public PooledObject AddHealthBar(Building building, Vector3 offset, string prefabName = null)
    {
        if (NGObjectPool.Instance == null)
        {
            return null;
        }

        string pName = string.IsNullOrEmpty(prefabName) ? this.HealthBarBuildingPrefabName : prefabName;

        PooledObject hBar = NGObjectPool.Instance.GetFromPool(PooledObjectType.INGAMEUI, pName);
        HealthBar bar = hBar.pHealthBar;

        bar.target = building.transform;
        bar.targetHealth = building;
        bar.targetOffset = offset;
        bar.SetColor(MatchManager.GetTeamColor(building.team));

        Transform t = bar.cachedTransform;
        t.SetParent(this.indicatorPanel.transform, false);
        t.ResetLocal();

        bar.HideBar();

        return hBar;
    }

    public PooledObject AddUnitRangeIndicator3D(Transform target, Vector3 offset)
    {
        PooledObject pIndicator =
            NGObjectPool.Instance.GetFromPool(PooledObjectType.MISC, this.RangeIndicatorUnitPrefabName);

        if (pIndicator == null)
        {
            return null;
        }

        pIndicator.pRangeIndicator.targetWidget = target;
        pIndicator.pRangeIndicator.targetOffset = offset;

        Transform t = pIndicator.pRangeIndicator.cachedTransform;

        t.parent = RangeIndicator3D.GetRangeContainer();
        t.ResetLocal();

        pIndicator.pRangeIndicator.ShowBar();

        return pIndicator;
    }

    public PooledObject AddCommandRangeIndicator3D(Transform target, Vector3 offset)
    {
        PooledObject pIndicator =
            NGObjectPool.Instance.GetFromPool(PooledObjectType.MISC, this.RangeIndicatorCommandPrefabName);

        pIndicator.pRangeIndicator.targetWidget = target;
        pIndicator.pRangeIndicator.targetOffset = offset;

        Transform t = pIndicator.pRangeIndicator.cachedTransform;

        t.parent = RangeIndicator3D.GetRangeContainer();
        t.ResetLocal();

        pIndicator.pRangeIndicator.ShowBar();

        return pIndicator;
    }

    public void RateUpClicked()
    {
        AnalyticsEventSender.SendRateGameEvent(true);
        ActivateChosenPanel();
        FeedbackForm.GiveFeedback();
    }

    public void RateDownClicked()
    {
        AnalyticsEventSender.SendRateGameEvent(false);
        ActivateChosenPanel();
        FeedbackForm.GiveFeedback();
    }

    public GameObject AddTutorialHighlight(GameObject toHighlight, bool destroyOnDisable,
                                           eHighlightType type = eHighlightType.DEFAULT, int clickAmount = 0)
    {
        return AddTutorialHighlight(toHighlight, null, destroyOnDisable, type, clickAmount);
    }

    public GameObject AddTutorialHighlight(GameObject toHighlight, GameObject fromHighlight, bool destroyOnDisable,
                                           eHighlightType type = eHighlightType.DEFAULT, int clickAmount = 0,
                                           string extraSpriteName = "")
    {
        if (toHighlight == null)
        {
            return null;
        }

        if (this.tutorialHighlights.ContainsKey(toHighlight.GetInstanceID())) //don't highlight again
        {
            if (this.tutorialHighlights[toHighlight.GetInstanceID()])
            {
                Destroy(this.tutorialHighlights[toHighlight.GetInstanceID()]);
            }

            this.tutorialHighlights.Remove(toHighlight.GetInstanceID());
        }

        Object prefab = null;
        if (fromHighlight != null)
        {
            prefab = Resources.Load("GUI/Tutorial/TutorialHighlight_Pointer");
        }
        else
        {
            switch (type)
            {
                case eHighlightType.DEFAULT:
                default:
                    prefab = Resources.Load("GUI/Tutorial/TutorialHighlight_2DArrow");
                    break;
                case eHighlightType.VICTORY_POINT:
                    prefab = Resources.Load("GUI/Tutorial/TutorialHighlight_2DArrow02");
                    break;
                case eHighlightType.ENEMY_UNIT:
                    prefab = Resources.Load("GUI/Tutorial/TutorialHighlight_2DArrow03");
                    break;
                case eHighlightType.POINT_BAR:
                    prefab = Resources.Load("GUI/Tutorial/TutorialHighlight_Bar");
                    break;
            }
        }

        GameObject arrow = (GameObject) Instantiate(prefab);

        if (arrow == null)
        {
            return null;
        }

        bool isGUIElement = toHighlight.layer == LayerMask.NameToLayer("UI");
        arrow.transform.SetParent(isGUIElement ? toHighlight.transform : this.currentPanel.transform, false);

        arrow.transform.position = Vector3.zero;
        arrow.transform.localScale = Vector3.one;
        arrow.transform.rotation = Quaternion.identity;

        arrow.layer = toHighlight.layer;

        TutorialHLArrow arrowScript = arrow.GetComponent<TutorialHLArrow>();
        if (arrowScript != null)
        {
            arrowScript.DestroyOnDisable = destroyOnDisable;
            arrowScript.SetClickAmount(clickAmount);
            arrowScript.SetHighlight(toHighlight, fromHighlight);
        }

        this.tutorialHighlights.Add(toHighlight.GetInstanceID(), arrow);
        return arrow;
    }

    public void AddTutorial3DHighlight(GameObject toHighlight, eHighlightType type = eHighlightType.DEFAULT,
                                       bool blue = true, float scale = 1.0f)
    {
        if (toHighlight == null)
        {
            return;
        }

        if (this.tutorialHighlights.ContainsKey(toHighlight.GetInstanceID())) //don't highlight again
        {
            return;
        }

        string path = blue ? "Particles/CFXM3_BuildingHighlight" : "Particles/CFXM3_BuildingHighlight_Red";

        GameObject godRays = (GameObject) Instantiate(Resources.Load(path));

        if (godRays == null)
        {
            return;
        }

        godRays.transform.parent = toHighlight.transform;
        godRays.transform.localPosition = Vector3.zero;
        godRays.transform.localScale = new Vector3(scale, scale, scale);
        godRays.transform.Rotate(0.0f, 0.0f, 0.0f);

        this.tutorialHighlights.Add(toHighlight.GetInstanceID(), godRays);
    }

    public void RemoveTutorialHighlights()
    {
        if (this.tutorialHighlights == null)
        {
            return;
        }

        foreach (KeyValuePair<int, GameObject> pair in this.tutorialHighlights)
        {
            Destroy(pair.Value);
        }

        this.tutorialHighlights.Clear();
    }

    public void RemoveTutorialHighlight(int iInstanceID)
    {
        if (!this.tutorialHighlights.ContainsKey(iInstanceID))
        {
            return;
        }

        Destroy(this.tutorialHighlights[iInstanceID]);
        this.tutorialHighlights.Remove(iInstanceID);
    }

    public void SetLoadingScreenProgress(float progress = 0, bool forceUpdate = false)
    {
        if (OnSetLoadingScreenProgress != null)
        {
            OnSetLoadingScreenProgress(progress, forceUpdate);
        }
    }
}