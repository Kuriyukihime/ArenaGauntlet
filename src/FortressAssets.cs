using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using R2API;
using RoR2;
using RoR2.CharacterAI;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BazaarGauntlet
{
    internal static class FortressAssets
    {
        private static readonly Dictionary<string,string> keys=new Dictionary<string,string>();
        private static readonly Dictionary<string,UnityEngine.Object> loaded=new Dictionary<string,UnityEngine.Object>();
        private static readonly GameObject[] masters=new GameObject[3];
        internal static GameObject ForModule(int index)=>masters[index==0?0:index<=4?1:2];
        internal static T Load<T>(string field) where T:UnityEngine.Object
        {
            if(loaded.TryGetValue(field,out var cached)) return cached as T;
            if(keys.Count==0)
                foreach(var type in typeof(RoR2BepInExPack.GameAssetPathsBetter.RoR2_Base_Engi).Assembly.GetTypes().Where(type=>type.Namespace=="RoR2BepInExPack.GameAssetPathsBetter" && !type.Name.Contains("_Junk_")))
                    foreach(var member in type.GetFields(BindingFlags.Public|BindingFlags.Static))
                        if(member.FieldType==typeof(string) && !keys.ContainsKey(member.Name)) keys[member.Name]=member.GetValue(null) as string;
            if(!keys.TryGetValue(field,out string key) || string.IsNullOrEmpty(key)) return null;
            foreach(var locator in Addressables.ResourceLocators)
                if(locator.Locate(key,typeof(T),out var locations) && locations.Count>0)
                {
                    var result=Addressables.LoadAssetAsync<T>(locations[0]).WaitForCompletion();
                    if(result) loaded[field]=result;
                    return result;
                }
            return null;
        }
        internal static void Register()
        {
            var bodySource=Load<GameObject>("EngiTurretBody_prefab");
            var masterSource=Load<GameObject>("EngiTurretMaster_prefab");
            if(!bodySource || !masterSource) throw new InvalidOperationException("Native fortress body templates are unavailable.");
            for(int kind=0;kind<masters.Length;kind++)RegisterKind(bodySource,masterSource,kind);
        }
        private static void RegisterKind(GameObject bodySource,GameObject masterSource,int kind)
        {
            var body=PrefabAPI.InstantiateClone(bodySource,"BazaarGauntletFortressBody"+kind);
            var character=body.GetComponent<CharacterBody>();
            character.baseNameToken="GAUNTLET_FORTRESS_STRUCTURE";
            LanguageAPI.Add(character.baseNameToken,"Fortress structure");
            character.baseMaxHealth=4000; character.levelMaxHealth=0; character.baseRegen=0; character.levelRegen=0;
            character.baseArmor=10; character.baseMoveSpeed=0;
            // Motorless turret bodies report isFlying unless explicitly marked.
            // Ground-only native attack/chase drivers must accept structures.
            character.isNotFlying=true;
            character.bodyFlags &= ~CharacterBody.BodyFlags.UsesAmbientLevel;
            character.bodyFlags |= CharacterBody.BodyFlags.ImmuneToExecutes|CharacterBody.BodyFlags.ImmuneToVoidDeath|CharacterBody.BodyFlags.IgnoreFallDamage|
                CharacterBody.BodyFlags.Unmovable|CharacterBody.BodyFlags.Ungrabbable|CharacterBody.BodyFlags.IgnoreKnockup;
            var team=body.GetComponent<TeamComponent>();if(team){team.hideAllyCardDisplay=true;team.hideIndicator=true;}
            foreach(var renderer in body.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff=true;
            var model=body.GetComponent<ModelLocator>().modelTransform;
            var group=model.GetComponent<HurtBoxGroup>();
            foreach(var old in body.GetComponentsInChildren<HurtBox>(true))
            { old.enabled=false;var collider=old.GetComponent<Collider>();if(collider)collider.enabled=false; }
            Vector3 size=kind==0?new Vector3(5.6f,6,5.6f):kind==1?new Vector3(15,3.4f,1.8f):new Vector3(4.5f,4,4.5f);
            var target=new GameObject("Fortress target",typeof(BoxCollider));target.layer=LayerIndex.entityPrecise.intVal;
            target.transform.SetParent(body.transform,false);target.transform.localPosition=Vector3.up*size.y*.5f;
            target.GetComponent<BoxCollider>().size=size;
            var hurt=target.AddComponent<HurtBox>();hurt.healthComponent=body.GetComponent<HealthComponent>();hurt.isBullseye=true;hurt.hurtBoxGroup=group;hurt.indexInGroup=0;
            group.hurtBoxes=new[]{hurt};group.mainHurtBox=hurt;group.bullseyeCount=1;
            // Movement collision follows the rendered architecture, not a solid
            // box across its doors. Damage still uses the registered hurtbox.
            var rigid=body.GetComponent<Rigidbody>();if(rigid){rigid.isKinematic=true;rigid.useGravity=false;}
            foreach(var machine in body.GetComponents<EntityStateMachine>())
                if(machine.customName=="Body")
                { machine.initialStateType=new EntityStates.SerializableEntityStateType(typeof(EntityStates.Idle)); machine.mainStateType=machine.initialStateType; }
            body.AddComponent<FortressBodyMarker>();
            var master=PrefabAPI.InstantiateClone(masterSource,"BazaarGauntletFortressMaster"+kind);
            master.GetComponent<CharacterMaster>().bodyPrefab=body;
            foreach(var brain in master.GetComponents<BaseAI>()) brain.enabled=false;
            masters[kind]=master;
            ContentAddition.AddBody(body); ContentAddition.AddMaster(master);
        }
    }
    internal sealed class FortressBodyMarker:MonoBehaviour
    {
        private void Awake(){HideNativeModel();}
        private void Start(){HideNativeModel();}
        private void HideNativeModel()
        {
            // forceRenderingOff on the template is not a serialized prefab
            // setting. Apply it to each actual spawned network body as well.
            foreach(var renderer in GetComponentsInChildren<Renderer>(true))renderer.forceRenderingOff=true;
        }
    }
}
