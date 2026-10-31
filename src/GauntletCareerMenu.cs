using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace BazaarGauntlet
{
    // The native survivor-selection controller owns this UI; its full canvas supplies screen coordinates.
    // There is deliberately no global hotkey, HUD entry, pause-menu entry or main-menu entry.
    internal sealed class GauntletCareerMenu:MonoBehaviour
    {
        private GameObject entry,panel,content;
        private readonly Vector3[] readyCorners=new Vector3[4];
        private TextMeshProUGUI title,subtitle;
        private TMP_FontAsset font;
        private int tab,slot,page;
        private string search="",notice="";
        private LocalUser User=>GetComponent<MPEventSystemLocator>()?.eventSystem?.localUser;
        private GauntletCareer Career=>GauntletMetaStore.For(User);
        internal static void Attach()
        {
            if(Application.isBatchMode || !GauntletMetaNetwork.Selecting)return;
            foreach(var screen in UnityEngine.Object.FindObjectsOfType<CharacterSelectController>())
                if(screen.isActiveAndEnabled && !screen.GetComponent<GauntletCareerMenu>())screen.gameObject.AddComponent<GauntletCareerMenu>();
        }
        private void Start()
        {
            font=GetComponentInChildren<TextMeshProUGUI>()?.font;
            var selectionCanvas=GetComponentInParent<Canvas>();
            var screen=selectionCanvas?selectionCanvas.rootCanvas.gameObject:gameObject;
            entry=Box(screen,"Arena career",Vector2.zero,new Vector2(300,36),new Color(.12f,.16f,.21f,.98f));
            var er=(RectTransform)entry.transform;er.anchorMin=er.anchorMax=Vector2.zero;er.pivot=new Vector2(.5f,1);
            var careerButton=MakeButton(entry,"ARENA CAREER",Vector2.zero,new Vector2(300,36),()=>{if(tab==5)tab=0;panel.SetActive(true);Render();});
            var br=(RectTransform)careerButton.transform;br.anchorMin=Vector2.zero;br.anchorMax=Vector2.one;br.offsetMin=br.offsetMax=Vector2.zero;
            var lr=careerButton.GetComponentInChildren<TextMeshProUGUI>().rectTransform;
            lr.anchorMin=Vector2.zero;lr.anchorMax=Vector2.one;lr.offsetMin=new Vector2(8,4);lr.offsetMax=new Vector2(-8,-4);
            panel=Box(screen,"Arena career panel",Vector2.zero,new Vector2(1080,730),new Color(.025f,.035f,.05f,.99f));
            var pr=(RectTransform)panel.transform;pr.anchorMin=pr.anchorMax=pr.pivot=new Vector2(.5f,.5f);pr.anchoredPosition=Vector2.zero;
            var canvas=panel.AddComponent<Canvas>();canvas.overrideSorting=true;canvas.sortingOrder=100;
            panel.AddComponent<GraphicRaycaster>();panel.AddComponent<MPEventSystemLocator>();panel.AddComponent<CursorOpener>();
            title=Label(panel,"ARENA CAREER",new Vector2(24,-18),new Vector2(850,34),26);
            subtitle=Label(panel,"",new Vector2(24,-59),new Vector2(1020,46),16);
            for(int i=0;i<5;i++){int selected=i;MakeButton(panel,new[]{"PERMANENT UPGRADES","STARTING LOADOUT","ASCENSION","TREASURY","FEATS"}[i],new Vector2(24+i*206,-115),new Vector2(198,38),()=>{tab=selected;page=0;Render();});}
            MakeButton(panel,"CLOSE",new Vector2(946,-20),new Vector2(110,32),()=>panel.SetActive(false));
            content=Box(panel,"Career content",new Vector2(24,-170),new Vector2(1032,530),new Color(0,0,0,0));
            Layer(entry);Layer(panel);panel.SetActive(false);
            PlaceEntry();
        }
        private void LateUpdate()
        {
            if(entry && entry.activeSelf)PlaceEntry();
            // Native item tooltips spawn on their own canvas; keep them above this panel.
            var tip=panel && panel.activeSelf?GetComponent<MPEventSystemLocator>()?.eventSystem?.currentTooltip:null;
            var canvas=tip?tip.tooltipCenterTransform.GetComponentInParent<Canvas>()?.rootCanvas:null;
            int order=panel?panel.GetComponent<Canvas>().sortingOrder:0;
            if(canvas && canvas.sortingOrder<=order)canvas.sortingOrder=order+1;
        }
        private void PlaceEntry()
        {
            if(!entry)return;
            var selection=GetComponent<CharacterSelectController>();
            var ready=selection.readyButton && selection.readyButton.gameObject.activeInHierarchy?selection.readyButton:selection.unreadyButton;
            if(!ready)ready=selection.readyButton;
            var parent=entry.transform.parent as RectTransform;if(!ready || !parent)return;
            ((RectTransform)ready.transform).GetWorldCorners(readyCorners);
            var left=parent.InverseTransformPoint(readyCorners[0]);var right=parent.InverseTransformPoint(readyCorners[3]);
            var rect=(RectTransform)entry.transform;
            // Convert the native Ready button's bottom edge into full-canvas coordinates.
            // Follow its actual layout and width, including the readied state and UI scaling.
            rect.sizeDelta=new Vector2(Mathf.Abs(right.x-left.x),36);
            rect.anchoredPosition=new Vector2((left.x+right.x)*.5f-parent.rect.xMin,Mathf.Min(left.y,right.y)-parent.rect.yMin-8);
        }
        private void Layer(GameObject root){foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=gameObject.layer;}
        private void Update()
        {
            bool visible=isActiveAndEnabled && GauntletMetaNetwork.Selecting && User!=null;
            if(entry)entry.SetActive(visible);
            if(!visible && panel)panel.SetActive(false);
        }
        private GameObject Box(GameObject parent,string name,Vector2 pos,Vector2 size,Color color)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent.transform,false);
            var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=pos;r.sizeDelta=size;
            go.GetComponent<Image>().color=color;go.GetComponent<Image>().raycastTarget=color.a>0;return go;
        }
        private TextMeshProUGUI Label(GameObject parent,string text,Vector2 pos,Vector2 size,float pixels=16)
        {
            var go=new GameObject("Text",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent.transform,false);
            var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=pos;r.sizeDelta=size;
            var label=go.GetComponent<TextMeshProUGUI>();if(font)label.font=font;label.text=text;label.fontSize=pixels;label.color=Color.white;
            label.enableWordWrapping=true;label.overflowMode=TextOverflowModes.Ellipsis;label.raycastTarget=false;
            // Long modded item names shrink to fit their button instead of being cut to "...".
            label.enableAutoSizing=true;label.fontSizeMin=Mathf.Max(9,pixels*.75f);label.fontSizeMax=pixels;return label;
        }
        private MPButton MakeButton(GameObject parent,string text,Vector2 pos,Vector2 size,UnityEngine.Events.UnityAction action,bool enabled=true)
        {
            var go=Box(parent,text,pos,size,new Color(.13f,.18f,.24f,1));go.AddComponent<MPEventSystemLocator>();
            var b=go.AddComponent<MPButton>();b.targetGraphic=go.GetComponent<Image>();b.allowAllEventSystems=true;b.interactable=enabled;b.onClick.AddListener(action);
            var label=Label(go,text,new Vector2(8,-4),size-new Vector2(16,8),15);label.alignment=TextAlignmentOptions.Center;
            return b;
        }
        private void Persist()
        {
            notice=GauntletMetaStore.Save(User)?"Saved. Applies when the next arena run begins.":"Save failed. Check the BepInEx log.";
            GauntletMetaNetwork.Submit(User);Render();
        }
        private void Render()
        {
            foreach(Transform child in content.transform){child.gameObject.SetActive(false);Destroy(child.gameObject);}
            var c=Career;if(c==null){subtitle.text="Career save unavailable. Your existing save has been preserved.";return;}
            title.text=$"ARENA CAREER    <color=#e8c06b>{c.Balance:N0} GLORY</color>";
            subtitle.text="Earn Glory for cleared rounds, even if the run ends in defeat, and once for each Feat. Standard clear: 20 Glory. Ascension clear: up to 50.\n"+notice;
            if(tab==0)Upgrades(c);else if(tab==1)Loadout(c);else if(tab==2)Challenges(c);else if(tab==3)Treasury(c);else if(tab==4)Labors(c);else ConfirmReset(c);
            Layer(content);
        }
        private void Upgrades(GauntletCareer c)
        {
            string[] descriptions={
                "Choose your starters, then unlock equipment, lunar items, reds, a fourth item and a fifth item.\nMaximum: five items + equipment, 21 loadout points, up to two reds. No Glory spent on run loadouts.",
                "Rank 1: start with a free Cinder Warband or Chitin Phalanx. Rank 2: one extra active company per player.\nRank 3: automatically restore the first fallen commander once per run. Company damage stays intact.",
                "Rank 1: one free WHITE item purchase each merchant break. Rank 2: choose WHITE or GREEN.\nYour free purchase is extra: it never uses the round's normal item purchase limits."};
            string[] names={"ARMORY","BARRACKS","PATRONAGE"};
            for(int branch=0;branch<3;branch++)
            {
                int choice=branch,rank=GauntletMetaPolicy.Rank(c,branch);float y=-branch*145;
                Label(content,$"<color=#e8c06b>{names[branch]}</color>   {rank}/{GauntletMetaPolicy.Costs[branch].Length}",new Vector2(4,y),new Vector2(780,30),23);
                Label(content,descriptions[branch],new Vector2(4,y-38),new Vector2(790,88),17);
                int price=rank<GauntletMetaPolicy.Costs[branch].Length?GauntletMetaPolicy.Costs[branch][rank]:0;
                MakeButton(content,price>0?$"UNLOCK\n{price} Glory":"MAXIMUM",new Vector2(834,y-22),new Vector2(190,74),()=>{if(GauntletMetaPolicy.Buy(c,choice))Persist();},price>0 && c.Balance>=price);
            }
            MakeButton(content,"Opening company: "+(c.Company==0?"Cinder Warband":"Chitin Phalanx"),new Vector2(4,-453),new Vector2(410,38),()=>{c.Company=1-c.Company;Persist();},c.Barracks>0);
            MakeButton(content,"<color=#ff8a80>RESET CAREER</color>",new Vector2(834,-453),new Vector2(190,38),()=>{tab=5;Render();});
            Label(content,"Permanent upgrades only affect ArenaGauntlet. Your Glory and choices are saved per local game profile.",new Vector2(4,-503),new Vector2(1000,25),15);
        }
        // Verification screen: the only path to a reset. Tab buttons, Cancel and closing the panel all back out.
        private void ConfirmReset(GauntletCareer c)
        {
            Label(content,"<color=#ff8a80>RESET ARENA CAREER?</color>",new Vector2(4,0),new Vector2(1020,40),27);
            Label(content,"This permanently erases your career on this game profile:\n"+
                $"-  {c.Earned:N0} Glory earned ({c.Balance:N0} unspent)\n"+
                $"-  Armory {c.Armory}, Barracks {c.Barracks} and Patronage {c.Patronage} ranks\n"+
                "-  Your chosen starting loadout and opening company\n"+
                $"-  {c.Cosmetics.Length} Treasury arena skins and decorations\n"+
                $"-  {c.Labors.Length} completed Feats (they can be earned again)\n"+
                "-  Ascension unlocks (highest clear: "+(c.HighestClear<0?"None":c.HighestClear==0?"Standard":"Ascension "+c.HighestClear)+")\n\n"+
                "This cannot be undone.",new Vector2(4,-56),new Vector2(1020,220),20);
            MakeButton(content,"CANCEL",new Vector2(4,-300),new Vector2(300,48),()=>{tab=0;Render();});
            MakeButton(content,"<color=#ff8a80>RESET CAREER</color>",new Vector2(320,-300),new Vector2(300,48),()=>
            {
                notice=GauntletMetaStore.Reset(User)?"Career reset. Earn Glory from cleared rounds to rebuild it.":"Reset failed. Your career was not changed. Check the BepInEx log.";
                tab=0;GauntletMetaNetwork.Submit(User);Render();
            });
        }
        private void Loadout(GauntletCareer c)
        {
            if(c.Armory==0){Label(content,"Unlock Armory I to choose your three starting items. Until then, you still receive three random white/green items.",new Vector2(5,-30),new Vector2(980,100),23);return;}
            int slots=GauntletMetaPolicy.Slots(c.Armory),spent=0,reds=0;
            for(int i=0;i<slots;i++)
            {
                var pickup=GauntletMetaprogression.Resolve(c.Items[i]);int cat=GauntletMetaprogression.Category(pickup);if(cat>=0){spent+=GauntletMetaPolicy.Cost(cat);if(cat==2)reds++;}
            }
            var equip=GauntletMetaprogression.Resolve(c.Equipment,true);int ec=GauntletMetaprogression.Category(equip);if(ec>=0)spent+=GauntletMetaPolicy.Cost(ec);
            Label(content,$"{spent}/{GauntletMetaPolicy.Budget(c.Armory)} loadout points  |  White 1 / Green 2 / Red 5 / Lunar 4 / Equipment 3 (lunar 4)\nBlank or unavailable item slots roll white/green. Enabled run drop pools decide eligibility; modded items are included.",new Vector2(4,0),new Vector2(1020,49),16);
            if(slot>=slots && slot!=5)slot=0;
            for(int i=0;i<slots;i++)
            {
                int current=i;var p=GauntletMetaprogression.Resolve(c.Items[i]);
                Describe(MakeButton(content,(slot==i?"> ":"")+(p==PickupIndex.none?$"ITEM {i+1}: RANDOM":Display(p)),new Vector2(i*172,-59),new Vector2(164,48),()=>{slot=current;page=0;Render();}),p);
            }
            Describe(MakeButton(content,(slot==5?"> ":"")+"EQUIPMENT",new Vector2(860,-59),new Vector2(164,48),()=>{slot=5;page=0;Render();},c.Armory>=2),equip);
            MakeButton(content,"CLEAR SELECTED SLOT",new Vector2(0,-117),new Vector2(242,34),()=>{if(slot==5)c.Equipment="";else c.Items[slot]=null;Persist();});
            MakeButton(content,"PREVIOUS",new Vector2(734,-117),new Vector2(140,34),()=>{page=Math.Max(0,page-1);Render();});
            MakeButton(content,"NEXT",new Vector2(884,-117),new Vector2(140,34),()=>{page++;Render();});
            // A native text field keeps typing inside the selection screen's event system.
            var fieldGo=Box(content,"Search",new Vector2(258,-117),new Vector2(460,34),new Color(.07f,.1f,.14f,1));
            var text=Label(fieldGo,search,new Vector2(8,-3),new Vector2(442,28),16);
            var input=fieldGo.AddComponent<TMP_InputField>();input.textViewport=(RectTransform)fieldGo.transform;input.textComponent=text;input.text=search;
            input.onEndEdit.AddListener(value=>{if(search==value)return;search=value;page=0;Render();});
            if(string.IsNullOrEmpty(search))Label(fieldGo,"Search items...",new Vector2(8,-3),new Vector2(442,28),16).color=new Color(.6f,.65f,.7f);
            var candidates=new List<PickupIndex>();
            if(slot==5)foreach(var index in EquipmentCatalog.allEquipment)candidates.Add(PickupCatalog.FindPickupIndex(index));
            else foreach(var index in ItemCatalog.allItems)candidates.Add(PickupCatalog.FindPickupIndex(index));
            var list=candidates.Where(p=>{int cat=GauntletMetaprogression.Category(p);return cat>=0 && GauntletMetaPolicy.Allowed(c.Armory,cat) &&
                Display(p).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0;}).OrderBy(Display).ToArray();
            page=Math.Min(page,Math.Max(0,(list.Length-1)/24));
            var old=slot==5?equip:GauntletMetaprogression.Resolve(c.Items[slot]);int oldCat=GauntletMetaprogression.Category(old);
            int currentCost=oldCat>=0?GauntletMetaPolicy.Cost(oldCat):0;
            for(int i=0;i<24 && page*24+i<list.Length;i++)
            {
                var p=list[page*24+i];int cat=GauntletMetaprogression.Category(p),cost=GauntletMetaPolicy.Cost(cat);
                bool fits=spent-currentCost+cost<=GauntletMetaPolicy.Budget(c.Armory) && (cat!=2 || reds-(oldCat==2?1:0)<2);
                var button=MakeButton(content,$"{Display(p)}\n{cost} point"+(cost==1?"":"s"),new Vector2(i%4*258,-164-i/4*55),new Vector2(250,49),()=>
                {if(slot==5)c.Equipment=GauntletMetaprogression.Name(p);else c.Items[slot]=GauntletMetaprogression.Name(p);Persist();},fits);
                Describe(button,p);
                var def=PickupCatalog.GetPickupDef(p);var texture=def?.iconTexture;
                if(texture)
                {
                    var icon=new GameObject("Pickup icon",typeof(RectTransform),typeof(RawImage));icon.transform.SetParent(button.transform,false);
                    var r=(RectTransform)icon.transform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,.5f);r.anchoredPosition=new Vector2(4,0);r.sizeDelta=new Vector2(34,34);
                    icon.GetComponent<RawImage>().texture=texture;icon.GetComponent<RawImage>().raycastTarget=false;
                    var label=button.GetComponentInChildren<TextMeshProUGUI>();label.rectTransform.anchoredPosition=new Vector2(42,-4);label.rectTransform.sizeDelta=new Vector2(200,41);
                }
            }
            Label(content,$"Page {page+1} / {Math.Max(1,(list.Length+23)/24)}   |   {list.Length} eligible catalog entries",new Vector2(4,-504),new Vector2(1020,24),15);
        }
        private static string Display(PickupIndex p){var def=PickupCatalog.GetPickupDef(p);return def==null?"Unavailable":Language.GetString(def.nameToken);}
        // LookingGlass is optional, so its per-stack stat calculator is found at runtime instead of referenced.
        private static readonly Lazy<MethodInfo> lookingGlass=new Lazy<MethodInfo>(()=>Chainloader.PluginInfos.TryGetValue("droppod.lookingglass",out var info) && info.Instance?
            info.Instance.GetType().Assembly.GetType("LookingGlass.ItemStatsNameSpace.ItemStats")?.GetMethod("GetItemDescription",new[]{typeof(ItemDef),typeof(int),typeof(CharacterMaster),typeof(bool),typeof(bool)}):null);
        // Hovering shows the native item tooltip: full description with stacking stats, plus LookingGlass's calculated stats when installed.
        private static void Describe(MPButton button,PickupIndex p)
        {
            var def=PickupCatalog.GetPickupDef(p);if(def==null)return;
            var item=ItemCatalog.GetItemDef(def.itemIndex);var equipment=EquipmentCatalog.GetEquipmentDef(def.equipmentIndex);
            string body=null;
            if(item)
            {
                try{body=lookingGlass.Value?.Invoke(null,new object[]{item,1,null,false,false}) as string;}
                catch(Exception error){GauntletPlugin.Log.LogWarning("LookingGlass item stats unavailable; showing the native description. "+error);}
                body=body??Language.GetString(Language.IsTokenInvalid(item.descriptionToken)?item.pickupToken:item.descriptionToken);
            }
            else if(equipment)body=Language.GetString(Language.IsTokenInvalid(equipment.descriptionToken)?equipment.pickupToken:equipment.descriptionToken)+$"\n\nCooldown: {equipment.cooldown:0.#}s";
            if(body==null)return;
            button.gameObject.AddComponent<TooltipProvider>().SetContent(new TooltipContent{titleToken=def.nameToken,titleColor=def.darkColor,overrideBodyText=body});
        }
        private void Challenges(GauntletCareer c)
        {
            bool host=GauntletMetaNetwork.IsHost(User);
            int selected=host?c.Ascension:GauntletMetaNetwork.RemoteAscension;
            Label(content,"ASCENSION "+selected+(selected==0?"  —  STANDARD ARENA":""),new Vector2(4,0),new Vector2(1020,38),27);
            Label(content,"The host chooses the challenge for the party. Clear Standard to unlock Ascension I, then clear each rank to unlock the next.\nProgression stays powerful in Standard. Ascension is optional and keeps the same ten rounds and horde population caps.",new Vector2(4,-49),new Vector2(1020,73),18);
            for(int i=0;i<=6;i++)
            {
                int level=i;bool unlocked=i<=Math.Min(6,c.HighestClear+1);
                MakeButton(content,i==0?"STANDARD":"ASCENSION "+i,new Vector2(i%4*258,-140-i/4*53),new Vector2(250,44),()=>{c.Ascension=level;Persist();},host && unlocked);
            }
            // Must match GauntletMetaPolicy.HealthStacks/DamageStacks/Bosses and GauntletAscension strike count/interval (10% per stack).
            string[] info={"Current arena balance. A full clear earns 20 Glory.",
                "Champions +30% health, hordes +10% health, all enemies +10% damage. No arena strikes. Full clear: 25 Glory.",
                "Champions +60% health, hordes +20% health, all enemies +20% damage. One marked strike every 18 seconds. Full clear: 30 Glory.",
                "Champions +100% health, hordes +30% health, all enemies +30% damage. One marked strike every 15 seconds. Full clear: 35 Glory.",
                "Champions +150% health, hordes +40% health, all enemies +40% damage. Two marked strikes every 12 seconds. Ordinary champion encounters add one more champion. Full clear: 40 Glory.",
                "Champions +220% health, hordes +50% health, all enemies +50% damage. Two marked strikes every 12 seconds. Ordinary champion encounters add one more champion. Full clear: 45 Glory.",
                "Champions +300% health, hordes +60% health, all enemies +70% damage. Three marked strikes every 9 seconds. Ordinary champion encounters add one more champion. Full clear: 50 Glory."};
            Label(content,info[selected]+"\n\nMarked strikes give 2.5 seconds to move. They stop during shopping. Additional health is additive with existing encounter bonuses.\n\nHighest clear: "+(c.HighestClear<0?"None":c.HighestClear==0?"Standard":"Ascension "+c.HighestClear)+". Your existing game difficulty still applies.",new Vector2(4,-274),new Vector2(1016,220),21);
        }
        private void Treasury(GauntletCareer c)
        {
            var catalog=GauntletCosmetics.Catalog;int cost=GauntletMetaPolicy.CosmeticCost;
            bool room=c.Balance>=cost && c.Cosmetics.Length<GauntletMetaPolicy.MaxCosmetics;
            Label(content,$"Cosmetic skins and decorations for the colosseum. {cost} Glory each, kept permanently. They never change gameplay.\n"+
                (GauntletMetaNetwork.IsHost(User)?"Everyone in your run sees your choices.":"Everyone sees the host's choices. Yours show when you host."),new Vector2(4,0),new Vector2(1020,49),17);
            if(catalog.Skins.Length+catalog.Decor.Length==0){Label(content,"No arena styles are available yet. They will arrive in a future update.",new Vector2(4,-80),new Vector2(1020,40),21);return;}
            Label(content,"<color=#e8c06b>ARENA SKIN</color>",new Vector2(4,-62),new Vector2(1020,30),23);
            var skins=new[]{new GauntletCosmetic{Id="",Name="Default arena"}}.Concat(catalog.Skins).ToArray();
            for(int i=0;i<skins.Length;i++)
            {
                var skin=skins[i];bool owned=skin.Id=="" || c.Cosmetics.Contains(skin.Id),selected=c.Skin==skin.Id;
                MakeButton(content,skin.Name+"\n"+(selected?"SELECTED":owned?"SELECT":$"BUY {cost} Glory"),new Vector2(i%4*258,-100-i/4*55),new Vector2(250,49),()=>
                {if(owned || GauntletMetaPolicy.BuyCosmetic(c,skin.Id)){c.Skin=skin.Id;Persist();}},!selected && (owned || room));
            }
            float top=-100-(skins.Length+3)/4*55-12;
            if(catalog.Decor.Length>0)Label(content,"<color=#e8c06b>DECORATIONS</color>",new Vector2(4,top),new Vector2(1020,30),23);
            for(int i=0;i<catalog.Decor.Length;i++)
            {
                var item=catalog.Decor[i];bool owned=c.Cosmetics.Contains(item.Id),shown=c.Decor.Contains(item.Id);
                MakeButton(content,item.Name+"\n"+(shown?"SHOWN":owned?"HIDDEN":$"BUY {cost} Glory"),new Vector2(i%4*258,top-38-i/4*55),new Vector2(250,49),()=>
                {if((owned || GauntletMetaPolicy.BuyCosmetic(c,item.Id)) && GauntletMetaPolicy.ShowDecor(c,item.Id,!shown))Persist();},owned || room);
            }
        }
        private void Labors(GauntletCareer c)
        {
            var all=GauntletMetaPolicy.Labors;
            Label(content,$"FEATS OF THE ARENA    <color=#e8c06b>{all.Count(l=>c.Labors.Contains(l.Id))}/{all.Length} complete</color>",new Vector2(4,0),new Vector2(1020,34),25);
            Label(content,"Feats of the arena. Each pays its Glory once, as soon as you achieve it. \"In one run\" counts must be reached within a single run.",new Vector2(4,-36),new Vector2(1020,24),16);
            for(int i=0;i<all.Length;i++)
            {
                var labor=all[i];bool complete=c.Labors.Contains(labor.Id);
                var box=Box(content,labor.Name,new Vector2(i%2*518,-68-i/2*76),new Vector2(510,70),complete?new Color(.13f,.2f,.11f,1):new Color(.08f,.11f,.15f,1));
                Label(box,(complete?"<color=#9be37a>COMPLETE</color>   ":$"<color=#e8c06b>+{labor.Glory} Glory</color>   ")+labor.Name,new Vector2(10,-6),new Vector2(490,26),19);
                Label(box,labor.Text,new Vector2(10,-34),new Vector2(490,32),15);
            }
        }
        private void OnDisable(){if(entry)entry.SetActive(false);if(panel)panel.SetActive(false);}
        private void OnDestroy(){if(entry)Destroy(entry);if(panel)Destroy(panel);}
    }
}
