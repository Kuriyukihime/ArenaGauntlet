using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarGauntlet
{
    internal sealed class GauntletHud : MonoBehaviour
    {
        private sealed class Row
        {
            internal int Commander;internal Image Background,Button;internal MPButton Action;
            internal TextMeshProUGUI Name,Detail,Label,Tier;internal float ConfirmUntil;
        }
        private static readonly string[] Numerals={"I","II","III","IV","V"};
        private GameObject panel,council,brazierArrow,brazierTarget;
        private TextMeshProUGUI heading, hint, coreLabel, renownLabel, councilStatus;
        private Image coreBar;
        private float nextUpdate;
        private readonly List<Row> rows=new List<Row>();
        internal static void Attach()
        {
            if (!GauntletController.Current || Application.isBatchMode) return;
            foreach (var hud in HUD.readOnlyInstanceList)
                if (hud && !hud.GetComponent<GauntletHud>()) hud.gameObject.AddComponent<GauntletHud>();
        }
        private void Start()
        {
            var hud = GetComponent<HUD>();
            if (!hud || !hud.mainContainer) return;
            panel = new GameObject("BazaarGauntletStatus", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)panel.transform;rect.SetParent(hud.mainContainer.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(0, -18);rect.sizeDelta = new Vector2(520, 68);
            var background = panel.GetComponent<Image>();background.color = new Color(.025f, .035f, .055f, .76f);background.raycastTarget = false;
            heading = Label(panel,"Round", new Vector2(12, -4), new Vector2(280, 21), 16);
            hint = Label(panel,"Next", new Vector2(12, -25), new Vector2(496, 17), 12);
            coreLabel=Label(panel,"Core",new Vector2(302,-4),new Vector2(206,21),16);
            coreLabel.alignment=TextAlignmentOptions.Right;
            renownLabel=Label(panel,"Renown",new Vector2(12,-45),new Vector2(496,17),12);renownLabel.color=new Color(.91f,.75f,.42f);
            var coreObject=new GameObject("Core health",typeof(RectTransform),typeof(Image));coreObject.transform.SetParent(panel.transform,false);
            var cr=(RectTransform)coreObject.transform;cr.anchorMin=new Vector2(0,0);cr.anchorMax=new Vector2(1,0);cr.pivot=new Vector2(0,0);cr.anchoredPosition=Vector2.zero;cr.sizeDelta=new Vector2(0,3);
            coreBar=coreObject.GetComponent<Image>();coreBar.color=new Color(.35f,.83f,1);coreBar.raycastTarget=false;
            BuildCouncil(hud);
            BuildBrazierArrow(hud);
        }
        private void BuildBrazierArrow(HUD hud)
        {
            // Native PositionIndicator handles both the on-screen target and
            // a rotating screen-edge arrow when the brazier is behind us.
            brazierTarget=new GameObject("Brazier arrow target");
            brazierTarget.transform.position=GauntletShop.Position(GauntletShop.Ready);
            brazierArrow=new GameObject("Brazier direction",typeof(RectTransform));brazierArrow.SetActive(false);
            brazierArrow.transform.SetParent(hud.mainContainer.transform,false);
            var indicator=brazierArrow.AddComponent<PositionIndicator>();indicator.targetTransform=brazierTarget.transform;
            indicator.shouldRotateOutsideViewObject=true;
            brazierArrow.AddComponent<GauntletHudMarker>().Hud=hud;
            for(int i=0;i<2;i++)
            {
                var arrow=new GameObject(i==0?"On-screen arrow":"Screen-edge arrow",typeof(RectTransform),typeof(GauntletArrowGraphic),typeof(Outline));
                arrow.transform.SetParent(brazierArrow.transform,false);
                var rect=(RectTransform)arrow.transform;rect.sizeDelta=new Vector2(36,28);
                var graphic=arrow.GetComponent<GauntletArrowGraphic>();graphic.color=new Color(1,.82f,.3f);graphic.raycastTarget=false;
                var outline=arrow.GetComponent<Outline>();outline.effectColor=new Color(0,0,0,.9f);outline.effectDistance=new Vector2(1.5f,-1.5f);
                if(i==0){indicator.insideViewObject=arrow;rect.localRotation=Quaternion.Euler(0,0,-90);}
                else indicator.outsideViewObject=arrow;
            }
            foreach(var child in brazierArrow.GetComponentsInChildren<Transform>(true))child.gameObject.layer=hud.mainContainer.layer;
        }
        private TextMeshProUGUI Label(GameObject parent,string name, Vector2 position, Vector2 size, float fontSize)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));obj.transform.SetParent(parent.transform, false);
            var rect = (RectTransform)obj.transform;rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position;rect.sizeDelta = size;
            var text = obj.GetComponent<TextMeshProUGUI>();text.fontSize = fontSize;text.color = Color.white;
            text.fontStyle = name == "Round" || name=="Name" || name=="Title" ? FontStyles.Bold : FontStyles.Normal;
            text.enableWordWrapping = false;text.overflowMode = TextOverflowModes.Ellipsis;text.raycastTarget = false;
            // Long modded boss/theme names shrink to fit instead of being cut to "...".
            text.enableAutoSizing = true;text.fontSizeMin = Mathf.Max(9, fontSize * .75f);text.fontSizeMax = fontSize;
            var native = GetComponent<HUD>().GetComponentInChildren<TextMeshProUGUI>();if (native && native.font) text.font = native.font;
            return text;
        }
        private static Image Box(GameObject parent,string name,Vector2 position,Vector2 size,Color color,bool raycast)
        {
            var obj=new GameObject(name,typeof(RectTransform),typeof(Image));obj.transform.SetParent(parent.transform,false);
            var rect=(RectTransform)obj.transform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=position;rect.sizeDelta=size;
            var image=obj.GetComponent<Image>();image.color=color;image.raycastTarget=raycast;return image;
        }
        private MPButton Button(GameObject parent,string name,Vector2 position,Vector2 size,out Image image,out TextMeshProUGUI label,UnityEngine.Events.UnityAction action)
        {
            image=Box(parent,name,position,size,new Color(.16f,.2f,.26f,.95f),true);
            image.gameObject.AddComponent<MPEventSystemLocator>();
            var button=image.gameObject.AddComponent<MPButton>();button.targetGraphic=image;button.allowAllEventSystems=true;
            button.onClick.AddListener(action);
            label=Label(image.gameObject,"Label",new Vector2(0,0),size,12);label.alignment=TextAlignmentOptions.Center;
            return button;
        }
        // The council opens under the round strip. Its native cursor opener and
        // event-system locator use this HUD's local player, like other HUD menus.
        private void BuildCouncil(HUD hud)
        {
            council=new GameObject("BazaarGauntletWarCouncil",typeof(RectTransform),typeof(Image));council.SetActive(false);
            var rect=(RectTransform)council.transform;rect.SetParent(hud.mainContainer.transform,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,-92);
            int count=GauntletCommandRoster.Commanders.Length;rect.sizeDelta=new Vector2(560,64+count*44);
            var background=council.GetComponent<Image>();background.color=new Color(.02f,.03f,.045f,.9f);background.raycastTarget=true;
            council.AddComponent<Canvas>();council.AddComponent<GraphicRaycaster>();
            council.AddComponent<MPEventSystemLocator>();council.AddComponent<CursorOpener>();
            Label(council,"Title",new Vector2(14,-8),new Vector2(300,22),17).text="WAR COUNCIL";
            councilStatus=Label(council,"Status",new Vector2(14,-32),new Vector2(400,18),12);councilStatus.color=new Color(.91f,.75f,.42f);
            Button(council,"Close",new Vector2(446,-10),new Vector2(100,26),out _,out var close,()=>Toggle(false));
            close.text="Close";
            for(int i=0;i<count;i++)
            {
                var def=GauntletCommandRoster.Commanders[i];float y=-58-i*44;
                var row=new Row{Commander=i};
                row.Background=Box(council,"Commander "+def.Name,new Vector2(8,y),new Vector2(544,40),new Color(.07f,.09f,.12f,.85f),false);
                Box(row.Background.gameObject,"Banner",new Vector2(0,0),new Vector2(5,40),new Color(def.R,def.G,def.B),false);
                row.Tier=Label(row.Background.gameObject,"Tier",new Vector2(10,-10),new Vector2(30,20),14);row.Tier.alignment=TextAlignmentOptions.Center;row.Tier.text=Numerals[def.Tier-1];
                row.Name=Label(row.Background.gameObject,"Name",new Vector2(44,-3),new Vector2(360,19),13);
                row.Detail=Label(row.Background.gameObject,"Detail",new Vector2(44,-21),new Vector2(360,17),11);row.Detail.color=new Color(.7f,.78f,.84f);
                int index=i;
                row.Action=Button(row.Background.gameObject,"Action",new Vector2(410,-6),new Vector2(126,28),out row.Button,out row.Label,()=>Pressed(index));
                rows.Add(row);
            }
            // A nested canvas is culled by its own layer, and the native UI camera
            // draws only the HUD's UI layer; on Default the council is invisible.
            foreach(var child in council.GetComponentsInChildren<Transform>(true))child.gameObject.layer=hud.mainContainer.layer;
        }
        private LocalUser User=>GetComponent<HUD>()?.localUserViewer;
        // KeyboardShortcut.IsDown ignores the press while any other key is held
        // (WASD, sprint, jump); only the bound key and its own modifiers matter here.
        private static bool Pressed(BepInEx.Configuration.KeyboardShortcut key)=>
            key.MainKey!=KeyCode.None && BepInEx.UnityInput.Current.GetKeyDown(key.MainKey) && key.Modifiers.All(BepInEx.UnityInput.Current.GetKey);
        private void Toggle(bool open)
        {
            if(!council)return;
            council.SetActive(open);
            foreach(var row in rows)row.ConfirmUntil=0;
            nextUpdate=0;
        }
        private void Pressed(int commander)
        {
            var owner=GauntletController.Current;var user=User;if(!owner || user==null)return;
            var view=owner.Companies.For(user.cachedMaster);
            var company=view?.Companies.FirstOrDefault(value=>value.Commander==commander);
            var row=rows[commander];
            if(company!=null && !company.Fallen)
            {
                // Discharging forfeits the hire cost, so it needs a second press.
                if(Time.unscaledTime>row.ConfirmUntil){row.ConfirmUntil=Time.unscaledTime+3;nextUpdate=0;return;}
                row.ConfirmUntil=0;GauntletCompanies.Request(user,commander,true);
            }
            else GauntletCompanies.Request(user,commander,false);
            Util.PlaySound("Play_UI_menuClick",RoR2Application.instance.gameObject);
            nextUpdate=0;
        }
        private void Update()
        {
            if (!panel) return;
            var owner = GauntletController.Current;
            var user=User;
            if(owner && council && user!=null && user==LocalUserManager.GetFirstLocalUser() && Pressed(GauntletConfig.CommandMenuKey.Value))
            {
                // Typing in chat or the console must not toggle the council.
                var selected=user.eventSystem?user.eventSystem.currentSelectedGameObject:null;
                if(!selected || !selected.GetComponent<TMP_InputField>())Toggle(!council.activeSelf);
            }
            if (Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + .1f;
            panel.SetActive(owner && GauntletConfig.ShowHud.Value);
            if(council && (!owner || !GauntletConfig.ShowHud.Value) && council.activeSelf)Toggle(false);
            if(brazierArrow)brazierArrow.SetActive(owner && !owner.ShuttingDown && owner.Fortress.InCastle && GauntletConfig.ShowHud.Value &&
                (UnityEngine.Networking.NetworkServer.active?owner.Flow.Phase:owner.RemotePhase)==GauntletPhase.AwaitingExit);
            if (!owner) return;
            bool host = UnityEngine.Networking.NetworkServer.active;
            int round = host ? owner.Flow.Round : owner.RemoteRound, cycle = host ? owner.Flow.Cycle : owner.RemoteCycle;
            var phase = host ? owner.Flow.Phase : owner.RemotePhase;
            bool combat = !owner.Fortress.WaitingForStart && (phase == GauntletPhase.Horde || phase == GauntletPhase.Boss);
            bool shopping=GauntletRounds.IsBazaar;
            string phaseLabel=combat?(phase==GauntletPhase.Horde?$"HORDE {owner.Defeated}/{owner.SpawnQuota}":owner.SpawnQuota>1?$"CHAMPIONS {owner.Defeated}/{owner.SpawnQuota}":"CHAMPION"):
                phase==GauntletPhase.FinalDecision?"VICTORY":shopping?"BAZAAR":phase==GauntletPhase.AwaitingExit?"MERCHANTS OPEN":"PREPARATION";
            heading.text=$"ROUND {round:00}/{GauntletPlan.RoundsPerCycle}  |  {phaseLabel}";
            hint.text=phase==GauntletPhase.AwaitingExit?"Interact with the brazier to start the next round.":combat?(phase==GauntletPhase.Horde && owner.Spawned>=owner.SpawnQuota && owner.SpawnQuota-owner.Defeated<=5?"Last enemies marked. Finish the horde.":owner.ThemeName):"Combat begins automatically after players arrive.";
            if(!combat && owner.ThemeName.StartsWith("Retrying "))hint.text=owner.ThemeName+" - combat is paused until setup succeeds.";
            if(cycle>1)hint.text+=$"  |  Cycle {cycle}";
            coreLabel.text=combat?$"ENEMY LV {owner.Run.ambientLevelFloor:0}":phase==GauntletPhase.AwaitingExit?owner.ThemeName:"COLOSSEUM";
            float progress=combat && phase==GauntletPhase.Horde && owner.SpawnQuota>0?Mathf.Clamp01((float)owner.Defeated/owner.SpawnQuota):1;
            coreBar.rectTransform.anchorMax=new Vector2(progress,0);coreBar.color=new Color(.84f,.68f,.36f);
            var view=owner.Companies.For(user?.cachedMaster);
            long renown=view?.Renown??0;int active=view?.Companies.Count(company=>!company.Fallen)??0,capacity=view?.Capacity??owner.Companies.Capacity;
            if(owner.Meta.Ascension>0)hint.text+=$"  |  Ascension {owner.Meta.Ascension}";
            if(phase==GauntletPhase.AwaitingExit && user?.cachedMaster && owner.Meta.Remote.TryGetValue(user.cachedMaster.netId.Value,out var career) && career.Patronage>0)
                hint.text=career.Patronage>=2?"Patronage: one free white or green. Ready at the brazier when finished.":"Patronage: one free white. Ready at the brazier when finished.";
            string key=GauntletConfig.CommandMenuKey.Value.MainKey.ToString();
            renownLabel.text=$"RENOWN {renown:N0}   ·   COMPANIES {active}/{capacity}   ·   [{key}] WAR COUNCIL";
            if(council && council.activeSelf)RefreshCouncil(owner,view,renown,active,capacity,round,cycle,key);
        }
        private void RefreshCouncil(GauntletController owner,GauntletCompanies.View view,long renown,int active,int capacity,int round,int cycle,string key)
        {
            councilStatus.text=$"Renown {renown:N0}  ·  Companies {active}/{capacity}  ·  Kills and champions earn renown.";
            foreach(var row in rows)
            {
                var def=GauntletCommandRoster.Commanders[row.Commander];
                var company=view?.Companies.FirstOrDefault(value=>value.Commander==row.Commander);
                bool commanding=company!=null && !company.Fallen,fallen=company!=null && company.Fallen;
                bool available=GauntletCompanies.Available(row.Commander,owner.Run),unlocked=GauntletCommandRoster.Unlocked(def.Tier,round,cycle);
                row.Name.text=$"{def.Name}  <size=85%><color=#9fb3bf>{def.LeaderName} + {def.SquadSize} {def.SquadName}</color></size>";
                var invalid=UnityEngine.Networking.NetworkInstanceId.Invalid;
                string state=commanding?(company.Units.Length>0 && company.Units[0]!=invalid?
                    $"  ·  <color=#7fd38a>In the field, {company.Units.Skip(1).Count(unit=>unit!=invalid)}/{def.SquadSize} squad</color>":"  ·  <color=#7fd38a>Marching with you</color>"):
                    fallen?"  ·  <color=#d9785f>Commander fallen</color>":"";
                row.Detail.text=$"{def.AbilityName}: {def.AbilityText}{state}";
                bool enabled;string label;
                if(commanding){enabled=true;label=Time.unscaledTime<row.ConfirmUntil?"Confirm discharge":"Discharge";}
                else if(!available){enabled=false;label="Unavailable";}
                else if(!unlocked){enabled=false;label="Round "+GauntletCommandRoster.UnlockRound(def.Tier);}
                else if(active>=capacity){enabled=false;label="Ranks full";}
                else {enabled=renown>=def.Cost;label=(fallen?"Rehire ":"Hire ")+def.Cost.ToString("N0");}
                row.Action.interactable=enabled;row.Label.text=label;
                row.Button.color=commanding?new Color(.32f,.16f,.14f,.95f):enabled?new Color(.42f,.31f,.12f,.95f):new Color(.12f,.14f,.17f,.9f);
                row.Label.color=enabled?Color.white:new Color(.55f,.6f,.65f);
                row.Background.color=commanding?new Color(.09f,.13f,.1f,.88f):new Color(.07f,.09f,.12f,.85f);
            }
        }
        private void OnDestroy() { if (panel) Destroy(panel); if(council) Destroy(council); if(brazierArrow)Destroy(brazierArrow); if(brazierTarget)Destroy(brazierTarget); }
    }
}
