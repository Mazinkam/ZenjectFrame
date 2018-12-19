using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public abstract class BaseWindow<T> : BaseWindow where T : BaseWindow<T>
{
    public static T Instance { get; private set; }

    public static void Show()
    {
        CreateInstance();
        Instance.Open();
    }

    public static void Hide()
    {
        if (Instance == null)
        {
            return;
        }

        Instance.Close();
    }

    protected override void Awake()
    {
        base.Awake();
        RegisterSingleton();
    }

    protected override void OnDestroy()
    {
        UnregisterSingleton();
    }

    protected void RegisterSingleton()
    {
        if (Instance != null)
        {
            Debug.LogError(
                "Trying to register another " + typeof(T) +
                ", destroying the new instance gameobject. The existing instance is " + Instance, Instance);
            Destroy(this.gameObject);
        }
        else
        {
            Instance = this as T;
        }
    }

    protected void UnregisterSingleton()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        else if (Instance == null)
        {
            Debug.LogError("UnregisterSingleton called for " + typeof(T) + ", instance unregistered already");
        }
    }

    protected static void CreateInstance()
    {
        if (GUIManager.Instance == null)
        {
            return;
        }

        if (Instance == null)
        {
            GUIManager.Instance.Instantiate<T>();
        }
        else
        {
            Instance.ToggleCanvas(true);
        }
    }
}

public abstract class BaseWindow : MonoBehaviour
{
    [Header("Base window")] public Canvas Canvas;
    public RectTransform Content;

    [Tooltip("Destroy game object instead of disabling when closed.")]
    public bool DestroyWhenClosed = true;

    [Tooltip("Pushes menu to stack, auto sets sortoder, supports back button if enabled.")]
    public bool PushToStack = true;

    [Tooltip("If selected, won't disable menus under it. Mostly used for gameplay and dialog menus")]
    public bool Overlay;

    [Tooltip("Closes or disables current menu, overrwrite SendBackButtonClick if needed more functionality")]
    public bool SupportsBackButton = true;

    public int SortingOrder
    {
        get { return this.Canvas.sortingOrder; }
        set { this.Canvas.sortingOrder = value; }
    }

    private SafeAreaMargins _safeAreaMargins;
    private float canvasWidth;

    protected virtual void Awake()
    {
        this.Canvas = GetComponentInChildren<Canvas>();
        this._safeAreaMargins = GetComponentInChildren<SafeAreaMargins>();

        ToggleCanvas(false);

        this.Canvas.renderMode = RenderMode.ScreenSpaceCamera;
        this.Canvas.worldCamera = GUIManager.Instance.RenderCamera;

        if (this._safeAreaMargins != null)
        {
            this.Content = this._safeAreaMargins.gameObject.GetComponent<RectTransform>();
            this._safeAreaMargins.SetSafeAreaAnchors();
            this._safeAreaMargins.SetSafeAreaDimensions();
        }

        if (this.Overlay)
        {
            DOVirtual.DelayedCall(0.2f, () => ToggleCanvas(true));
        }
    }

    protected virtual void OnDestroy()
    {
    }

    protected virtual void Open()
    {
        if (GUIManager.Instance == null)
        {
            return;
        }

        GUIManager.Instance.AddBaseWindow(this);
        OpenFinished();
    }

    protected virtual void Close()
    {
        if (GUIManager.Instance == null)
        {
            return;
        }

        GUIManager.Instance.RemoveBaseWindow(this);
        CloseFinished();
    }
    
    protected void OpenFinished()
    {
        bool isPanel = this.PushToStack && !this.Overlay;
        bool isSocialPanelDialog = !this.PushToStack && !this.Overlay;

        if (this.Content != null
            && (isPanel || isSocialPanelDialog)
            && GUIManager.PreviousPanel.Value != GUIManager.Panel.InGame
            && GUIManager.PreviousPanel.Value != GUIManager.Panel.BattleResultsPanel)
        {
            HandlePanelAnimationIn();
        }
        else
        {
            GUIManager.Instance.isAnimatingMenu = false;
        }

        if (!this.Overlay)
        {
            ToggleCanvas(true);
        }
    }

    protected void CloseFinished()
    {
        if (this == null)
        {
            return;
        }

        bool isPanel = this.PushToStack && !this.Overlay;
        bool isSocialPanelDialog = !this.PushToStack && !this.Overlay;

        if (isPanel || isSocialPanelDialog)
        {
            HandlePanelAnimationOut();
        }
        else
        {
            DestroyOrDisable();
        }
    }

    private void HandlePanelAnimationIn()
    {
        this.canvasWidth = this.Canvas.GetComponent<RectTransform>().rect.width + 100;
        float startingPosition = GUIManager.Instance.AnimationDirection * this.canvasWidth;
        this.Content.offsetMin = new Vector2(startingPosition, 0); // new Vector2(left, bottom);
        this.Content.offsetMax = new Vector2(startingPosition, 0); // new Vector2(-right, -top);
       
        DOTween.To(() => this.Content.offsetMin, x => this.Content.offsetMin = x, Vector2.zero, 0.5f)
               .SetEase(GUIManager.Instance.PanelEaseTypeIn);
        DOTween.To(() => this.Content.offsetMax, x => this.Content.offsetMax = x, Vector2.zero, 0.5f)
               .SetEase(GUIManager.Instance.PanelEaseTypeIn).OnComplete(
                   () => { FinishPanelInAnimation(); });
    }

    private void FinishPanelInAnimation()
    {
        GUIManager.Instance.isAnimatingMenu = false;
        
        if (!this.PushToStack)
        {
            return;
        }

        this.Canvas.sortingOrder--;
    }

    private void HandlePanelAnimationOut()
    {
        float animationDirection = -this.canvasWidth * GUIManager.Instance.AnimationDirection;
        GUIManager.Instance.isAnimatingMenu = true;
        DOTween.To(() => this.Content.offsetMin, x => this.Content.offsetMin = x,
                   new Vector2(animationDirection, 0), 0.5f)
               .SetEase(GUIManager.Instance.PanelEaseTypeOut);
        DOTween.To(() => this.Content.offsetMax, x => this.Content.offsetMax = x,
                   new Vector2(animationDirection, 0), 0.5f)
               .SetEase(GUIManager.Instance.PanelEaseTypeOut).OnComplete(
                   () => { DestroyOrDisable(); });
    }

    private void DestroyOrDisable()
    {
        GUIManager.Instance.RemoveBaseWindow(this);
        GUIManager.Instance.isSwiping = false;

        if (this.DestroyWhenClosed && !GUIManager.Instance.IsInStack(this))
        {
            Destroy(this.gameObject);
        }
        else
        {
            // Turn off canvas to improve performance if this menu should not be destroyed
            ToggleCanvas(false);
        }
    }

    public virtual void SendBackButtonClick()
    {
        if (!this.SupportsBackButton)
        {
            return;
        }

        if (MiniLoadingDialog.Instance != null)
        {
            return;
        }

        if (GUIManager.Instance.TopmostPageTypeMatches(ConnectingDialog.Instance))
        {
            ConnectingDialog.Instance.CancelMatchMaking();
        }

        Close();
    }

    public void ToggleCanvas(bool isEnabled)
    {
        this.Canvas.enabled = isEnabled;
    }
}